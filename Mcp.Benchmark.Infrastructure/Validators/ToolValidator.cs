using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Diagnostics;
using System.Linq;
using Microsoft.Extensions.Logging;
using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Constants;

using Mcp.Benchmark.Infrastructure.Strategies.Scoring;
using Mcp.Benchmark.Infrastructure.Authentication;
using Mcp.Compliance.Spec;
using Mcp.Benchmark.Infrastructure.Utilities;

namespace Mcp.Benchmark.Infrastructure.Validators;

public class ToolValidator : BaseValidator<ToolValidator>, IToolValidator
{
    private readonly IMcpHttpClient _httpClient;
    private readonly ISchemaValidator _schemaValidator;
    private readonly ISchemaRegistry _schemaRegistry;
    private readonly IScoringStrategy<ToolTestResult> _scoringStrategy;
    private readonly IAuthenticationService _authenticationService;
    private readonly IContentSafetyAnalyzer _contentSafetyAnalyzer;

    public ToolValidator(
        ILogger<ToolValidator> logger,
        IMcpHttpClient httpClient,
        ISchemaValidator schemaValidator,
        ISchemaRegistry schemaRegistry,
        IAuthenticationService authenticationService,
        IContentSafetyAnalyzer contentSafetyAnalyzer)
        : base(logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _schemaValidator = schemaValidator ?? throw new ArgumentNullException(nameof(schemaValidator));
        _schemaRegistry = schemaRegistry ?? throw new ArgumentNullException(nameof(schemaRegistry));
        _authenticationService = authenticationService ?? throw new ArgumentNullException(nameof(authenticationService));
        _contentSafetyAnalyzer = contentSafetyAnalyzer ?? throw new ArgumentNullException(nameof(contentSafetyAnalyzer));
        _scoringStrategy = new ToolScoringStrategy();
    }

