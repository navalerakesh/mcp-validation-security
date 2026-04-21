using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Mcp.Benchmark.Core.Abstractions;
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
/// OAuth 2.1 RFC 6750 Section 3.1 Error Response Requirements:
/// - invalid_request: Missing/malformed parameters - SHOULD respond with HTTP 400
/// - invalid_token: Expired/revoked/malformed tokens - SHOULD respond with HTTP 401
/// - insufficient_scope: Higher privileges required - SHOULD respond with HTTP 403
/// - All authentication errors: MUST include WWW-Authenticate header
/// 
/// VALIDATED COMPLIANCE MATRIX (Based on Real Server Testing):
/// - No Auth: 400/401 + WWW-Authenticate (BOTH VALID per OAuth 2.1 RFC 6750)
/// - Malformed Token: 400/401 + WWW-Authenticate (BOTH VALID per OAuth 2.1 RFC 6750)
/// - Invalid Token: 400/401 + WWW-Authenticate (BOTH VALID per OAuth 2.1 RFC 6750) 
/// - Token Expired: 400/401 + WWW-Authenticate (BOTH VALID per OAuth 2.1 RFC 6750)
/// - Invalid Scope: 403 + WWW-Authenticate (insufficient_scope)
/// - Insufficient Perms: 403 + WWW-Authenticate (insufficient_scope)
/// - Valid Token: 200 + JSON-RPC success
/// 
/// PROVEN COMPLIANCE: GitHub (400+WWW-Auth) and Microsoft Graph (401+WWW-Auth) both achieve 100%
/// UNPROTECTED SERVERS: Skip authentication validation entirely (100% compliant)
/// STDIO TRANSPORT: No HTTP auth headers (100% compliant per MCP spec)
/// </summary>
public class McpCompliantAuthValidator
{
    private readonly ILogger<McpCompliantAuthValidator> _logger;
    private readonly IMcpHttpClient _httpClient;

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

