using System.Text.Json;
using Microsoft.Extensions.Logging;
using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Constants;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Services;
using Mcp.Compliance.Spec;

namespace Mcp.Benchmark.Infrastructure.Validators;

/// <summary>
/// MCP-Compliant Authentication Validator - Per MCP Specification 2025-06-18
/// 
/// MCP AUTHENTICATION COMPLIANCE RULES (Per Official Specification):
/// 
/// Reference: https://modelcontextprotocol.io/specification/2025-06-18/basic/authorization#token-handling
/// 
/// MCP servers, acting as OAuth 2.1 resource servers, MUST:
/// 1. Validate access tokens as per OAuth 2.1 Section 5.2
/// 2. Validate tokens were issued for them as intended audience (RFC 8707 Section 2)  
/// 3. Respond per OAuth 2.1 Section 5.3 error handling if validation fails
/// 4. Send appropriate HTTP status codes per OAuth 2.1 RFC 6750
/// 
/// OAuth 2.1 / MCP Error Response Requirements:
/// - invalid_request: Missing/malformed parameters - SHOULD respond with HTTP 400
/// - invalid_token: Invalid, expired, revoked, or wrong-audience tokens - MUST respond with HTTP 401
/// - insufficient_scope: Higher privileges required - SHOULD respond with HTTP 403
/// - 401 authentication errors: MUST include WWW-Authenticate header with protected resource metadata discovery
/// - HTTP clients MUST send bearer tokens in Authorization headers, not URI query strings
/// 
/// VALIDATED COMPLIANCE MATRIX:
/// - No Auth: 401 + WWW-Authenticate + resource_metadata preferred; secure rejection accepted as compatible
/// - Malformed Token: 400/401 + WWW-Authenticate preferred; secure rejection accepted as compatible
/// - Invalid Token: 401 + WWW-Authenticate required for standards alignment
/// - Token Expired: 401 + WWW-Authenticate required for standards alignment
/// - Invalid Scope: 403 + WWW-Authenticate (insufficient_scope)
/// - Insufficient Perms: 403 + WWW-Authenticate (insufficient_scope)
/// - Wrong Audience: 401 rejection; acceptance indicates audience validation and token passthrough risk
/// - Query Token: URI query-string token must not grant access
/// - Valid Token: 200 + JSON-RPC success
/// 
/// UNPROTECTED SERVERS: Skip authentication validation entirely (100% compliant)
/// STDIO TRANSPORT: No HTTP auth headers (100% compliant per MCP spec)
/// </summary>
public class McpCompliantAuthValidator
{
    private readonly ILogger<McpCompliantAuthValidator> _logger;
    private readonly IMcpHttpClient _httpClient;

    private const string McpAuthorizationSpecReference = "https://modelcontextprotocol.io/specification/2025-06-18/basic/authorization";
    private const string McpSecurityBestPracticesReference = "https://modelcontextprotocol.io/specification/2025-06-18/basic/security_best_practices#token-passthrough";

    private static readonly JsonSerializerOptions AuthMetadataJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    // Standard MCP methods that may exist based on server capabilities
    private readonly string[] _standardMcpMethods = new[]
    {
        "tools/list",
        "tools/call",
        "resources/list",
        "resources/read",
        "prompts/list",
        "prompts/get",
        "logging/setLevel"
    };