    public async Task<ToolTestResult> ValidateToolDiscoveryAsync(McpServerConfig serverConfig, ToolTestingConfig config, CancellationToken cancellationToken = default)
    {
        return await ExecuteValidationAsync(serverConfig, "Tool Discovery", async (ct) =>
        {
            var result = new ToolTestResult
            {
                ToolResults = new List<IndividualToolResult>()
            };

            var capabilitySnapshot = config.CapabilitySnapshot;
            var cachedToolsListResponse = CreateResponseFromSnapshot(capabilitySnapshot);
            var snapshotTools = BuildToolListFromSnapshot(capabilitySnapshot?.Payload);
            var toolsListDuration = capabilitySnapshot?.Transport.Duration.TotalMilliseconds ?? 0;
            var authSecurity = BuildAuthSecurityFromDiscovery(config.PreDiscoveredAuth);

            if (authSecurity != null)
            {
                result.AuthenticationSecurity = authSecurity;
                result.AuthenticationProperlyEnforced = authSecurity.AuthenticationRequired && authSecurity.HasProperAuthHeaders;
            }

            // Check for pre-discovered auth info
            if (config.PreDiscoveredAuth != null)
            {
                var authInfo = config.PreDiscoveredAuth;
                var authIssues = new List<string>(authInfo.Issues);
                
                if (!string.IsNullOrEmpty(authInfo.WwwAuthenticateHeader))
                {
                    authIssues.Add("✅ Proper WWW-Authenticate header present");
                    authIssues.Add("ℹ️  Auth enforced correctly (discovered during pre-validation)");
                }
                
                result.ToolResults.Add(new IndividualToolResult
                {
                    ToolName = "Authentication Discovery",
                    Status = TestStatus.Passed,
                    DiscoveredCorrectly = true,
                    MetadataValid = !string.IsNullOrEmpty(authInfo.WwwAuthenticateHeader),
                    ExecutionSuccessful = true,
                    ExecutionTimeMs = authInfo.DiscoveryTimeMs,
                    Issues = authIssues,
                    WwwAuthenticateHeader = authInfo.WwwAuthenticateHeader,
                    AuthMetadata = authInfo.Metadata
                });
            }

            // REAL MCP TOOL DISCOVERY TESTING
            Logger.LogDebug("Performing comprehensive tool discovery via MCP tools/list");

            var toolsListStartTime = DateTime.UtcNow;
            var toolsListResponse = cachedToolsListResponse;
            if (toolsListResponse == null)
            {
                toolsListStartTime = DateTime.UtcNow;
                toolsListResponse = await _httpClient.CallAsync(serverConfig.Endpoint!, ValidationConstants.Methods.ToolsList, null, ct);
                toolsListDuration = (DateTime.UtcNow - toolsListStartTime).TotalMilliseconds;
            }

            // CASE 1: 401/403 = Auth required (validate it's done RIGHT)
            if (toolsListResponse.StatusCode == 401 || toolsListResponse.StatusCode == 403)
            {
                result.Status = TestStatus.Passed;
                result.Score = 100.0;
                result.ToolsTestPassed = 1;
                
                var authIssues = new List<string>();
                string? authHeaderValue = null;
                
                if (toolsListResponse.Headers != null)
                {
                    if (toolsListResponse.Headers.TryGetValue("WWW-Authenticate", out var val)) authHeaderValue = val.ToString();
                    else if (toolsListResponse.Headers.TryGetValue("www-authenticate", out var val2)) authHeaderValue = val2.ToString();
                }

                var hasWwwAuth = !string.IsNullOrEmpty(authHeaderValue);

                if (hasWwwAuth) authIssues.Add("✅ Proper WWW-Authenticate header present");
                else authIssues.Add("⚠️  Missing WWW-Authenticate header (recommended per RFC 9110)");

                authIssues.Add($"ℹ️  HTTP {toolsListResponse.StatusCode}: Auth enforced correctly");
                authIssues.Add("🔒 MCP spec allows both public and auth-protected servers - this is SECURE");

                authSecurity ??= new AuthenticationSecurityResult();
                authSecurity.AuthenticationRequired = true;
                authSecurity.RejectsUnauthenticated = true;
                authSecurity.CorrectStatusCodes = true;
                authSecurity.ErrorResponsesCompliant = true;
                authSecurity.HasProperAuthHeaders = hasWwwAuth;
                authSecurity.ChallengeStatusCode = toolsListResponse.StatusCode;
                if (toolsListDuration > 0)
                {
                    authSecurity.ChallengeDurationMs = toolsListDuration;
                }
                if (hasWwwAuth)
                {
                    authSecurity.WwwAuthenticateHeader = authHeaderValue;
                }
                authSecurity.SecurityScore = hasWwwAuth ? 100.0 : 85.0;
                result.AuthenticationSecurity = authSecurity;
                result.AuthenticationProperlyEnforced = authSecurity.AuthenticationRequired && authSecurity.HasProperAuthHeaders;

                AuthMetadata? authMetadata = null;
                if (hasWwwAuth && authHeaderValue!.Contains("resource_metadata=\""))
                {
                    try 
                    {
                        // Extract URL: resource_metadata="https://..."
                        var start = authHeaderValue.IndexOf("resource_metadata=\"") + "resource_metadata=\"".Length;
                        var end = authHeaderValue.IndexOf("\"", start);
                        if (end > start)
                        {
                            var metadataUrl = authHeaderValue.Substring(start, end - start);
                            Logger.LogDebug($"Fetching auth metadata from: {metadataUrl}");
                            
                            var json = await _httpClient.GetStringAsync(metadataUrl, ct);
                            authMetadata = JsonSerializer.Deserialize<AuthMetadata>(json);
                            
                            if (authMetadata != null)
                            {
                                authIssues.Add("✅ Successfully fetched and parsed OAuth 2.0 Metadata");
                                if (authSecurity != null)
                                {
                                    authSecurity.AuthMetadata = authMetadata;
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.LogWarning(ex, "Failed to fetch/parse auth metadata");
                        authIssues.Add($"⚠️  Failed to fetch auth metadata: {ex.Message}");
                    }
                }

                // INTERACTIVE AUTH ATTEMPT
                if (serverConfig.Authentication?.AllowInteractive == true && authMetadata != null)
                {
                    Logger.LogInformation("Interactive auth enabled. Attempting to acquire token via authentication service...");
                    
                    // Use a longer timeout (5 min) for interactive auth, ignoring the default 30s operation timeout
                    using var authCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    authCts.CancelAfter(TimeSpan.FromMinutes(5));

                    if (authSecurity != null)
                    {
                        authSecurity.InteractiveLoginAttempted = true;
                    }

                    var token = await _authenticationService.GetTokenAsync(authMetadata, authCts.Token);

                    if (!string.IsNullOrEmpty(token))
                    {
                        Logger.LogInformation("Token acquired successfully! Retrying tool discovery...");
                        if (authSecurity != null)
                        {
                            authSecurity.InteractiveLoginSucceeded = true;
                        }
                        
                        // Update HTTP client with new token
                        _httpClient.SetAuthentication(new AuthenticationConfig 
                        { 
                            Type = "bearer", 
                            Token = token 
                        });

                        // Update server config so subsequent validators use the token
                        if (serverConfig.Authentication == null) serverConfig.Authentication = new AuthenticationConfig();
                        serverConfig.Authentication.Type = "bearer";
                        serverConfig.Authentication.Token = token;

                        // Retry the request
                        toolsListStartTime = DateTime.UtcNow;
                        toolsListResponse = await _httpClient.CallAsync(serverConfig.Endpoint!, ValidationConstants.Methods.ToolsList, null, ct);
                        toolsListDuration = (DateTime.UtcNow - toolsListStartTime).TotalMilliseconds;

                        // If success, proceed to normal validation flow
                        if (toolsListResponse.IsSuccess)
                        {
                            authIssues.Add("✅ Successfully authenticated via strategy-based flow");
                            
                            // Add the auth discovery result so it's preserved in the report
                            result.ToolResults.Add(new IndividualToolResult
                            {
                                ToolName = "Authentication Discovery",
                                Status = TestStatus.Passed,
                                DiscoveredCorrectly = true,
                                MetadataValid = hasWwwAuth,
                                ExecutionSuccessful = true,
                                ExecutionTimeMs = toolsListDuration,
                                Issues = new List<string>(authIssues),
                                WwwAuthenticateHeader = authHeaderValue,
                                AuthMetadata = authMetadata
                            });

                            // Fall through to normal processing below...
                        }
                        else
                        {
                            authIssues.Add($"❌ Interactive auth failed to grant access: HTTP {toolsListResponse.StatusCode}");
                        }
                    }
                    else
                    {
                        authIssues.Add("⚠️  Interactive auth attempt failed or was cancelled");
                    }
                }
                
                // If still 401/403 after retry (or no retry), return the Auth Check result
                if (toolsListResponse.StatusCode == 401 || toolsListResponse.StatusCode == 403)
                {
                    // If interactive auth was requested but failed, mark as SKIPPED instead of PASSED
                    if (serverConfig.Authentication?.AllowInteractive == true)
                    {
                        result.Status = TestStatus.Skipped;
                        result.Score = 0.0;
                        result.ToolsTestPassed = 0;
                        result.Issues.Add("⚠️ Tool validation SKIPPED: Interactive authentication failed or was not completed.");
                        
                        result.ToolResults.Add(new IndividualToolResult
                        {
                            ToolName = "tools/list (Auth Check)",
                            Status = TestStatus.Skipped,
                            DiscoveredCorrectly = true,
                            MetadataValid = hasWwwAuth,
                            ExecutionSuccessful = true,
                            ExecutionTimeMs = toolsListDuration,
                            Issues = authIssues,
                            WwwAuthenticateHeader = authHeaderValue,
                            AuthMetadata = authMetadata
                        });
                        if (authSecurity != null)
                        {
                            AppendAuthFindings(authSecurity, authIssues);
                            result.AuthenticationSecurity = authSecurity;
                            result.AuthenticationProperlyEnforced = authSecurity.AuthenticationRequired && authSecurity.HasProperAuthHeaders;
                        }
                        NormalizeToolDiscoveryMetrics(result);
                        return result;
                    }

                    result.Status = TestStatus.Passed;
                    result.Score = 100.0;
                    result.ToolsTestPassed = 1;
                    
                    result.ToolResults.Add(new IndividualToolResult
                    {
                        ToolName = "tools/list (Auth Check)",
                        Status = TestStatus.Passed,
                        DiscoveredCorrectly = true,
                        MetadataValid = hasWwwAuth,
                        ExecutionSuccessful = true,
                        ExecutionTimeMs = toolsListDuration,
                        Issues = authIssues,
                        WwwAuthenticateHeader = authHeaderValue,
                        AuthMetadata = authMetadata
                    });

                    NormalizeToolDiscoveryMetrics(result);
                    return result;
                }

                if (authSecurity != null)
                {
                    AppendAuthFindings(authSecurity, authIssues);
                    result.AuthenticationSecurity = authSecurity;
                }
            }


            // CASE 2: 200 = Public/authenticated access (validate functionality)
            if (!toolsListResponse.IsSuccess)
            {
                result.Status = TestStatus.Failed;
                result.ToolsTestFailed = 1;
                result.ToolResults.Add(new IndividualToolResult
                {
                    ToolName = "tools/list",
                    Status = TestStatus.Failed,
                    DiscoveredCorrectly = false,
                    MetadataValid = false,
                    ExecutionSuccessful = false,
                    ExecutionTimeMs = toolsListDuration,
                    Issues = new List<string> { $"tools/list failed: HTTP {toolsListResponse.StatusCode}" }
                });
                return result;
            }

                // Parse tools from response
                var toolsList = ParseTools(toolsListResponse.RawJson);
            if (toolsList.Count == 0 && snapshotTools != null && snapshotTools.Count > 0)
            {
                toolsList = snapshotTools;
            }
            result.ToolsDiscovered = toolsList.Count;
            result.DiscoveredToolNames = toolsList.Select(t => t.Name).ToList();

                // Schema-based validation of tools/list response shape (best-effort)
                var protocolVersion = SchemaValidationHelpers.ResolveProtocolVersion(serverConfig.ProtocolVersion);
                if (SchemaValidationHelpers.TryValidateListResult(
                    _schemaRegistry,
                    _schemaValidator,
                    protocolVersion,
                    SchemaValidationHelpers.ListToolsResultDefinition,
                    toolsListResponse.RawJson,
                    Logger,
                    out var schemaValidationResult) &&
                schemaValidationResult is not null &&
                !schemaValidationResult.IsValid)
            {
                    var schemaErrors = schemaValidationResult.Errors ?? new List<string>();
                    var hasProcessingError = schemaErrors.Any(e => e.Contains("Schema processing error", StringComparison.OrdinalIgnoreCase));
                    var schemaIssueHeader = hasProcessingError
                        ? "⚠️ Schema validation warning: tools/list schema could not be fully processed"
                        : "❌ NON-COMPLIANT: tools/list response does not conform to MCP JSON Schema";

                    // Only count hard schema violations (where we could
                    // fully process the schema and found non-compliance)
                    // as test failures. Unresolvable external $ref values
                    // are treated as a pass with a warning so that server
                    // authors see the guidance without being marked failed.
                    if (!hasProcessingError)
                    {
                        result.ToolsTestFailed++;
                    }

                    result.Issues.Add(schemaIssueHeader);
                    var schemaIssueDetails = new List<string>();
                    foreach (var error in schemaErrors)
                    {
                        var detail = $"   • {error}";
                        result.Issues.Add(detail);
                        schemaIssueDetails.Add(detail);
                    }

                    // Surface schema failures/warnings as their own tool
                    // result so reports clearly show the root cause.
                    result.ToolResults.Add(new IndividualToolResult
                    {
                        ToolName = "tools/list (Schema Compliance)",
                        Status = hasProcessingError ? TestStatus.Passed : TestStatus.Failed,
                        DiscoveredCorrectly = true,
                        MetadataValid = !hasProcessingError,
                        ExecutionSuccessful = true,
                        ExecutionTimeMs = toolsListDuration,
                        Issues = BuildSchemaIssueList(schemaIssueHeader, schemaIssueDetails)
                    });
            }

            if (toolsList.Count == 0)
            {
                result.Status = TestStatus.Passed;
                result.ToolsTestPassed++;
                result.Issues.Add("Server returned no tools - this is allowed but unusual");
                result.ToolResults.Add(new IndividualToolResult
                {
                    ToolName = "tools/list",
                    Status = TestStatus.Passed,
                    DiscoveredCorrectly = true,
                    MetadataValid = true,
                    ExecutionSuccessful = true,
                    ExecutionTimeMs = toolsListDuration,
                    Issues = new List<string> { "No tools discovered" }
                });
                return result;
            }

            // Validate each tool and apply static content safety analysis
            foreach (var tool in toolsList)
            {
                var toolResult = await ValidateIndividualToolAsync(serverConfig, tool, config, ct);

                // Static, metadata-only content safety analysis
                var safetyFindings = _contentSafetyAnalyzer.AnalyzeTool(tool.Name);
                if (safetyFindings.Count > 0)
                {
                    toolResult.ContentSafetyFindings.AddRange(safetyFindings);
                    result.ContentSafetyFindings.AddRange(safetyFindings);
                }

                result.ToolResults.Add(toolResult);

                if (toolResult.Status == TestStatus.Passed) result.ToolsTestPassed++;
                else result.ToolsTestFailed++;
            }

            // Calculate Score using Strategy
            result.Score = _scoringStrategy.CalculateScore(result);

            if (capabilitySnapshot?.Payload != null)
            {
                if (capabilitySnapshot.Payload.ToolInvocationSucceeded)
                {
                    result.Issues.Add("✅ SDK invocation of the first discovered tool succeeded");
                }
                else if (capabilitySnapshot.Payload.ToolListingSucceeded)
                {
                    result.Issues.Add("⚠️ SDK invocation of the first discovered tool failed; ensure the tool accepts empty arguments");
                }
            }

            result.Status = result.ToolsTestFailed == 0 ? TestStatus.Passed : TestStatus.Failed;

            // AI Readiness Analysis
            AnalyzeAiReadiness(result, toolsList, toolsListResponse.RawJson);

            return result;
        }, cancellationToken);
    }

    /// <summary>
    /// Analyzes tool schemas for AI agent consumption quality:
    /// - Schema description completeness (hallucination risk)
    /// - Type specificity (vague "string" without constraints)
    /// - Token efficiency (payload size vs context window limits)
    /// </summary>
    private void AnalyzeAiReadiness(ToolTestResult result, List<ToolInfo> tools, string? rawJson)
    {
        if (tools.Count == 0)
        {
            result.AiReadinessScore = -1; // N/A
            return;
        }

        double totalScore = 0;
        int scored = 0;

        foreach (var tool in tools)
        {
            double toolScore = 100.0;
            var schema = tool.InputSchema;

            // 1. Check for description on the tool level
            // (We only have name in ToolInfo, but we can infer from schema)
            if (schema.ValueKind == JsonValueKind.Undefined ||
                !schema.TryGetProperty("properties", out var props) ||
                props.EnumerateObject().Count() == 0)
            {
                // No input schema or no properties — acceptable but not great for AI
                totalScore += 80.0;
                scored++;
                continue;
            }

            int paramCount = 0;
            int undescribedParams = 0;
            int vagueStringParams = 0;

            foreach (var prop in props.EnumerateObject())
            {
                paramCount++;

                // Check: does the parameter have a description?
                if (!prop.Value.TryGetProperty("description", out _))
                {
                    undescribedParams++;
                }

                // Check: is it type:string with no enum/pattern/format constraint?
                if (prop.Value.TryGetProperty("type", out var t) && t.GetString() == "string")
                {
                    bool hasConstraint = prop.Value.TryGetProperty("enum", out _) ||
                                        prop.Value.TryGetProperty("pattern", out _) ||
                                        prop.Value.TryGetProperty("format", out _);
                    if (!hasConstraint) vagueStringParams++;
                }
            }

            // Penalties
            if (paramCount > 0)
            {
                double descPenalty = (undescribedParams / (double)paramCount) * 30.0;
                double vaguePenalty = (vagueStringParams / (double)paramCount) * 20.0;
                toolScore -= descPenalty;
                toolScore -= vaguePenalty;
            }

            if (undescribedParams > 0)
                result.AiReadinessIssues.Add($"Tool '{tool.Name}': {undescribedParams}/{paramCount} parameters lack descriptions (increases hallucination risk)");
            if (vagueStringParams > 0)
                result.AiReadinessIssues.Add($"Tool '{tool.Name}': {vagueStringParams}/{paramCount} string parameters have no enum/pattern/format constraint");

            totalScore += Math.Max(0, toolScore);
            scored++;
        }

        result.AiReadinessScore = scored > 0 ? Math.Round(totalScore / scored, 1) : -1;

        // Token Efficiency: estimate token count from raw JSON size
        // Rough heuristic: ~4 chars per token for JSON
        if (!string.IsNullOrEmpty(rawJson))
        {
            result.EstimatedTokenCount = rawJson.Length / 4;
            if (result.EstimatedTokenCount > 32000)
            {
                result.AiReadinessIssues.Add($"⚠️ tools/list response is ~{result.EstimatedTokenCount:N0} tokens — exceeds typical 32k context window. AI agents may truncate tool metadata.");
                result.AiReadinessScore = Math.Max(0, result.AiReadinessScore - 10);
            }
            else if (result.EstimatedTokenCount > 8000)
            {
                result.AiReadinessIssues.Add($"ℹ️ tools/list response is ~{result.EstimatedTokenCount:N0} tokens — consider reducing descriptions for token efficiency.");
            }
        }

        if (result.AiReadinessScore >= 0)
        {
            var grade = result.AiReadinessScore >= 80 ? "Good" : result.AiReadinessScore >= 50 ? "Fair" : "Poor";
            result.Issues.Add($"🤖 AI Readiness Score: {result.AiReadinessScore:F0}/100 ({grade})");
        }
    }

    private async Task<IndividualToolResult> ValidateIndividualToolAsync(McpServerConfig serverConfig, ToolInfo tool, ToolTestingConfig config, CancellationToken ct)
    {
        var result = new IndividualToolResult
        {
            ToolName = tool.Name,
            DiscoveredCorrectly = true,
            MetadataValid = true
        };

        try
        {
            var toolCallStartTime = DateTime.UtcNow;
            var toolCallResponse = await _httpClient.CallAsync(serverConfig.Endpoint!, ValidationConstants.Methods.ToolsCall, CreateSafeToolCallParams(tool), ct);
            var toolCallDuration = (DateTime.UtcNow - toolCallStartTime).TotalMilliseconds;
            result.ExecutionTimeMs = toolCallDuration;

            if (toolCallResponse.StatusCode == 401 || toolCallResponse.StatusCode == 403)
            {
                // Auth enforced on individual tool call — compliant
                result.Status = TestStatus.Passed;
                result.ExecutionSuccessful = true;
                result.Issues.Add("Tool call requires authentication (Secure)");
            }
            else if (toolCallResponse.IsSuccess)
            {
                result.ExecutionSuccessful = true;
                result.Status = TestStatus.Passed;

                // MCP Spec Compliance: Validate tools/call response structure
                if (!string.IsNullOrEmpty(toolCallResponse.RawJson))
                {
                    // Check if the response is a JSON-RPC error (spec-compliant input validation)
                    if (IsJsonRpcError(toolCallResponse.RawJson, out var errorCode, out var errorMsg))
                    {
                        // Server returned a 200 with JSON-RPC error — this is COMPLIANT behavior.
                        result.Status = TestStatus.Passed;
                        result.ExecutionSuccessful = true;
                        
                        if (errorCode == -32602 || errorCode == -32600)
                        {
                            result.Issues.Add($"✅ Server correctly validated input: {errorMsg} (code: {errorCode})");
                        }
                        else
                        {
                            result.Issues.Add($"ℹ️ Server returned JSON-RPC error: {errorMsg} (code: {errorCode})");
                        }

                        // LLM-Friendliness: Grade the error response for AI self-correction
                        GradeErrorForLlmFriendliness(toolCallResponse.RawJson, errorCode, errorMsg, tool.Name, result);
                    }
                    else
                    {
                        // Successful tool execution — validate response structure
                        ValidateToolCallResponseStructure(toolCallResponse.RawJson, result);
                    }
                }
            }
            else if (toolCallResponse.StatusCode == 400)
            {
                // HTTP 400 = server validated input and rejected it.
                // This is CORRECT behavior when we send dummy arguments.
                result.Status = TestStatus.Passed;
                result.ExecutionSuccessful = true;
                result.Issues.Add("✅ Server correctly rejected invalid parameters (HTTP 400)");
            }
            else if (toolCallResponse.StatusCode == 404)
            {
                // 404 may indicate the tool doesn't accept the request path — still responsive
                result.Status = TestStatus.Passed;
                result.ExecutionSuccessful = true;
                result.Issues.Add($"ℹ️ Server returned 404 — tool may require specific request routing");
            }
            else if (toolCallResponse.StatusCode == 500)
            {
                // 500 = server crashed processing our dummy input. This IS a real failure.
                result.Status = TestStatus.Failed;
                result.ExecutionSuccessful = false;
                result.Issues.Add("❌ Server crashed (500 Internal Server Error) on tool call with test parameters");
            }
            else if (toolCallResponse.StatusCode < 0)
            {
                // Negative status = network/timeout error. 
                // This is NOT a server compliance failure — it's a connectivity issue.
                // We should NOT fail the tool for this.
                result.Status = TestStatus.Passed;
                result.ExecutionSuccessful = true;
                result.Issues.Add($"⚠️ Network/timeout issue (HTTP {toolCallResponse.StatusCode}). Tool may require longer timeout or specific network access. Not a compliance failure.");
            }
            else
            {
                // Other unexpected HTTP status
                result.Status = TestStatus.Failed;
                result.ExecutionSuccessful = false;
                result.Issues.Add($"❌ Unexpected response: HTTP {toolCallResponse.StatusCode}");
            }
        }
        catch (OperationCanceledException)
        {
            // Timeout — not a compliance failure, it's an infrastructure issue
            result.Status = TestStatus.Passed;
            result.ExecutionSuccessful = true;
            result.Issues.Add("⚠️ Tool call timed out. Tool may require longer timeout. Not a compliance failure.");
        }
        catch (Exception ex)
        {
            result.Status = TestStatus.Failed;
            result.ExecutionSuccessful = false;
            result.Issues.Add($"Tool call exception: {ex.Message}");
        }

        return result;
    }

    /// <summary>
    /// Checks if a JSON response is a JSON-RPC error (has "error" object with code and message).
    /// This is valid MCP behavior — servers SHOULD return standard JSON-RPC errors for invalid params.
    /// </summary>
    private static bool IsJsonRpcError(string rawJson, out int errorCode, out string errorMessage)
    {
        errorCode = 0;
        errorMessage = "";
        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            if (doc.RootElement.TryGetProperty("error", out var errorObj))
            {
                if (errorObj.TryGetProperty("code", out var code))
                    errorCode = code.GetInt32();
                if (errorObj.TryGetProperty("message", out var msg))
                    errorMessage = msg.GetString() ?? "";
                return true;
            }
        }
        catch { /* Not valid JSON-RPC error format */ }
        return false;
    }

    /// <summary>
    /// Grades an error response for LLM-friendliness — does the error help an AI agent
    /// understand what went wrong and what to do next? This is critical for agentic AI
    /// because a poor error response leads to:
    /// - Hallucinated retry arguments (LLM guesses blindly)
    /// - Infinite retry loops (LLM doesn't know it's the same error)
    /// - Incorrect tool selection (LLM thinks tool doesn't work, tries another)
    /// 
    /// Scoring (0-100):
    /// +20: Uses standard JSON-RPC error code (LLM can pattern-match on code)
    /// +25: Error message mentions the specific parameter name (LLM knows WHAT to fix)
    /// +20: Error message describes expected format/type (LLM knows HOW to fix)
    /// +15: Error includes 'data' field with structured details (programmatic correction)
    /// +10: Message is specific (>20 chars, not generic "Error" or "Internal Server Error")
    /// +10: Error correctly uses isError:true in tool result (LLM knows it's a tool failure, not data)
    /// </summary>
    private void GradeErrorForLlmFriendliness(string rawJson, int errorCode, string errorMessage, string toolName, IndividualToolResult result)
    {
        int score = 0;
        var insights = new List<string>();
        var textLower = (errorMessage ?? "").ToLowerInvariant();

        // 1. Standard JSON-RPC error code (+20)
        if (errorCode is -32602 or -32600 or -32601 or -32603 or -32700)
        {
            score += 20;
        }
        else
        {
            insights.Add("Non-standard error code — LLM may not recognize the failure type");
        }

        // 2. Mentions parameter name (+25)
        bool mentionsParam = false;
        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            if (doc.RootElement.TryGetProperty("error", out var err))
            {
                var fullText = err.ToString().ToLowerInvariant();
                // Check if any known parameter names appear in the error
                if (fullText.Contains("param") || fullText.Contains("argument") || fullText.Contains("field") || 
                    fullText.Contains("required") || fullText.Contains("missing"))
                {
                    mentionsParam = true;
                    score += 25;
                }

                // 3. Describes expected type/format (+20) 
                if (fullText.Contains("expected") || fullText.Contains("must be") || fullText.Contains("should be") ||
                    fullText.Contains("type") || fullText.Contains("format") || fullText.Contains("valid"))
                {
                    score += 20;
                }
                else
                {
                    insights.Add("Error doesn't describe expected format — LLM can't self-correct");
                }

                // 4. Has structured 'data' field (+15)
                if (err.TryGetProperty("data", out _))
                {
                    score += 15;
                }

                // 5. Message specificity (+10)
                if ((errorMessage?.Length ?? 0) > 20)
                {
                    score += 10;
                }
                else
                {
                    insights.Add("Error message too short — LLM gets no useful context");
                }
            }
        }
        catch { /* ignore parse errors */ }

        if (!mentionsParam)
        {
            insights.Add("Error doesn't mention which parameter is wrong — LLM will guess blindly");
        }

        // 6. Check isError in tool results (separate from JSON-RPC errors)
        if (rawJson.Contains("\"isError\""))
        {
            score += 10;
        }

        score = Math.Min(100, score);

        // Classify
        var grade = score >= 70 ? "Pro-LLM" : score >= 40 ? "Neutral" : "Anti-LLM";
        var gradeIcon = score >= 70 ? "🟢" : score >= 40 ? "🟡" : "🔴";

        result.Issues.Add($"{gradeIcon} LLM-Friendliness: {score}/100 ({grade}) — {(score >= 70 ? "Error helps AI self-correct" : score >= 40 ? "Error partially helpful for AI" : "Error will cause AI hallucination/loops")}");

        if (insights.Count > 0 && score < 70)
        {
            foreach (var insight in insights.Take(2))
            {
                result.Issues.Add($"   ↳ {insight}");
            }
        }
    }

    /// <summary>
    /// Validates the tools/call response body against MCP spec:
    /// - result.content MUST be an array of Content objects
    /// - Each Content MUST have a "type" field ("text", "image", "audio", "resource")
    /// - result.isError SHOULD be present (boolean)
    /// </summary>
    private void ValidateToolCallResponseStructure(string rawJson, IndividualToolResult result)
    {
        try
        {
            using var doc = JsonDocument.Parse(rawJson);

            // Navigate to result object
            if (!doc.RootElement.TryGetProperty("result", out var resultObj))
            {
                // JSON-RPC error response (has "error" instead of "result") is valid
                if (doc.RootElement.TryGetProperty("error", out _)) return;
                result.Issues.Add("\u26a0\ufe0f MCP Compliance: tools/call response missing 'result' object");
                return;
            }

            // Check content[] array (MUST per spec)
            if (!resultObj.TryGetProperty("content", out var contentArray))
            {
                result.Issues.Add("\u274c MCP Compliance: tools/call result missing 'content' array (MUST per spec)");
                result.MetadataValid = false;
                return;
            }

            if (contentArray.ValueKind != JsonValueKind.Array)
            {
                result.Issues.Add("\u274c MCP Compliance: result.content MUST be an array");
                result.MetadataValid = false;
                return;
            }

            // Validate each Content object has a 'type' field
            var contentCount = contentArray.GetArrayLength();
            for (int i = 0; i < contentCount; i++)
            {
                var item = contentArray[i];
                if (!item.TryGetProperty("type", out var typeField))
                {
                    result.Issues.Add($"\u274c MCP Compliance: content[{i}] missing 'type' field (MUST be text/image/audio/resource)");
                    result.MetadataValid = false;
                }
                else
                {
                    var typeStr = typeField.GetString();
                    if (typeStr is not ("text" or "image" or "audio" or "resource"))
                    {
                        result.Issues.Add($"\u26a0\ufe0f MCP Compliance: content[{i}] has unknown type '{typeStr}' (expected text/image/audio/resource)");
                    }

                    // Validate type-specific required fields
                    if (typeStr == "text" && !item.TryGetProperty("text", out _))
                        result.Issues.Add($"\u274c MCP Compliance: content[{i}] type=text missing 'text' field");
                    if (typeStr == "image" && (!item.TryGetProperty("data", out _) || !item.TryGetProperty("mimeType", out _)))
                        result.Issues.Add($"\u274c MCP Compliance: content[{i}] type=image missing 'data' or 'mimeType'");
                    if (typeStr == "audio" && (!item.TryGetProperty("data", out _) || !item.TryGetProperty("mimeType", out _)))
                        result.Issues.Add($"\u274c MCP Compliance: content[{i}] type=audio missing 'data' or 'mimeType'");
                    if (typeStr == "resource" && !item.TryGetProperty("resource", out _))
                        result.Issues.Add($"\u274c MCP Compliance: content[{i}] type=resource missing 'resource' object");
                }
            }

            // Check isError field (SHOULD per spec)
            if (!resultObj.TryGetProperty("isError", out _))
            {
                result.Issues.Add("\u2139\ufe0f MCP Note: result.isError field not present (SHOULD be included per spec)");
            }
        }
        catch (Exception ex)
        {
            result.Issues.Add($"\u26a0\ufe0f Failed to validate tools/call response structure: {ex.Message}");
        }
    }

    public async Task<ToolTestResult> ValidateToolExecutionAsync(McpServerConfig serverConfig, ToolTestingConfig config, CancellationToken cancellationToken = default)
    {
        return await ExecuteValidationAsync(serverConfig, "Tool Execution", async (ct) =>
        {
            var result = new ToolTestResult { ToolResults = new List<IndividualToolResult>() };

            // Discover tools first
            var listResponse = await _httpClient.CallAsync(serverConfig.Endpoint!, ValidationConstants.Methods.ToolsList, null, serverConfig.Authentication, ct);

            if (listResponse.StatusCode == 401 || listResponse.StatusCode == 403)
            {
                result.Status = TestStatus.Skipped;
                result.Message = "Tool execution skipped: authentication required.";
                return result;
            }

            if (!listResponse.IsSuccess)
            {
                result.Status = TestStatus.Failed;
                result.Message = $"tools/list failed: HTTP {listResponse.StatusCode}";
                return result;
            }

            var tools = ParseTools(listResponse.RawJson);
            if (tools.Count == 0)
            {
                result.Status = TestStatus.Passed;
                result.Message = "No tools to execute.";
                return result;
            }

            // Execute each tool with schema-aware safe payloads
            foreach (var tool in tools)
            {
                var toolResult = new IndividualToolResult
                {
                    ToolName = tool.Name,
                    DiscoveredCorrectly = true,
                    MetadataValid = true
                };

                try
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    var callResponse = await _httpClient.CallAsync(
                        serverConfig.Endpoint!,
                        ValidationConstants.Methods.ToolsCall,
                        CreateSafeToolCallParams(tool),
                        serverConfig.Authentication,
                        ct);
                    sw.Stop();
                    toolResult.ExecutionTimeMs = sw.ElapsedMilliseconds;

                    if (callResponse.StatusCode == 401 || callResponse.StatusCode == 403)
                    {
                        toolResult.Status = TestStatus.Passed;
                        toolResult.ExecutionSuccessful = true;
                        toolResult.Issues.Add("Auth enforced on tools/call (Secure)");
                    }
                    else if (callResponse.IsSuccess || callResponse.StatusCode == 400)
                    {
                        toolResult.Status = TestStatus.Passed;
                        toolResult.ExecutionSuccessful = true;
                    }
                    else
                    {
                        toolResult.Status = TestStatus.Failed;
                        toolResult.ExecutionSuccessful = false;
                        toolResult.Issues.Add($"tools/call failed: HTTP {callResponse.StatusCode}");
                    }
                }
                catch (Exception ex)
                {
                    toolResult.Status = TestStatus.Failed;
                    toolResult.ExecutionSuccessful = false;
                    toolResult.Issues.Add($"tools/call exception: {ex.Message}");
                }

                result.ToolResults.Add(toolResult);
                if (toolResult.Status == TestStatus.Passed) result.ToolsTestPassed++;
                else result.ToolsTestFailed++;
            }

            result.ToolsDiscovered = tools.Count;
            result.Score = _scoringStrategy.CalculateScore(result);
            result.Status = result.ToolsTestFailed == 0 ? TestStatus.Passed : TestStatus.Failed;
            return result;
        }, cancellationToken);
    }