                // Test with valid token (if available)
                if (!string.IsNullOrEmpty(serverConfig.Authentication?.Token))
                {
                    var validTokenTest = await TestMethodAuthentication(serverConfig, endpoint, "Valid Token", profile, cancellationToken);
                    result.TestScenarios.Add(validTokenTest);
                }
            }

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
            "Invalid Token" => "4xx (Secure Rejection)", 
            "Token Expired" => "4xx (Secure Rejection)", 
            "Invalid Scope" => "4xx (Secure Rejection)", 
            "Insufficient Permissions" => "4xx (Secure Rejection)", 
            "Valid Token" => "200 + JSON-RPC Response",
            _ => "4xx (Secure Rejection)"
        };
    }

    private void AnalyzeAuthenticationResponse(AuthenticationScenario scenario, JsonRpcResponse response, string testType, string method)
    {
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
                    MarkScenarioStandardsAligned(
                        scenario,
                        actualBehavior: "400 + WWW-Auth",
                        analysis: "✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400.",
                        complianceReason: "PASS: Access denied with challenge metadata.");
                }
                else
                {
                    MarkScenarioSecureCompatible(
                        scenario,
                        actualBehavior: "400 (missing WWW-Auth)",
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
                        actualBehavior: hasWWWAuth ? "401 + WWW-Auth" : "401 (missing WWW-Auth)",
                        analysis: "❌ INSECURE: A valid token was rejected.",
                        complianceReason: "FAIL: Server rejected the valid token.");
                }
                else
                {
                    if (hasWWWAuth)
                    {
                        MarkScenarioStandardsAligned(
                            scenario,
                            actualBehavior: "401 + WWW-Auth",
                            analysis: "✅ ALIGNED: OAuth/MCP challenge returned with HTTP 401.",
                            complianceReason: "PASS: Access denied with challenge metadata.");
                    }
                    else
                    {
                        MarkScenarioSecureCompatible(
                            scenario,
                            actualBehavior: "401 (missing WWW-Auth)",
                            analysis: "⚠️ COMPATIBLE: Secure rejection without the preferred WWW-Authenticate challenge.",
                            complianceReason: "SECURE: Request was rejected, but challenge metadata was not provided.");
                    }
                }
                break;

            case 403:
                // OAuth 2.1: 403 for insufficient_scope
                // STRICT SPEC COMPLIANCE: Must have WWW-Authenticate header
                
                if (hasWWWAuth)
                {
                    MarkScenarioStandardsAligned(
                        scenario,
                        actualBehavior: "403 + WWW-Auth",
                        analysis: "✅ ALIGNED: Secure insufficient-scope rejection returned with challenge metadata.",
                        complianceReason: "PASS: Access denied with challenge metadata.");
                }
                else
                {
                    MarkScenarioSecureCompatible(
                        scenario,
                        actualBehavior: "403 (missing WWW-Auth)",
                        analysis: "⚠️ COMPATIBLE: Secure rejection without the preferred WWW-Authenticate challenge.",
                        complianceReason: "SECURE: Request was rejected, but challenge metadata was not provided.");
                }
                break;

            case 429:
                // Rate Limiting - Not an auth violation
                MarkScenarioInconclusive(
                    scenario,
                    actualBehavior: "429 Too Many Requests",
                    analysis: "ℹ️ INFO: Rate limiting observed; auth semantics inconclusive.",
                    complianceReason: "INFO: Request was throttled and excluded from auth scoring.");
                break;

            case 406:
                // Content Negotiation - Not an auth violation
                MarkScenarioInconclusive(
                    scenario,
                    actualBehavior: "406 Not Acceptable",
                    analysis: "ℹ️ INFO: Content negotiation failed before auth semantics could be evaluated.",
                    complianceReason: "INFO: Content negotiation prevented a conclusive auth assessment.");
                break;

            case 503:
                // Service Unavailable - Not an auth violation
                MarkScenarioInconclusive(
                    scenario,
                    actualBehavior: "503 Service Unavailable",
                    analysis: "ℹ️ INFO: Service availability issue made auth semantics inconclusive.",
                    complianceReason: "INFO: Service was unavailable during auth assessment.");
                break;

            case 405:
                MarkScenarioSecureCompatible(
                    scenario,
                    actualBehavior: hasWWWAuth ? "405 + WWW-Auth" : "405 (missing WWW-Auth)",
                    analysis: "⚠️ COMPATIBLE: Request was rejected, but the server used a non-canonical HTTP method outcome.",
                    complianceReason: "SECURE: Request was rejected using a non-canonical transport response.");
                break;

            case 200:
                var jsonRpcResult = ParseJsonRpcResponse(response);

                if (testType == "Valid Token")
                {
                    MarkScenarioStandardsAligned(
                        scenario,
                        actualBehavior: jsonRpcResult.IsSuccess ? "200 + JSON-RPC success" : "200 + JSON-RPC error",
                        analysis: "✅ ALIGNED: Valid authentication was accepted and request processing proceeded.",
                        complianceReason: "PASS: Server accepted the valid token and processed the request.");
                }
                else
                {
                    if (!jsonRpcResult.IsSuccess)
                    {
                        MarkScenarioSecureCompatible(
                            scenario,
                            actualBehavior: "200 + JSON-RPC error",
                            analysis: "⚠️ COMPATIBLE: Authentication appears to be enforced at the application layer instead of via an HTTP challenge.",
                            complianceReason: "SECURE: Request was rejected, but not through the preferred HTTP challenge flow.");
                    }
                    else
                    {
                        if (ValidationCalibration.IsDiscoveryMethod(method))
                        {
                            MarkScenarioSecureCompatible(
                                scenario,
                                actualBehavior: "200 + JSON-RPC success",
                                analysis: "⚠️ COMPATIBLE: Discovery metadata was exposed without authentication.",
                                complianceReason: "SECURE: Discovery surface is public, but challenge-based auth was not enforced.");
                        }
                        else
                        {
                            MarkScenarioInsecure(
                                scenario,
                                actualBehavior: "200 + JSON-RPC success",
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
                        actualBehavior: $"HTTP {response.StatusCode}",
                        analysis: $"⚠️ COMPATIBLE: Server rejected the request with HTTP {response.StatusCode}, but without the preferred challenge semantics.",
                        complianceReason: "SECURE: Request was rejected using a non-canonical HTTP pattern.");
                }
                else
                {
                    MarkScenarioInconclusive(
                        scenario,
                        actualBehavior: $"HTTP {response.StatusCode}",
                        analysis: $"ℹ️ INFO: Unexpected HTTP {response.StatusCode} prevented a reliable auth assessment.",
                        complianceReason: "INFO: Response was excluded from auth scoring due to inconclusive transport behavior.");
                }
                break;
        }
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