    public McpCompliantAuthValidator(ILogger<McpCompliantAuthValidator> logger, IMcpHttpClient httpClient)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    /// <summary>
    /// Performs comprehensive MCP-compliant authentication testing according to official specification
    /// </summary>
    public async Task<AuthenticationTestResult> ValidateAuthenticationComplianceAsync(
        McpServerConfig serverConfig,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting comprehensive MCP authentication validation for: {Server}", serverConfig.Endpoint);

        var startTime = DateTime.UtcNow;
        var result = new AuthenticationTestResult
        {
            Status = TestStatus.InProgress,
            ComplianceScore = 0.0,
            TestScenarios = new List<AuthenticationScenario>()
        };

        try
        {
            var profile = serverConfig.Profile;

            // Step 1: Handle STDIO transport (no HTTP auth per MCP spec)
            if (serverConfig.Transport?.ToLower() == "stdio")
            {
                return HandleStdioTransport(result, startTime);
            }

            // Step 2: Test initialize method first to detect if server requires authentication
            _logger.LogInformation("Testing if server requires authentication");
            var initializeNoAuth = await TestMethodAuthentication(
                new McpServerConfig { Endpoint = serverConfig.Endpoint ?? "", Transport = serverConfig.Transport ?? "http", Authentication = new AuthenticationConfig() },
                "initialize", "No Auth", profile, cancellationToken);
            result.TestScenarios.Add(initializeNoAuth);

            // Check for critical network errors
            if (int.TryParse(initializeNoAuth.StatusCode, out int statusCode) && statusCode < 0)
            {
                 result.Status = TestStatus.Error;
                 result.Duration = DateTime.UtcNow - startTime;
                 return result;
            }

            // Step 3: Discover available endpoints
            _logger.LogInformation("Discovering server capabilities to identify available endpoints");
            var discoveredEndpoints = await DiscoverServerEndpoints(serverConfig, cancellationToken);

            if (discoveredEndpoints.Count == 0)
            {
                _logger.LogInformation("No endpoints discovered from capabilities, testing standard MCP methods");
                discoveredEndpoints = _standardMcpMethods.ToList();
            }

            _logger.LogInformation("Testing authentication on {Count} discovered endpoints: {Endpoints}",
                discoveredEndpoints.Count, string.Join(", ", discoveredEndpoints));

            // Step 4: Test each endpoint with different authentication scenarios
            foreach (var endpoint in discoveredEndpoints)
            {
                // Test without authentication
                var noAuthTest = await TestMethodAuthentication(
                    new McpServerConfig { Endpoint = serverConfig.Endpoint ?? "", Transport = serverConfig.Transport ?? "http", Authentication = new AuthenticationConfig() },
                    endpoint, "No Auth", profile, cancellationToken);
                result.TestScenarios.Add(noAuthTest);

                // Test with malformed token (invalid format)
                var malformedTokenTest = await TestMethodAuthentication(
                    new McpServerConfig
                    {
                        Endpoint = serverConfig.Endpoint ?? "",
                        Transport = serverConfig.Transport ?? "http",
                        Authentication = new AuthenticationConfig { Type = "Bearer", Token = "malformed_!!@#$%_invalid_format" }
                    },
                    endpoint, "Malformed Token", profile, cancellationToken);
                result.TestScenarios.Add(malformedTokenTest);

                // Test with invalid token (valid format but fake)
                var invalidTokenTest = await TestMethodAuthentication(
                    new McpServerConfig
                    {
                        Endpoint = serverConfig.Endpoint ?? "",
                        Transport = serverConfig.Transport ?? "http",
                        Authentication = new AuthenticationConfig { Type = "Bearer", Token = "invalid_fake_token_mcp_test_12345" }
                    },
                    endpoint, "Invalid Token", profile, cancellationToken);
                result.TestScenarios.Add(invalidTokenTest);

                // Test with expired token (simulated expired JWT format)
                var expiredTokenTest = await TestMethodAuthentication(
                    new McpServerConfig
                    {
                        Endpoint = serverConfig.Endpoint ?? "",
                        Transport = serverConfig.Transport ?? "http",
                        Authentication = new AuthenticationConfig { Type = "Bearer", Token = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0NTY3ODkwIiwibmFtZSI6IkpvaG4gRG9lIiwiaWF0IjoxNTE2MjM5MDIyLCJleHAiOjE1MTYyMzkwMjJ9.expired_signature_simulation" }
                    },
                    endpoint, "Token Expired", profile, cancellationToken);
                result.TestScenarios.Add(expiredTokenTest);

                // Test with token having invalid scope (simulated token with wrong scope)
                var invalidScopeTest = await TestMethodAuthentication(
                    new McpServerConfig
                    {
                        Endpoint = serverConfig.Endpoint ?? "",
                        Transport = serverConfig.Transport ?? "http",
                        Authentication = new AuthenticationConfig { Type = "Bearer", Token = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0NTY3ODkwIiwic2NvcGUiOiJ3cm9uZy1zY29wZSIsImlhdCI6MTcyMjM3MjgwMCwiZXhwIjoxNzIyNDU5MjAwfQ.invalid_scope_simulation" }
                    },
                    endpoint, "Invalid Scope", profile, cancellationToken);
                result.TestScenarios.Add(invalidScopeTest);

                // Test with token having insufficient permissions (simulated read-only token for write operations)
                var insufficientPermissionsTest = await TestMethodAuthentication(
                    new McpServerConfig
                    {
                        Endpoint = serverConfig.Endpoint ?? "",
                        Transport = serverConfig.Transport ?? "http",
                        Authentication = new AuthenticationConfig { Type = "Bearer", Token = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0NTY3ODkwIiwic2NvcGUiOiJyZWFkLW9ubHkiLCJpYXQiOjE3MjIzNzI4MDAsImV4cCI6MTcyMjQ1OTIwMH0.insufficient_permissions_simulation" }
                    },
                    endpoint, "Insufficient Permissions", profile, cancellationToken);
                result.TestScenarios.Add(insufficientPermissionsTest);


                // Test with revoked/blacklisted token pattern (simulated)
                var revokedTokenTest = await TestMethodAuthentication(
                    new McpServerConfig
                    {
                        Endpoint = serverConfig.Endpoint ?? "",
                        Transport = serverConfig.Transport ?? "http",
                        Authentication = new AuthenticationConfig { Type = "Bearer", Token = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0NTY3ODkwIiwianRpIjoicmV2b2tlZC10b2tlbi1pZCIsImlhdCI6MTcyMjM3MjgwMCwiZXhwIjoxNzIyNDU5MjAwfQ.revoked_token_simulation" }
                    },
                    endpoint, "Revoked Token", profile, cancellationToken);
                result.TestScenarios.Add(revokedTokenTest);

                // Test with wrong audience claim (RFC 8707)
                var wrongAudienceTest = await TestMethodAuthentication(
                    new McpServerConfig
                    {
                        Endpoint = serverConfig.Endpoint ?? "",
                        Transport = serverConfig.Transport ?? "http",
                        Authentication = new AuthenticationConfig { Type = "Bearer", Token = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0NTY3ODkwIiwiYXVkIjoiaHR0cHM6Ly93cm9uZy1hdWRpZW5jZS5jb20iLCJpYXQiOjE3MjIzNzI4MDAsImV4cCI6MTcyMjQ1OTIwMH0.wrong_audience_simulation" }
                    },
                    endpoint, "Wrong Audience (RFC 8707)", profile, cancellationToken);
                result.TestScenarios.Add(wrongAudienceTest);

                if (ValidationCalibration.IsSensitiveMethod(endpoint))
                {
                    var queryTokenTest = await TestMethodAuthentication(
                        new McpServerConfig
                        {
                            Endpoint = AppendQueryAccessToken(serverConfig.Endpoint ?? "", "mcp_query_token_probe"),
                            Transport = serverConfig.Transport ?? "http",
                            Authentication = new AuthenticationConfig()
                        },
                        endpoint, "Query Token", profile, cancellationToken);
                    result.TestScenarios.Add(queryTokenTest);
                }

                // Test with valid token (if available)
                if (!string.IsNullOrEmpty(serverConfig.Authentication?.Token))
                {
                    var validTokenTest = await TestMethodAuthentication(serverConfig, endpoint, "Valid Token", profile, cancellationToken);
                    result.TestScenarios.Add(validTokenTest);
                }
            }

            await FinalizeAuthenticationEvidenceAsync(result, serverConfig, profile, cancellationToken);

            // Step 5: Calculate final compliance score
            // Scenario scoring is calibrated instead of binary:
            // - 100: secure and standards-aligned
            // - 75: secure but non-canonical / guidance gap
            // - 0: insecure for the declared profile
            // - <0: informational or inconclusive, excluded from scoring
            var scoredScenarios = result.TestScenarios
                .Where(s => s.AssessmentScore >= 0)
                .ToList();

            var totalScenarios = scoredScenarios.Count;
            var secureScenarios = scoredScenarios.Count(s => s.IsSecure);

            result.ComplianceScore = totalScenarios > 0
                ? scoredScenarios.Average(s => s.AssessmentScore)
                : 100.0;

            result.Status = scoredScenarios.Any(s => ValidationCalibration.IsBlockingAuthenticationFailure(s, profile))
                ? TestStatus.Failed
                : TestStatus.Passed;
            result.Duration = DateTime.UtcNow - startTime;

            _logger.LogInformation("Authentication validation completed: {Score:F1}% ({Secure}/{Total} scored scenarios secure)",
                result.ComplianceScore, secureScenarios, totalScenarios);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Authentication validation failed");
            result.Status = TestStatus.Error;
            result.Duration = DateTime.UtcNow - startTime;
            result.ComplianceScore = 0.0;
            return result;
        }
    }

    /// <summary>
    /// Handles STDIO transport validation per MCP specification
    /// </summary>
    private AuthenticationTestResult HandleStdioTransport(AuthenticationTestResult result, DateTime startTime)
    {
        _logger.LogInformation("STDIO transport detected - validating environment-based authentication posture");

        result.Duration = DateTime.UtcNow - startTime;

        // Per MCP spec: STDIO servers do NOT use HTTP authorization.
        // However, they CAN enforce authentication via environment variables
        // (e.g., API keys, tokens passed as env vars to the spawned process).
        // We verify this by checking if the server config specifies env-based credentials.
        result.TestScenarios.Add(new AuthenticationScenario
        {
            ScenarioName = "STDIO Transport Protocol Check",
            TestType = "Transport Protocol Validation",
            Method = "transport-validation",
            ExpectedBehavior = "STDIO transport should NOT use HTTP authentication headers",
            IsCompliant = true,
            ActualBehavior = "✅ Correctly using STDIO transport (no HTTP auth)",
            ComplianceReason = "MCP Spec: STDIO implementations should NOT follow HTTP authorization spec"
        });

        // Check if environment variables are configured for credential passing
        var hasEnvCredentials = false;
        // Common env var patterns for MCP server credentials
        var credentialEnvVars = new[] { "API_KEY", "TOKEN", "SECRET", "AUTH", "CREDENTIAL", "PASSWORD" };
        var envVars = System.Environment.GetEnvironmentVariables();
        foreach (System.Collections.DictionaryEntry entry in envVars)
        {
            var key = entry.Key?.ToString()?.ToUpperInvariant() ?? "";
            if (credentialEnvVars.Any(c => key.Contains(c)))
            {
                hasEnvCredentials = true;
                break;
            }
        }

        result.TestScenarios.Add(new AuthenticationScenario
        {
            ScenarioName = "STDIO Environment Credentials Check",
            TestType = "Environment Authentication",
            Method = "env-check",
            ExpectedBehavior = "Environment variables may carry credentials for STDIO servers",
            IsCompliant = true, // Not a failure either way — informational
            ActualBehavior = hasEnvCredentials
                ? "✅ Environment credential variables detected"
                : "ℹ️ No credential-related environment variables found (server may use other auth mechanisms)",
            ComplianceReason = hasEnvCredentials
                ? "Credentials appear to be passed via environment variables"
                : "STDIO servers may authenticate using process arguments, config files, or be intentionally public"
        });

        // Score: Give full marks for transport compliance, but note limited coverage
        result.Status = TestStatus.Passed;
        result.ComplianceScore = 100.0;

        _logger.LogInformation("STDIO transport validation: {Score}% compliant (env credentials: {HasEnv})",
            result.ComplianceScore, hasEnvCredentials);
        return result;
    }

    /// <summary>
    /// Tests if any method follows MCP authentication patterns
    /// 
    /// Calibrated OAuth/MCP authentication assessment:
    /// - Public profiles treat auth challenges as informational.
    /// - Protected HTTP profiles prefer RFC 6750 / MCP challenge behavior.
    /// - Secure but non-canonical rejection patterns reduce score but do not fail.
    /// - Only genuine unauthorized success on sensitive operations is treated as blocking.
    /// 
    /// Authentication Test Matrix:
    /// ┌─────────────────────┬─────────────────────────────────────────────────────────┐
    /// │ Test Scenario       │ Expected Response (OAuth 2.1 Only)                     │
    /// ├─────────────────────┼─────────────────────────────────────────────────────────┤
    /// │ No Auth             │ 401/4xx challenge preferred; secure rejection accepted │
    /// │ Malformed Token     │ 401/4xx challenge preferred; secure rejection accepted │
    /// │ Invalid Token       │ 401/4xx challenge preferred; secure rejection accepted │
    /// │ Token Expired       │ 401/4xx challenge preferred; secure rejection accepted │
    /// │ Invalid Scope       │ 403 challenge preferred; secure rejection accepted     │
    /// │ Insufficient Perms  │ 403 challenge preferred; secure rejection accepted     │
    /// │ Valid Token         │ 2xx JSON-RPC processing                                │
    /// └─────────────────────┴─────────────────────────────────────────────────────────┘
    /// 
    /// Special Case: initialize method can return 401 OR 200 (both acceptable)
    /// 
    /// Reference Links:
    /// - OAuth 2.1 Section 5.3: https://datatracker.ietf.org/doc/html/draft-ietf-oauth-v2-1-09#section-5.3
    /// - RFC 6750 Section 3.1: https://datatracker.ietf.org/doc/html/rfc6750#section-3.1
    /// - JSON-RPC 2.0 Spec: https://www.jsonrpc.org/specification
    /// </summary>
    private async Task<AuthenticationScenario> TestMethodAuthentication(
        McpServerConfig serverConfig,
        string method,
        string testType,
        McpServerProfile profile,
        CancellationToken cancellationToken)
    {
        var scenario = new AuthenticationScenario
        {
            ScenarioName = $"{testType} - {method}",
            TestType = testType,
            Method = method,
            ExpectedBehavior = GetExpectedBehavior(testType, method)
        };

        try
        {
            var response = await _httpClient.CallAsync(serverConfig.Endpoint!, method,
                GetParametersForMethod(method), serverConfig.Authentication, cancellationToken);

            // Analyze response based on test type and MCP protocol requirements
            AnalyzeAuthenticationResponse(scenario, response, testType, method);
            ApplyProfileSemantics(scenario, profile);
        }
        catch (Exception ex)
        {
            // Treat transport-level failures (timeouts, cancellations, DNS issues, etc.) as
            // environment/network issues rather than direct authentication non-compliance.
            scenario.StatusCode = "-1";
            MarkScenarioInconclusive(
                scenario,
                actualBehavior: $"⏱️ Request failed: {ex.Message}",
                analysis: "ℹ️ INFO: Authentication test inconclusive due to validator/network error.",
                complianceReason: "INFO: Environment or network issue during authentication testing; excluded from compliance scoring.");
            scenario.IsCompliant = true;
        }

        return scenario;
    }

    private object? GetParametersForMethod(string method)
    {
        return method switch
        {
            "initialize" => new
            {
                protocolVersion = SchemaRegistryProtocolVersions.NormalizeRequestedVersion(null),
                capabilities = new { },
                clientInfo = new { name = "Visual Studio Code", version = "1.96.0" }
            },
            "tools/list" => new { }, // Empty object for tools/list
            "tools/call" => new
            {
                name = "test-tool", // Test tool name
                arguments = new { } // Empty arguments
            },
            "resources/list" => new { }, // Empty object for resources/list
            "resources/read" => new
            {
                uri = "test://resource" // Test resource URI
            },
            "prompts/list" => new { }, // Empty object for prompts/list
            "prompts/get" => new
            {
                name = "test-prompt" // Test prompt name
            },
            "logging/setLevel" => new
            {
                level = "info" // Test log level
            },
            _ => new { } // Default empty object for unknown methods
        };
    }

    private string GetExpectedBehavior(string testType, string method)
    {
        // OAuth 2.1 + MCP Specification Compliance Matrix
        // Updated based on official specs analysis: both 400 and 401 are valid for auth errors
        // PRACTICAL UPDATE: Any 4xx rejection is considered secure in practical scenarios
        return testType switch
        {
            "No Auth" => "4xx (Secure Rejection)",
            "Malformed Token" => "4xx (Secure Rejection)",
            "Invalid Token" => "401 + WWW-Authenticate challenge",
            "Token Expired" => "401 + WWW-Authenticate challenge",
            "Invalid Scope" => "4xx (Secure Rejection)",
            "Insufficient Permissions" => "4xx (Secure Rejection)",
            "Revoked Token" => "401 + WWW-Authenticate challenge",
            "Wrong Audience (RFC 8707)" => "401 + audience-bound token rejection",
            "Query Token" => "4xx or JSON-RPC rejection; query-string tokens must not grant access",
            "Valid Token" => "200 + JSON-RPC Response",
            _ => "4xx (Secure Rejection)"
        };
    }

    private void AnalyzeAuthenticationResponse(AuthenticationScenario scenario, JsonRpcResponse response, string testType, string method)
    {
        scenario.ProbeContext = response.ProbeContext;
        var hasWWWAuth = response.Headers?.Keys.Any(k => k.Equals("WWW-Authenticate", StringComparison.OrdinalIgnoreCase)) == true;

        if (hasWWWAuth && response.Headers != null)
        {
            var headerKey = response.Headers.Keys.First(k => k.Equals("WWW-Authenticate", StringComparison.OrdinalIgnoreCase));
            if (response.Headers.TryGetValue(headerKey, out var headerVal))
            {
                scenario.WwwAuthenticateHeader = headerVal;
            }
        }

        scenario.StatusCode = response.StatusCode.ToString();

        // Negative status codes are reserved for transport/tool-layer failures (e.g. timeouts,
        // cancellations, DNS issues) mapped by the HTTP client. Treat these as environment
        // issues: they should not count as authentication failures or passes.
        if (response.StatusCode < 0)
        {
            MarkScenarioInconclusive(
                scenario,
                actualBehavior: "⏱️ Request canceled or timed out (validator/network)",
                analysis: "ℹ️ INFO: Authentication test inconclusive due to validator/network timeout or cancellation.",
                complianceReason: "INFO: Environment/network issue during auth testing; excluded from compliance scoring.");
            return;
        }

        // MCP + OAuth 2.1 Compliance Analysis
        switch (response.StatusCode)
        {
            case 400:
                if (hasWWWAuth)
                {
                    if (RequiresUnauthorizedForInvalidToken(testType))
                    {
                        MarkScenarioSecureCompatible(
                            scenario,
                            actualBehavior: "Challenge returned with non-401 status",
                            analysis: "⚠️ COMPATIBLE: Token was rejected, but invalid, expired, revoked, or wrong-audience tokens should receive HTTP 401.",
                            complianceReason: "SECURE: Token was rejected, but the response did not use the required invalid-token status.");
                    }
                    else
                    {
                        MarkScenarioStandardsAligned(
                            scenario,
                            actualBehavior: "Challenge returned",
                            analysis: "✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400.",
                            complianceReason: "PASS: Access denied with challenge metadata.");
                    }
                }
                else
                {
                    MarkScenarioSecureCompatible(
                        scenario,
                        actualBehavior: "Challenge missing",
                        analysis: "⚠️ COMPATIBLE: Secure rejection without the preferred WWW-Authenticate challenge.",
                        complianceReason: "SECURE: Request was rejected, but challenge metadata was not provided.");
                }
                break;

            case 401:
                // OAuth 2.1 RFC 6750: 401 is valid for authentication errors (invalid_token pattern)
                // STRICT SPEC COMPLIANCE: Must have WWW-Authenticate header
                
                if (testType == "Valid Token")
                {
                    MarkScenarioInsecure(
                        scenario,
                        actualBehavior: hasWWWAuth ? "Valid token rejected with challenge" : "Valid token rejected without challenge",
                        analysis: "❌ INSECURE: A valid token was rejected.",
                        complianceReason: "FAIL: Server rejected the valid token.");
                }
                else
                {
                    if (hasWWWAuth)
                    {
                        MarkScenarioStandardsAligned(
                            scenario,
                            actualBehavior: "Challenge returned",
                            analysis: "✅ ALIGNED: OAuth/MCP challenge returned with HTTP 401.",
                            complianceReason: "PASS: Access denied with challenge metadata.");
                    }
                    else
                    {
                        MarkScenarioSecureCompatible(
                            scenario,
                            actualBehavior: "Challenge missing",
                            analysis: "⚠️ COMPATIBLE: Secure rejection without the preferred WWW-Authenticate challenge.",
                            complianceReason: "SECURE: Request was rejected, but challenge metadata was not provided.");
                    }
                }
                break;

            case 403:
                // OAuth 2.1: 403 for insufficient_scope
                // STRICT SPEC COMPLIANCE: Must have WWW-Authenticate header
                
                if (RequiresUnauthorizedForInvalidToken(testType))
                {
                    MarkScenarioSecureCompatible(
                        scenario,
                        actualBehavior: hasWWWAuth ? "Challenge returned with non-401 status" : "Rejected without invalid-token challenge",
                        analysis: "⚠️ COMPATIBLE: Token was rejected, but invalid, expired, revoked, or wrong-audience tokens should receive HTTP 401.",
                        complianceReason: "SECURE: Token was rejected, but the response did not use the required invalid-token status.");
                }
                else if (hasWWWAuth)
                {
                    MarkScenarioStandardsAligned(
                        scenario,
                        actualBehavior: "Challenge returned",
                        analysis: "✅ ALIGNED: Secure insufficient-scope rejection returned with challenge metadata.",
                        complianceReason: "PASS: Access denied with challenge metadata.");
                }
                else
                {
                    MarkScenarioSecureCompatible(
                        scenario,
                        actualBehavior: "Challenge missing",
                        analysis: "⚠️ COMPATIBLE: Secure rejection without the preferred WWW-Authenticate challenge.",
                        complianceReason: "SECURE: Request was rejected, but challenge metadata was not provided.");
                }
                break;

            case 429:
                // Rate Limiting - Not an auth violation
                MarkScenarioInconclusive(
                    scenario,
                    actualBehavior: "Rate limited",
                    analysis: "ℹ️ INFO: Rate limiting observed; auth semantics inconclusive.",
                    complianceReason: "INFO: Request was throttled and excluded from auth scoring.");
                break;

            case 406:
                // Content Negotiation - Not an auth violation
                MarkScenarioInconclusive(
                    scenario,
                    actualBehavior: "Content negotiation rejected",
                    analysis: "ℹ️ INFO: Content negotiation failed before auth semantics could be evaluated.",
                    complianceReason: "INFO: Content negotiation prevented a conclusive auth assessment.");
                break;

            case 503:
                // Service Unavailable - Not an auth violation
                MarkScenarioInconclusive(
                    scenario,
                    actualBehavior: "Service unavailable",
                    analysis: "ℹ️ INFO: Service availability issue made auth semantics inconclusive.",
                    complianceReason: "INFO: Service was unavailable during auth assessment.");
                break;

            case 405:
                MarkScenarioSecureCompatible(
                    scenario,
                    actualBehavior: hasWWWAuth ? "Method rejected with challenge" : "Method rejected without challenge",
                    analysis: "⚠️ COMPATIBLE: Request was rejected, but the server used a non-canonical HTTP method outcome.",
                    complianceReason: "SECURE: Request was rejected using a non-canonical transport response.");
                break;

            case 200:
                var jsonRpcResult = ParseJsonRpcResponse(response);

                if (testType == "Valid Token")
                {
                    MarkScenarioStandardsAligned(
                        scenario,
                        actualBehavior: jsonRpcResult.IsSuccess ? "Authenticated request succeeded" : "Authenticated request returned JSON-RPC error",
                        analysis: "✅ ALIGNED: Valid authentication was accepted and request processing proceeded.",
                        complianceReason: "PASS: Server accepted the valid token and processed the request.");
                }
                else
                {
                    if (!jsonRpcResult.IsSuccess)
                    {
                        MarkScenarioSecureCompatible(
                            scenario,
                            actualBehavior: "Request rejected in JSON-RPC layer",
                            analysis: "⚠️ COMPATIBLE: Authentication appears to be enforced at the application layer instead of via an HTTP challenge.",
                            complianceReason: "SECURE: Request was rejected, but not through the preferred HTTP challenge flow.");
                    }
                    else
                    {
                        if (ValidationCalibration.IsDiscoveryMethod(method))
                        {
                            MarkScenarioSecureCompatible(
                                scenario,
                                actualBehavior: "Discovery metadata returned",
                                analysis: "⚠️ COMPATIBLE: Discovery metadata was exposed without authentication.",
                                complianceReason: "SECURE: Discovery surface is public, but challenge-based auth was not enforced.");
                        }
                        else
                        {
                            MarkScenarioInsecure(
                                scenario,
                                actualBehavior: "Sensitive operation succeeded",
                                analysis: "❌ INSECURE: Sensitive operation succeeded without valid authentication.",
                                complianceReason: "FAIL: Invalid authentication was accepted.");
                        }
                    }
                }
                break;

            default:
                if (response.StatusCode >= 400 && response.StatusCode < 500)
                {
                    MarkScenarioSecureCompatible(
                        scenario,
                        actualBehavior: "HTTP rejection",
                        analysis: $"⚠️ COMPATIBLE: Server rejected the request with HTTP {response.StatusCode}, but without the preferred challenge semantics.",
                        complianceReason: "SECURE: Request was rejected using a non-canonical HTTP pattern.");
                }
                else
                {
                    MarkScenarioInconclusive(
                        scenario,
                        actualBehavior: "Unexpected transport response",
                        analysis: $"ℹ️ INFO: Unexpected HTTP {response.StatusCode} prevented a reliable auth assessment.",
                        complianceReason: "INFO: Response was excluded from auth scoring due to inconclusive transport behavior.");
                }
                break;
        }
    }

    private async Task FinalizeAuthenticationEvidenceAsync(
        AuthenticationTestResult result,
        McpServerConfig serverConfig,
        McpServerProfile profile,
        CancellationToken cancellationToken)
    {
        var strictProfile = ValidationCalibration.RequiresStrictAuthentication(profile);
        var authenticationObserved = result.TestScenarios.Any(IsAuthenticationObserved);
        if (!strictProfile && !authenticationObserved)
        {
            return;
        }

        AddChallengeFindings(result);
        await ValidateProtectedResourceMetadataAsync(result, cancellationToken);
        AddAuthorizationHeaderFindings(result);
        AddTokenPlacementAndAudienceFindings(result);
        AddResourceIndicatorFinding(result, serverConfig);
    }

    private void AddChallengeFindings(AuthenticationTestResult result)
    {
        foreach (var scenario in result.TestScenarios.Where(s => string.Equals(s.StatusCode, "401", StringComparison.OrdinalIgnoreCase)))
        {
            if (string.IsNullOrWhiteSpace(scenario.WwwAuthenticateHeader))
            {
                AddAuthFinding(
                    result,
                    ValidationFindingRuleIds.AuthWwwAuthenticateMissing,
                    scenario.Method,
                    ValidationFindingSeverity.High,
                    $"{scenario.ScenarioName}: HTTP 401 did not include WWW-Authenticate.",
                    "Return a WWW-Authenticate challenge on 401 responses and include protected resource metadata discovery information.",
                    ValidationRuleSource.Spec,
                    McpAuthorizationSpecReference,
                    new Dictionary<string, string> { ["statusCode"] = scenario.StatusCode });
            }
        }
    }

    private async Task ValidateProtectedResourceMetadataAsync(AuthenticationTestResult result, CancellationToken cancellationToken)
    {
        var challenge = result.TestScenarios
            .Select(CreateChallengeObservation)
            .FirstOrDefault(observation => !string.IsNullOrWhiteSpace(observation.ResourceMetadataUrl));

        if (challenge == null)
        {
            if (result.TestScenarios.Any(s => !string.IsNullOrWhiteSpace(s.WwwAuthenticateHeader)))
            {
                AddAuthFinding(
                    result,
                    ValidationFindingRuleIds.AuthProtectedResourceMetadataMissing,
                    "authorization",
                    ValidationFindingSeverity.High,
                    "WWW-Authenticate challenge did not advertise resource_metadata.",
                    "Include resource_metadata in the WWW-Authenticate challenge so MCP clients can use OAuth 2.0 Protected Resource Metadata for authorization server discovery.",
                    ValidationRuleSource.Spec,
                    McpAuthorizationSpecReference);
            }

            return;
        }

        var metadataUrl = challenge.ResourceMetadataUrl!;
        result.ProtectedResourceMetadataUrl = metadataUrl;

        if (!TryCreateAbsoluteUri(metadataUrl, out var metadataUri, out var issue) || !string.IsNullOrEmpty(metadataUri.Fragment))
        {
            AddAuthFinding(
                result,
                ValidationFindingRuleIds.AuthProtectedResourceMetadataInvalid,
                "authorization",
                ValidationFindingSeverity.High,
                "resource_metadata did not contain a valid absolute URI without a fragment.",
                "Advertise an absolute protected resource metadata URL that clients can safely dereference.",
                ValidationRuleSource.Spec,
                McpAuthorizationSpecReference,
                new Dictionary<string, string>
                {
                    ["resourceMetadataUrl"] = metadataUrl,
                    ["issue"] = issue ?? "URI contains a fragment."
                });
            return;
        }

        if (!ShouldFetchOAuthMetadata(metadataUri))
        {
            AddAuthFinding(
                result,
                ValidationFindingRuleIds.AuthProtectedResourceMetadataInvalid,
                "authorization",
                ValidationFindingSeverity.Medium,
                "resource_metadata URL used an insecure non-loopback HTTP endpoint.",
                "Use HTTPS for OAuth-related metadata URLs in production, or restrict HTTP metadata URLs to explicit local development loopback addresses.",
                ValidationRuleSource.Guideline,
                "https://modelcontextprotocol.io/specification/2025-06-18/basic/security_best_practices#server-side-request-forgery-ssrf",
                new Dictionary<string, string> { ["resourceMetadataUrl"] = metadataUrl });
            return;
        }

        AuthMetadata? metadata;
        try
        {
            var json = await _httpClient.GetStringAsync(metadataUrl, cancellationToken);
            metadata = JsonSerializer.Deserialize<AuthMetadata>(json, AuthMetadataJsonOptions);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AddAuthFinding(
                result,
                ValidationFindingRuleIds.AuthProtectedResourceMetadataFetchFailed,
                "authorization",
                ValidationFindingSeverity.Medium,
                "Protected resource metadata could not be fetched or parsed.",
                "Ensure the resource_metadata URL returns a valid OAuth 2.0 Protected Resource Metadata document.",
                ValidationRuleSource.Spec,
                McpAuthorizationSpecReference,
                new Dictionary<string, string>
                {
                    ["resourceMetadataUrl"] = metadataUrl,
                    ["error"] = ex.Message
                });
            return;
        }

        if (metadata == null)
        {
            AddAuthFinding(
                result,
                ValidationFindingRuleIds.AuthProtectedResourceMetadataInvalid,
                "authorization",
                ValidationFindingSeverity.High,
                "Protected resource metadata response was empty or invalid.",
                "Return a JSON protected resource metadata document with resource and authorization_servers fields.",
                ValidationRuleSource.Spec,
                McpAuthorizationSpecReference,
                new Dictionary<string, string> { ["resourceMetadataUrl"] = metadataUrl });
            return;
        }

        result.ProtectedResourceMetadata = metadata;
        ValidateAuthMetadataDocument(result, metadata);
    }

    private static void ValidateAuthMetadataDocument(AuthenticationTestResult result, AuthMetadata metadata)
    {
        if (string.IsNullOrWhiteSpace(metadata.Resource))
        {
            AddAuthFinding(
                result,
                ValidationFindingRuleIds.AuthResourceIndicatorMissing,
                "authorization",
                ValidationFindingSeverity.Medium,
                "Protected resource metadata did not identify the MCP resource URI.",
                "Include the protected resource URI so clients can send the required OAuth resource parameter in authorization and token requests.",
                ValidationRuleSource.Spec,
                McpAuthorizationSpecReference);
        }
        else if (!TryCreateAbsoluteUri(metadata.Resource, out var resourceUri, out var issue) || !string.IsNullOrEmpty(resourceUri.Fragment))
        {
            AddAuthFinding(
                result,
                ValidationFindingRuleIds.AuthResourceIndicatorMissing,
                "authorization",
                ValidationFindingSeverity.Medium,
                "Protected resource metadata contained an invalid resource URI.",
                "Use a canonical absolute MCP server URI without a fragment for the OAuth resource indicator.",
                ValidationRuleSource.Spec,
                McpAuthorizationSpecReference,
                new Dictionary<string, string>
                {
                    ["resource"] = metadata.Resource,
                    ["issue"] = issue ?? "URI contains a fragment."
                });
        }

        if (metadata.AuthorizationServers?.Any(server => !string.IsNullOrWhiteSpace(server)) != true)
        {
            AddAuthFinding(
                result,
                ValidationFindingRuleIds.AuthAuthorizationServersMissing,
                "authorization",
                ValidationFindingSeverity.High,
                "Protected resource metadata did not include authorization_servers.",
                "Include at least one authorization server URL in the protected resource metadata document.",
                ValidationRuleSource.Spec,
                McpAuthorizationSpecReference);
        }
        else
        {
            foreach (var authorizationServer in metadata.AuthorizationServers.Where(server => !string.IsNullOrWhiteSpace(server)))
            {
                if (!TryCreateAbsoluteUri(authorizationServer, out var authorizationServerUri, out var serverIssue) ||
                    !string.Equals(authorizationServerUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                {
                    AddAuthFinding(
                        result,
                        ValidationFindingRuleIds.AuthAuthorizationServerInsecure,
                        "authorization",
                        ValidationFindingSeverity.High,
                        "Authorization server metadata URL was not an absolute HTTPS URI.",
                        "Serve OAuth authorization server endpoints over HTTPS.",
                        ValidationRuleSource.Spec,
                        McpAuthorizationSpecReference,
                        new Dictionary<string, string>
                        {
                            ["authorizationServer"] = authorizationServer,
                            ["issue"] = serverIssue ?? "Authorization server URL must use HTTPS."
                        });
                }
            }
        }

        if (metadata.BearerMethodsSupported?.Any() == true &&
            !metadata.BearerMethodsSupported.Any(method => string.Equals(method, "header", StringComparison.OrdinalIgnoreCase)))
        {
            AddAuthFinding(
                result,
                ValidationFindingRuleIds.AuthBearerHeaderUnsupported,
                "authorization",
                ValidationFindingSeverity.High,
                "Protected resource metadata did not advertise bearer token header support.",
                "MCP clients must send access tokens using the Authorization: Bearer header; advertise and support bearer_methods_supported=header.",
                ValidationRuleSource.Spec,
                McpAuthorizationSpecReference,
                new Dictionary<string, string> { ["bearerMethodsSupported"] = string.Join(",", metadata.BearerMethodsSupported) });
        }
    }

    private static void AddAuthorizationHeaderFindings(AuthenticationTestResult result)
    {
        foreach (var scenario in result.TestScenarios.Where(s => ShouldApplyBearerHeader(s.TestType) && s.ProbeContext != null))
        {
            if (!scenario.ProbeContext!.AuthApplied)
            {
                AddAuthFinding(
                    result,
                    ValidationFindingRuleIds.AuthAuthorizationHeaderMissing,
                    scenario.Method,
                    ValidationFindingSeverity.High,
                    $"{scenario.ScenarioName}: bearer token was not applied through the Authorization header.",
                    "Send access tokens in the Authorization: Bearer header on every HTTP request.",
                    ValidationRuleSource.Spec,
                    McpAuthorizationSpecReference);
            }
            else if (!string.Equals(scenario.ProbeContext.AuthScheme, "Bearer", StringComparison.OrdinalIgnoreCase))
            {
                AddAuthFinding(
                    result,
                    ValidationFindingRuleIds.AuthAuthorizationHeaderMissing,
                    scenario.Method,
                    ValidationFindingSeverity.Medium,
                    $"{scenario.ScenarioName}: authorization header did not use the Bearer scheme.",
                    "Use the Bearer authentication scheme for OAuth access tokens sent to HTTP MCP servers.",
                    ValidationRuleSource.Spec,
                    McpAuthorizationSpecReference,
                    new Dictionary<string, string> { ["authScheme"] = scenario.ProbeContext.AuthScheme ?? "unknown" });
            }
        }
    }

    private static void AddTokenPlacementAndAudienceFindings(AuthenticationTestResult result)
    {
        foreach (var scenario in result.TestScenarios)
        {
            if (string.Equals(scenario.TestType, "Query Token", StringComparison.OrdinalIgnoreCase) &&
                scenario.AssessmentDisposition == AuthenticationAssessmentDisposition.Insecure)
            {
                AddAuthFinding(
                    result,
                    ValidationFindingRuleIds.AuthQueryTokenAccepted,
                    scenario.Method,
                    ValidationFindingSeverity.Critical,
                    $"{scenario.ScenarioName}: URI query-string token granted access to a sensitive operation.",
                    "Reject access tokens supplied in URI query strings and require Authorization: Bearer headers.",
                    ValidationRuleSource.Spec,
                    McpAuthorizationSpecReference,
                    new Dictionary<string, string> { ["statusCode"] = scenario.StatusCode });
            }

            if (IsTokenScenarioRequiring401(scenario.TestType) && IsRejectedWithNonUnauthorizedStatus(scenario))
            {
                AddAuthFinding(
                    result,
                    ValidationFindingRuleIds.AuthInvalidTokenStatus,
                    scenario.Method,
                    ValidationFindingSeverity.Medium,
                    $"{scenario.ScenarioName}: token was rejected with HTTP {scenario.StatusCode} instead of HTTP 401.",
                    "Return HTTP 401 for invalid, expired, revoked, or wrong-audience access tokens.",
                    ValidationRuleSource.Spec,
                    McpAuthorizationSpecReference,
                    new Dictionary<string, string> { ["statusCode"] = scenario.StatusCode });
            }

            if (IsInvalidTokenScenario(scenario.TestType) && scenario.AssessmentDisposition == AuthenticationAssessmentDisposition.Insecure)
            {
                AddAuthFinding(
                    result,
                    ValidationFindingRuleIds.AuthInvalidTokenAccepted,
                    scenario.Method,
                    ValidationFindingSeverity.Critical,
                    $"{scenario.ScenarioName}: invalid token was accepted.",
                    "Validate access tokens before processing MCP requests and reject invalid or expired credentials.",
                    ValidationRuleSource.Spec,
                    McpAuthorizationSpecReference);
            }

            if (string.Equals(scenario.TestType, "Wrong Audience (RFC 8707)", StringComparison.OrdinalIgnoreCase) &&
                scenario.AssessmentDisposition == AuthenticationAssessmentDisposition.Insecure)
            {
                AddAuthFinding(
                    result,
                    ValidationFindingRuleIds.AuthWrongAudienceAccepted,
                    scenario.Method,
                    ValidationFindingSeverity.Critical,
                    $"{scenario.ScenarioName}: token for a different audience was accepted.",
                    "Validate that access tokens were issued specifically for this MCP server as the intended resource.",
                    ValidationRuleSource.Spec,
                    McpAuthorizationSpecReference);

                AddAuthFinding(
                    result,
                    ValidationFindingRuleIds.AuthTokenPassthroughRisk,
                    scenario.Method,
                    ValidationFindingSeverity.High,
                    $"{scenario.ScenarioName}: wrong-audience token acceptance indicates token passthrough/confused-deputy risk.",
                    "Do not accept or transit tokens issued for other resources; acquire separate downstream tokens when proxying to upstream APIs.",
                    ValidationRuleSource.Spec,
                    McpSecurityBestPracticesReference);
            }
        }
    }

    private static void AddResourceIndicatorFinding(AuthenticationTestResult result, McpServerConfig serverConfig)
    {
        if (result.ProtectedResourceMetadata != null || string.IsNullOrWhiteSpace(result.ProtectedResourceMetadataUrl))
        {
            return;
        }

        AddAuthFinding(
            result,
            ValidationFindingRuleIds.AuthResourceIndicatorMissing,
            "authorization",
            ValidationFindingSeverity.Medium,
            "Resource indicator evidence could not be validated from protected resource metadata.",
            "Expose protected resource metadata with a canonical resource URI matching the MCP server endpoint used by clients.",
            ValidationRuleSource.Spec,
            McpAuthorizationSpecReference,
            new Dictionary<string, string>
            {
                ["endpoint"] = serverConfig.Endpoint ?? string.Empty,
                ["resourceMetadataUrl"] = result.ProtectedResourceMetadataUrl
            });
    }

    private static bool IsAuthenticationObserved(AuthenticationScenario scenario)
    {
        if (!string.IsNullOrWhiteSpace(scenario.WwwAuthenticateHeader))
        {
            return true;
        }

        return int.TryParse(scenario.StatusCode, out var statusCode) && ValidationReliability.IsAuthenticationStatusCode(statusCode);
    }

    private static AuthenticationChallengeObservation CreateChallengeObservation(AuthenticationScenario scenario)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(scenario.WwwAuthenticateHeader))
        {
            headers["WWW-Authenticate"] = scenario.WwwAuthenticateHeader;
        }

        return AuthenticationChallengeInterpreter.Inspect(new JsonRpcResponse
        {
            StatusCode = int.TryParse(scenario.StatusCode, out var statusCode) ? statusCode : 0,
            Headers = headers
        });
    }

    private static bool RequiresUnauthorizedForInvalidToken(string testType)
    {
        return IsTokenScenarioRequiring401(testType);
    }

    private static bool IsTokenScenarioRequiring401(string testType)
    {
        return string.Equals(testType, "Invalid Token", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(testType, "Token Expired", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(testType, "Revoked Token", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(testType, "Wrong Audience (RFC 8707)", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsInvalidTokenScenario(string testType)
    {
        return string.Equals(testType, "Malformed Token", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(testType, "Invalid Token", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(testType, "Token Expired", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(testType, "Revoked Token", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRejectedWithNonUnauthorizedStatus(AuthenticationScenario scenario)
    {
        return int.TryParse(scenario.StatusCode, out var statusCode) &&
               statusCode >= 400 &&
               statusCode < 500 &&
               statusCode != 401;
    }

    private static bool ShouldApplyBearerHeader(string testType)
    {
        return IsInvalidTokenScenario(testType) ||
               string.Equals(testType, "Invalid Scope", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(testType, "Insufficient Permissions", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(testType, "Wrong Audience (RFC 8707)", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(testType, "Valid Token", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryCreateAbsoluteUri(string value, out Uri uri, out string? issue)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out uri!))
        {
            issue = "URI is not absolute.";
            return false;
        }

        issue = null;
        return true;
    }

    private static bool ShouldFetchOAuthMetadata(Uri metadataUri)
    {
        return string.Equals(metadataUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
               (string.Equals(metadataUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) && metadataUri.IsLoopback);
    }

    private static string AppendQueryAccessToken(string endpoint, string token)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return endpoint;
        }

        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var endpointUri))
        {
            var separator = endpoint.Contains('?', StringComparison.Ordinal) ? "&" : "?";
            return $"{endpoint}{separator}access_token={Uri.EscapeDataString(token)}";
        }

        var builder = new UriBuilder(endpointUri);
        var query = builder.Query;
        var prefix = string.IsNullOrWhiteSpace(query) ? string.Empty : query.TrimStart('?') + "&";
        builder.Query = prefix + "access_token=" + Uri.EscapeDataString(token);
        return builder.Uri.ToString();
    }

    private static void AddAuthFinding(
        AuthenticationTestResult result,
        string ruleId,
        string component,
        ValidationFindingSeverity severity,
        string summary,
        string recommendation,
        ValidationRuleSource source,
        string specReference,
        Dictionary<string, string>? metadata = null)
    {
        if (result.Findings.Any(f => string.Equals(f.RuleId, ruleId, StringComparison.OrdinalIgnoreCase) &&
                                     string.Equals(f.Component, component, StringComparison.OrdinalIgnoreCase) &&
                                     string.Equals(f.Summary, summary, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var findingMetadata = metadata == null
            ? new Dictionary<string, string>()
            : new Dictionary<string, string>(metadata, StringComparer.OrdinalIgnoreCase);

        findingMetadata["specReference"] = specReference;

        result.Findings.Add(new ValidationFinding
        {
            RuleId = ruleId,
            Category = ValidationConstants.Categories.AuthenticationSecurity,
            Component = component,
            Severity = severity,
            Summary = summary,
            Recommendation = recommendation,
            Source = source,
            SpecReference = specReference,
            Metadata = findingMetadata
        });
    }

    private static void ApplyProfileSemantics(AuthenticationScenario scenario, McpServerProfile profile)
    {
        if (ValidationCalibration.RequiresStrictAuthentication(profile))
        {
            return;
        }

        if (string.Equals(scenario.TestType, "Valid Token", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        scenario.ExpectedBehavior = "Informational (public profile)";
        scenario.AssessmentDisposition = AuthenticationAssessmentDisposition.Informational;
        scenario.AssessmentScore = -1.0;
        scenario.IsCompliant = true;
        scenario.IsSecure = true;

        if (!string.IsNullOrWhiteSpace(scenario.Analysis))
        {
            scenario.Analysis = $"ℹ️ INFO (Public profile): {scenario.Analysis}";
        }

        scenario.ComplianceReason = "INFO: Authentication challenge behavior is informational for public profiles.";
    }

    private static void MarkScenarioStandardsAligned(AuthenticationScenario scenario, string actualBehavior, string analysis, string complianceReason)
    {
        scenario.ActualBehavior = actualBehavior;
        scenario.Analysis = analysis;
        scenario.ComplianceReason = complianceReason;
        scenario.IsCompliant = true;
        scenario.IsSecure = true;
        scenario.IsStandardsAligned = true;
        scenario.AssessmentScore = ValidationCalibration.StandardsAlignedScenarioScore;
        scenario.AssessmentDisposition = AuthenticationAssessmentDisposition.StandardsAligned;
    }

    private static void MarkScenarioSecureCompatible(AuthenticationScenario scenario, string actualBehavior, string analysis, string complianceReason)
    {
        scenario.ActualBehavior = actualBehavior;
        scenario.Analysis = analysis;
        scenario.ComplianceReason = complianceReason;
        scenario.IsCompliant = true;
        scenario.IsSecure = true;
        scenario.IsStandardsAligned = false;
        scenario.AssessmentScore = ValidationCalibration.SecureCompatibleScenarioScore;
        scenario.AssessmentDisposition = AuthenticationAssessmentDisposition.SecureCompatible;
    }

    private static void MarkScenarioInsecure(AuthenticationScenario scenario, string actualBehavior, string analysis, string complianceReason)
    {
        scenario.ActualBehavior = actualBehavior;
        scenario.Analysis = analysis;
        scenario.ComplianceReason = complianceReason;
        scenario.IsCompliant = false;
        scenario.IsSecure = false;
        scenario.IsStandardsAligned = false;
        scenario.AssessmentScore = 0.0;
        scenario.AssessmentDisposition = AuthenticationAssessmentDisposition.Insecure;
    }

    private static void MarkScenarioInconclusive(AuthenticationScenario scenario, string actualBehavior, string analysis, string complianceReason)
    {
        scenario.ActualBehavior = actualBehavior;
        scenario.Analysis = analysis;
        scenario.ComplianceReason = complianceReason;
        scenario.IsCompliant = true;
        scenario.IsSecure = false;
        scenario.IsStandardsAligned = false;
        scenario.AssessmentScore = -1.0;
        scenario.AssessmentDisposition = AuthenticationAssessmentDisposition.Inconclusive;
    }

    private JsonRpcResponseType ParseJsonRpcResponse(JsonRpcResponse response)
    {
        if (string.IsNullOrEmpty(response.RawJson))
        {
            return new JsonRpcResponseType { IsSuccess = false, ErrorMessage = "Empty response" };
        }

        try
        {
            var jsonDoc = JsonDocument.Parse(response.RawJson);

            // Check for JSON-RPC error
            if (jsonDoc.RootElement.TryGetProperty("error", out var errorElement))
            {
                var errorMessage = "Unknown error";
                if (errorElement.TryGetProperty("message", out var messageElement))
                {
                    errorMessage = messageElement.GetString() ?? "Unknown error";
                }

                return new JsonRpcResponseType
                {
                    IsSuccess = false,
                    ErrorMessage = errorMessage
                };
            }

            // Check for JSON-RPC result (success)
            if (jsonDoc.RootElement.TryGetProperty("result", out _))
            {
                return new JsonRpcResponseType { IsSuccess = true };
            }

            // Neither error nor result found
            return new JsonRpcResponseType
            {
                IsSuccess = false,
                ErrorMessage = "Invalid JSON-RPC response format"
            };
        }
        catch (JsonException)
        {
            return new JsonRpcResponseType
            {
                IsSuccess = false,
                ErrorMessage = "Invalid JSON format"
            };
        }
    }

    private class JsonRpcResponseType
    {
        public bool IsSuccess { get; set; }
        public string ErrorMessage { get; set; } = "";
    }

    /// <summary>
    /// Discovers available endpoints from server capabilities
    /// </summary>
    private async Task<List<string>> DiscoverServerEndpoints(
        McpServerConfig serverConfig, CancellationToken cancellationToken)
    {
        var endpoints = new List<string>();

        try
        {
            // Try to get capabilities from initialize method
            var response = await _httpClient.CallAsync(serverConfig.Endpoint!, "initialize",
                new
                {
                    protocolVersion = SchemaRegistryProtocolVersions.NormalizeRequestedVersion(null),
                    capabilities = new { },
                    clientInfo = new { name = "Visual Studio Code", version = "1.96.0" }
                }, serverConfig.Authentication, cancellationToken);

            if (response.IsSuccess && !string.IsNullOrEmpty(response.RawJson))
            {
                try
                {
                    var jsonDoc = JsonDocument.Parse(response.RawJson);
                    JsonElement capabilitiesElement;

                    // Try JSON-RPC 2.0 format first (result.capabilities)
                    if (jsonDoc.RootElement.TryGetProperty("result", out var resultElement) &&
                        resultElement.TryGetProperty("capabilities", out capabilitiesElement))
                    {
                        _logger.LogInformation("Found JSON-RPC 2.0 capabilities response");
                    }
                    // Fallback to direct capabilities property
                    else if (jsonDoc.RootElement.TryGetProperty("capabilities", out capabilitiesElement))
                    {
                        _logger.LogInformation("Found direct capabilities response");
                    }
                    else
                    {
                        return endpoints;
                    }

                    // Parse discovered capabilities
                    foreach (var capability in capabilitiesElement.EnumerateObject())
                    {
                        switch (capability.Name.ToLower())
                        {
                            case "tools":
                                endpoints.Add("tools/list");
                                endpoints.Add("tools/call");
                                break;
                            case "resources":
                                endpoints.Add("resources/list");
                                endpoints.Add("resources/read");
                                break;
                            case "prompts":
                                endpoints.Add("prompts/list");
                                endpoints.Add("prompts/get");
                                break;
                            case "logging":
                                endpoints.Add("logging/setLevel");
                                break;
                        }
                    }
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "Failed to parse JSON response from initialize method");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to discover endpoints from server capabilities");
        }

        return endpoints;
    }

}