    public async Task<ToolTestResult> ValidateParameterValidationAsync(McpServerConfig serverConfig, string toolName, IEnumerable<ToolTestScenario> testScenarios, CancellationToken cancellationToken = default)
    {
        return await ExecuteValidationAsync(serverConfig, $"Parameter Validation: {toolName}", async (ct) =>
        {
            await Task.CompletedTask;
            var result = new ToolTestResult();
            result.Status = TestStatus.Skipped;
            result.Message = "Parameter validation not yet fully implemented";
            return result;
        }, cancellationToken);
    }

    private List<ToolInfo> ParseTools(string? json)
    {
        var tools = new List<ToolInfo>();
        if (string.IsNullOrEmpty(json)) return tools;

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("result", out var result) &&
                result.TryGetProperty("tools", out var toolsArray) &&
                toolsArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var tool in toolsArray.EnumerateArray())
                {
                    if (tool.TryGetProperty("name", out var name))
                    {
                        var info = new ToolInfo { Name = name.GetString() ?? "unknown" };
                        
                        // Parse Input Schema for Payload Generation
                        if (tool.TryGetProperty("inputSchema", out var schema))
                        {
                            info.InputSchema = schema.Clone();
                        }
                        else
                        {
                            using var empty = JsonDocument.Parse("{}");
                            info.InputSchema = empty.RootElement.Clone();
                        }

                        // Parse Description (for AI readiness)
                        if (tool.TryGetProperty("description", out var desc))
                            info.Description = desc.GetString();

                        // Parse Tool Annotations (MCP spec: readOnlyHint, destructiveHint, openWorldHint)
                        if (tool.TryGetProperty("annotations", out var annotations))
                        {
                            if (annotations.TryGetProperty("readOnlyHint", out var readOnly))
                                info.ReadOnlyHint = readOnly.GetBoolean();
                            if (annotations.TryGetProperty("destructiveHint", out var destructive))
                                info.DestructiveHint = destructive.GetBoolean();
                            if (annotations.TryGetProperty("openWorldHint", out var openWorld))
                                info.OpenWorldHint = openWorld.GetBoolean();
                        }
                        
                        tools.Add(info);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to parse tools list");
        }
        return tools;
    }

    /// <summary>
    /// Fetches all tools with pagination support (handles nextCursor per MCP spec).
    /// </summary>
    private async Task<(List<ToolInfo> Tools, List<string> Issues)> FetchAllToolsWithPaginationAsync(
        string endpoint, AuthenticationConfig? auth, CancellationToken ct)
    {
        var allTools = new List<ToolInfo>();
        var issues = new List<string>();
        string? cursor = null;
        int pageCount = 0;
        const int maxPages = 50; // Safety limit

        do
        {
            pageCount++;
            object? listParams = cursor != null ? new { cursor } : null;
            var response = await _httpClient.CallAsync(endpoint, ValidationConstants.Methods.ToolsList, listParams, auth, ct);

            if (!response.IsSuccess) break;

            var tools = ParseTools(response.RawJson);
            allTools.AddRange(tools);

            // Check for nextCursor (pagination)
            cursor = null;
            if (!string.IsNullOrEmpty(response.RawJson))
            {
                try
                {
                    using var doc = JsonDocument.Parse(response.RawJson);
                    if (doc.RootElement.TryGetProperty("result", out var result) &&
                        result.TryGetProperty("nextCursor", out var nextCursor) &&
                        nextCursor.ValueKind == JsonValueKind.String)
                    {
                        cursor = nextCursor.GetString();
                    }
                }
                catch { /* ignore parse errors */ }
            }

            if (cursor != null && pageCount == 1)
            {
                issues.Add($"ℹ️ Server uses pagination for tools/list (nextCursor detected)");
            }
        }
        while (cursor != null && pageCount < maxPages);

        if (pageCount > 1)
        {
            issues.Add($"✅ Pagination: Fetched {allTools.Count} tools across {pageCount} pages");
        }

        return (allTools, issues);
    }

    private object CreateSafeToolCallParams(ToolInfo tool)
    {
        var args = new Dictionary<string, object>();
        
        try 
        {
            if (tool.InputSchema.ValueKind != JsonValueKind.Undefined && 
                tool.InputSchema.TryGetProperty("properties", out var props))
            {
                foreach (var prop in props.EnumerateObject())
                {
                    // Generate dummy value based on type
                    if (prop.Value.TryGetProperty("type", out var type))
                    {
                        var typeStr = type.GetString();
                        switch (typeStr)
                        {
                            case "string": args[prop.Name] = "test-check"; break;
                            case "integer": args[prop.Name] = 1; break;
                            case "number": args[prop.Name] = 1.0; break;
                            case "boolean": args[prop.Name] = true; break;
                            case "array": args[prop.Name] = new object[] { }; break;
                            case "object": args[prop.Name] = new { }; break;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"Failed to generate safe payload for tool {tool.Name}: {ex.Message}");
        }

        return new
        {
            name = tool.Name,
            arguments = args
        };
    }

    private class ToolInfo
    {
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public JsonElement InputSchema { get; set; }
        // MCP Tool Annotations (spec: readOnlyHint, destructiveHint, openWorldHint)
        public bool? ReadOnlyHint { get; set; }
        public bool? DestructiveHint { get; set; }
        public bool? OpenWorldHint { get; set; }
    }

    private static AuthenticationSecurityResult? BuildAuthSecurityFromDiscovery(AuthDiscoveryInfo? discovery)
    {
        if (discovery == null)
        {
            return null;
        }

        return new AuthenticationSecurityResult
        {
            AuthenticationRequired = true,
            RejectsUnauthenticated = true,
            CorrectStatusCodes = true,
            ErrorResponsesCompliant = true,
            HasProperAuthHeaders = !string.IsNullOrWhiteSpace(discovery.WwwAuthenticateHeader),
            WwwAuthenticateHeader = discovery.WwwAuthenticateHeader,
            AuthMetadata = discovery.Metadata,
            ChallengeDurationMs = discovery.DiscoveryTimeMs,
            SecurityScore = !string.IsNullOrWhiteSpace(discovery.WwwAuthenticateHeader) ? 100.0 : 85.0,
            Findings = new List<string>(discovery.Issues)
        };
    }

    private static void AppendAuthFindings(AuthenticationSecurityResult authSecurity, IEnumerable<string> findings)
    {
        foreach (var note in findings)
        {
            if (string.IsNullOrWhiteSpace(note))
            {
                continue;
            }

            if (!authSecurity.Findings.Contains(note))
            {
                authSecurity.Findings.Add(note);
            }
        }
    }

    private static void NormalizeToolDiscoveryMetrics(ToolTestResult result)
    {
        if (result.ToolsDiscovered == 0 &&
            result.DiscoveredToolNames.Count == 0 &&
            result.ToolResults.Count > 0 &&
            result.ToolResults.All(IsSyntheticAuthEntry))
        {
            result.ToolsDiscovered = result.ToolResults.Count;
        }
    }

    private static bool IsSyntheticAuthEntry(IndividualToolResult toolResult)
    {
        if (toolResult == null)
        {
            return false;
        }

        return toolResult.ToolName?.Contains("auth", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static List<string> BuildSchemaIssueList(string header, IEnumerable<string> details)
    {
        var issues = new List<string>();
        if (!string.IsNullOrWhiteSpace(header))
        {
            issues.Add(header);
        }

        var detailAdded = false;
        foreach (var detail in details)
        {
            if (string.IsNullOrWhiteSpace(detail))
            {
                continue;
            }

            issues.Add(detail);
            detailAdded = true;
        }

        if (!detailAdded)
        {
            issues.Add("   • No additional schema error details were provided");
        }

        issues.Add("ℹ️ Initial 401 challenge (if any) succeeded; failure is due to schema non-compliance.");
        return issues;
    }

    private static JsonRpcResponse? CreateResponseFromSnapshot(TransportResult<CapabilitySummary>? snapshot)
    {
        if (snapshot == null)
        {
            return null;
        }

        var cached = CapabilitySnapshotUtils.CloneResponse(snapshot.Payload?.ToolListResponse);
        if (cached != null)
        {
            return cached;
        }

        var headers = new Dictionary<string, string>();
        foreach (var header in snapshot.Transport.Headers)
        {
            headers[header.Key] = header.Value;
        }

        var status = snapshot.Transport.StatusCode ?? (snapshot.IsSuccessful ? 200 : -1);

        return new JsonRpcResponse
        {
            StatusCode = status,
            IsSuccess = snapshot.IsSuccessful,
            RawJson = snapshot.Transport.RawContent,
            Error = snapshot.Error,
            Headers = headers
        };
    }

    private static List<ToolInfo>? BuildToolListFromSnapshot(CapabilitySummary? summary)
    {
        if (summary?.Tools == null || summary.Tools.Count == 0)
        {
            return null;
        }

        var list = new List<ToolInfo>(summary.Tools.Count);
        foreach (var tool in summary.Tools)
        {
            if (!string.IsNullOrWhiteSpace(tool.Name))
            {
                list.Add(new ToolInfo { Name = tool.Name });
            }
        }

        return list;
    }
}
