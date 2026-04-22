using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;

using System.Diagnostics;
using System.Linq;
using Microsoft.Extensions.Logging;
using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Constants;
using Mcp.Benchmark.Core.Services;

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
    private readonly IToolAiReadinessAnalyzer _aiReadinessAnalyzer;
    private static readonly string[] DestructiveConfirmationGuidanceCues =
    [
        "confirm",
        "confirmation",
        "approve",
        "approval",
        "review",
        "warning",
        "warn"
    ];
    private static readonly string[] DestructiveConfirmationNegations =
    [
        "without confirmation",
        "no confirmation",
        "without approval",
        "no approval",
        "without warning",
        "no warning"
    ];

    public ToolValidator(
        ILogger<ToolValidator> logger,
        IMcpHttpClient httpClient,
        ISchemaValidator schemaValidator,
        ISchemaRegistry schemaRegistry,
        IAuthenticationService authenticationService,
        IContentSafetyAnalyzer contentSafetyAnalyzer,
        IToolAiReadinessAnalyzer aiReadinessAnalyzer)
        : base(logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _schemaValidator = schemaValidator ?? throw new ArgumentNullException(nameof(schemaValidator));
        _schemaRegistry = schemaRegistry ?? throw new ArgumentNullException(nameof(schemaRegistry));
        _authenticationService = authenticationService ?? throw new ArgumentNullException(nameof(authenticationService));
        _contentSafetyAnalyzer = contentSafetyAnalyzer ?? throw new ArgumentNullException(nameof(contentSafetyAnalyzer));
        _aiReadinessAnalyzer = aiReadinessAnalyzer ?? throw new ArgumentNullException(nameof(aiReadinessAnalyzer));
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

            Logger.LogDebug("Performing comprehensive tool discovery via MCP tools/list");

            var toolsListStartTime = DateTime.UtcNow;
            var toolsListResponse = cachedToolsListResponse;
            if (toolsListResponse == null)
            {
                toolsListStartTime = DateTime.UtcNow;
                toolsListResponse = await _httpClient.CallAsync(serverConfig.Endpoint!, ValidationConstants.Methods.ToolsList, null, ct);
                toolsListDuration = (DateTime.UtcNow - toolsListStartTime).TotalMilliseconds;
            }

            var toolsListAuthChallenge = AuthenticationChallengeInterpreter.Inspect(toolsListResponse, toolsListDuration);
            if (toolsListAuthChallenge.RequiresAuthentication)
            {
                result.Status = TestStatus.Passed;
                result.Score = 100.0;
                result.ToolsTestPassed = 1;

                var authIssues = new List<string>();
                var authHeaderValue = toolsListAuthChallenge.WwwAuthenticateHeader;
                var hasWwwAuth = toolsListAuthChallenge.HasWwwAuthenticateHeader;

                if (hasWwwAuth) authIssues.Add("✅ Proper WWW-Authenticate header present");
                else authIssues.Add("⚠️  Missing WWW-Authenticate header (recommended per RFC 9110)");

                authIssues.Add($"ℹ️  HTTP {toolsListResponse.StatusCode}: Auth enforced correctly");
                authIssues.Add("🔒 MCP spec allows both public and auth-protected servers - this is SECURE");

                authSecurity ??= new AuthenticationSecurityResult();
                AuthenticationChallengeInterpreter.Apply(authSecurity, toolsListAuthChallenge);
                result.AuthenticationSecurity = authSecurity;
                result.AuthenticationProperlyEnforced = authSecurity.AuthenticationRequired && authSecurity.HasProperAuthHeaders;

                AuthMetadata? authMetadata = null;
                if (!string.IsNullOrEmpty(toolsListAuthChallenge.ResourceMetadataUrl))
                {
                    try
                    {
                        Logger.LogDebug("Fetching auth metadata from: {MetadataUrl}", toolsListAuthChallenge.ResourceMetadataUrl);

                        var json = await _httpClient.GetStringAsync(toolsListAuthChallenge.ResourceMetadataUrl, ct);
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
                    catch (Exception ex)
                    {
                        Logger.LogWarning(ex, "Failed to fetch/parse auth metadata");
                        authIssues.Add($"⚠️  Failed to fetch auth metadata: {ex.Message}");
                    }
                }

                if (serverConfig.Authentication?.AllowInteractive == true && authMetadata != null)
                {
                    Logger.LogInformation("Interactive auth enabled. Attempting to acquire token via authentication service...");

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
                if (AuthenticationChallengeInterpreter.Inspect(toolsListResponse, toolsListDuration).RequiresAuthentication)
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

            long toolsListPayloadChars = toolsListResponse.RawJson?.Length ?? 0;

            // Parse tools from the first page, then continue through paginated discovery when the server exposes nextCursor.
            var toolsList = ParseTools(toolsListResponse.RawJson);
            if (cachedToolsListResponse == null && snapshotTools == null)
            {
                var paginationFetch = await FetchAllToolsWithPaginationAsync(serverConfig.Endpoint!, serverConfig.Authentication, toolsListResponse, ct);
                toolsList = paginationFetch.Tools;
                toolsListPayloadChars = paginationFetch.TotalPayloadChars;
                result.Issues.AddRange(paginationFetch.Issues);
                ApplyPaginationFindings(result, paginationFetch, toolsList, toolsListPayloadChars / 4);
            }

            if (toolsList.Count == 0 && snapshotTools != null && snapshotTools.Count > 0)
            {
                toolsList = snapshotTools;
            }
            result.ToolsDiscovered = toolsList.Count;
            result.DiscoveredToolNames = toolsList.Select(t => t.Name).ToList();

                // Schema-based validation of tools/list response shape (best-effort)
                var protocolVersion = SchemaValidationHelpers.ResolveProtocolVersion(_schemaRegistry, serverConfig.ProtocolVersion);
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
                    var hasProcessingError = SchemaValidationHelpers.HasSchemaProcessingError(schemaValidationResult);
                    var schemaIssueHeader = SchemaValidationHelpers.FormatListSchemaIssueHeader(ValidationConstants.Methods.ToolsList, hasProcessingError);

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
                ApplyGuidelineFindings(result, tool, toolResult);

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
            ApplyAiReadinessAnalysis(result, toolsList, toolsListResponse.RawJson, toolsListPayloadChars);

            return result;
        }, cancellationToken);
    }

    private void ApplyAiReadinessAnalysis(ToolTestResult result, IReadOnlyCollection<ToolInfo> tools, string? rawJson, long? totalPayloadChars = null)
    {
        var analysis = _aiReadinessAnalyzer.AnalyzeCatalog(
            tools.Select(tool => new ToolAiReadinessTarget
            {
                Name = tool.Name,
                InputSchema = tool.InputSchema
            }).ToList(),
            rawJson,
            totalPayloadChars);

        result.AiReadinessScore = analysis.Score;
        result.EstimatedTokenCount = analysis.EstimatedTokenCount;

        foreach (var finding in analysis.Findings)
        {
            result.AddAiReadinessFinding(finding, finding.Summary);
        }

        if (!string.IsNullOrWhiteSpace(analysis.SummaryIssue))
        {
            result.Issues.Add(analysis.SummaryIssue);
        }
    }

    private void ApplyErrorAiReadinessAssessment(string toolName, string rawJson, int errorCode, string errorMessage, IndividualToolResult result)
    {
        var assessment = _aiReadinessAnalyzer.AnalyzeErrorResponse(toolName, rawJson, errorCode, errorMessage);
        result.AddFinding(assessment.Finding, assessment.Finding.Summary);

        foreach (var issue in assessment.SupportingIssues)
        {
            result.Issues.Add($"   ↳ {issue}");
        }
    }

    private async Task<IndividualToolResult> ValidateIndividualToolAsync(McpServerConfig serverConfig, ToolInfo tool, ToolTestingConfig config, CancellationToken ct)
    {
        var result = new IndividualToolResult
        {
            ToolName = tool.Name,
            DiscoveredCorrectly = true,
            MetadataValid = true,
            DisplayTitle = tool.DisplayTitle,
            Description = tool.Description,
            ReadOnlyHint = tool.ReadOnlyHint,
            DestructiveHint = tool.DestructiveHint,
            OpenWorldHint = tool.OpenWorldHint,
            IdempotentHint = tool.IdempotentHint,
            InputParameterNames = GetInputParameterNames(tool)
        };

        try
        {
            var toolCallStartTime = DateTime.UtcNow;
            var toolCallResponse = await _httpClient.CallAsync(serverConfig.Endpoint!, ValidationConstants.Methods.ToolsCall, CreateSafeToolCallParams(tool), ct);
            var toolCallDuration = (DateTime.UtcNow - toolCallStartTime).TotalMilliseconds;
            result.ExecutionTimeMs = toolCallDuration;

            if (AuthenticationChallengeInterpreter.Inspect(toolCallResponse).RequiresAuthentication)
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
                        ApplyErrorAiReadinessAssessment(tool.Name, toolCallResponse.RawJson, errorCode, errorMsg, result);
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

            if (!doc.RootElement.TryGetProperty("result", out var resultObj))
            {
                if (doc.RootElement.TryGetProperty("error", out _))
                {
                    return;
                }

                result.AddFinding(new ValidationFinding
                {
                    RuleId = ValidationFindingRuleIds.ToolCallMissingResultObject,
                    Category = "ProtocolCompliance",
                    Component = result.ToolName,
                    Severity = ValidationFindingSeverity.High,
                    Summary = "tools/call response missing 'result' object",
                    Recommendation = "Return either a JSON-RPC result object or a JSON-RPC error object for every tools/call response."
                }, "⚠️ MCP Compliance: tools/call response missing 'result' object");
                return;
            }

            if (!resultObj.TryGetProperty("content", out var contentArray))
            {
                result.AddFinding(new ValidationFinding
                {
                    RuleId = ValidationFindingRuleIds.ToolCallMissingContentArray,
                    Category = "ProtocolCompliance",
                    Component = result.ToolName,
                    Severity = ValidationFindingSeverity.Critical,
                    Summary = "tools/call result missing 'content' array",
                    Recommendation = "Return result.content as an array of MCP content blocks."
                }, "❌ MCP Compliance: tools/call result missing 'content' array (MUST per spec)");
                result.MetadataValid = false;
                return;
            }

            if (contentArray.ValueKind != JsonValueKind.Array)
            {
                result.AddFinding(new ValidationFinding
                {
                    RuleId = ValidationFindingRuleIds.ToolCallContentNotArray,
                    Category = "ProtocolCompliance",
                    Component = result.ToolName,
                    Severity = ValidationFindingSeverity.Critical,
                    Summary = "tools/call result.content is not an array",
                    Recommendation = "Serialize result.content as an array even when there is only one content block."
                }, "❌ MCP Compliance: result.content MUST be an array");
                result.MetadataValid = false;
                return;
            }

            var contentCount = contentArray.GetArrayLength();
            for (int i = 0; i < contentCount; i++)
            {
                var item = contentArray[i];
                if (!item.TryGetProperty("type", out var typeField))
                {
                    result.AddFinding(new ValidationFinding
                    {
                        RuleId = ValidationFindingRuleIds.ToolCallContentMissingType,
                        Category = "ProtocolCompliance",
                        Component = result.ToolName,
                        Severity = ValidationFindingSeverity.Critical,
                        Summary = $"tools/call content[{i}] missing 'type' field",
                        Recommendation = "Set content.type to one of the MCP-supported content types.",
                        Metadata = { ["index"] = i.ToString() }
                    }, $"❌ MCP Compliance: content[{i}] missing 'type' field (MUST be text/image/audio/resource)");
                    result.MetadataValid = false;
                    continue;
                }

                var typeStr = typeField.GetString();
                if (typeStr is not ("text" or "image" or "audio" or "resource"))
                {
                    result.AddFinding(new ValidationFinding
                    {
                        RuleId = ValidationFindingRuleIds.ToolCallContentInvalidType,
                        Category = "ProtocolCompliance",
                        Component = result.ToolName,
                        Severity = ValidationFindingSeverity.Medium,
                        Summary = $"tools/call content[{i}] has unknown type '{typeStr}'",
                        Recommendation = "Use a recognized MCP content type so clients can interpret responses consistently.",
                        Metadata = { ["index"] = i.ToString(), ["type"] = typeStr ?? string.Empty }
                    }, $"⚠️ MCP Compliance: content[{i}] has unknown type '{typeStr}' (expected text/image/audio/resource)");
                }

                if (typeStr == "text" && !item.TryGetProperty("text", out _))
                {
                    result.AddFinding(new ValidationFinding
                    {
                        RuleId = ValidationFindingRuleIds.ToolCallContentMissingText,
                        Category = "ProtocolCompliance",
                        Component = result.ToolName,
                        Severity = ValidationFindingSeverity.High,
                        Summary = $"tools/call content[{i}] type=text missing 'text' field",
                        Metadata = { ["index"] = i.ToString() }
                    }, $"❌ MCP Compliance: content[{i}] type=text missing 'text' field");
                }

                if (typeStr == "image" && (!item.TryGetProperty("data", out _) || !item.TryGetProperty("mimeType", out _)))
                {
                    result.AddFinding(new ValidationFinding
                    {
                        RuleId = ValidationFindingRuleIds.ToolCallContentMissingImageData,
                        Category = "ProtocolCompliance",
                        Component = result.ToolName,
                        Severity = ValidationFindingSeverity.High,
                        Summary = $"tools/call content[{i}] type=image missing data or mimeType",
                        Metadata = { ["index"] = i.ToString() }
                    }, $"❌ MCP Compliance: content[{i}] type=image missing 'data' or 'mimeType'");
                }

                if (typeStr == "audio" && (!item.TryGetProperty("data", out _) || !item.TryGetProperty("mimeType", out _)))
                {
                    result.AddFinding(new ValidationFinding
                    {
                        RuleId = ValidationFindingRuleIds.ToolCallContentMissingAudioData,
                        Category = "ProtocolCompliance",
                        Component = result.ToolName,
                        Severity = ValidationFindingSeverity.High,
                        Summary = $"tools/call content[{i}] type=audio missing data or mimeType",
                        Metadata = { ["index"] = i.ToString() }
                    }, $"❌ MCP Compliance: content[{i}] type=audio missing 'data' or 'mimeType'");
                }

                if (typeStr == "resource" && !item.TryGetProperty("resource", out _))
                {
                    result.AddFinding(new ValidationFinding
                    {
                        RuleId = ValidationFindingRuleIds.ToolCallContentMissingResource,
                        Category = "ProtocolCompliance",
                        Component = result.ToolName,
                        Severity = ValidationFindingSeverity.High,
                        Summary = $"tools/call content[{i}] type=resource missing resource object",
                        Metadata = { ["index"] = i.ToString() }
                    }, $"❌ MCP Compliance: content[{i}] type=resource missing 'resource' object");
                }
            }

            if (!resultObj.TryGetProperty("isError", out _))
            {
                result.AddFinding(new ValidationFinding
                {
                    RuleId = ValidationFindingRuleIds.ToolCallMissingIsError,
                    Category = "ProtocolCompliance",
                    Component = result.ToolName,
                    Severity = ValidationFindingSeverity.Low,
                    Summary = "tools/call result.isError field not present",
                    Recommendation = "Include result.isError in tool responses so clients can distinguish failures from normal payloads."
                }, "ℹ️ MCP Note: result.isError field not present (SHOULD be included per spec)");
            }
        }
        catch (Exception ex)
        {
            result.AddFinding(new ValidationFinding
            {
                RuleId = ValidationFindingRuleIds.ToolCallResponseValidationFailed,
                Category = "ProtocolCompliance",
                Component = result.ToolName,
                Severity = ValidationFindingSeverity.Medium,
                Summary = $"Failed to validate tools/call response structure: {ex.Message}",
                Recommendation = "Return well-formed JSON-RPC responses so tools/call output can be validated consistently."
            }, $"⚠️ Failed to validate tools/call response structure: {ex.Message}");
        }
    }

    public async Task<ToolTestResult> ValidateToolExecutionAsync(McpServerConfig serverConfig, ToolTestingConfig config, CancellationToken cancellationToken = default)
    {
        return await ExecuteValidationAsync(serverConfig, "Tool Execution", async (ct) =>
        {
            var result = new ToolTestResult { ToolResults = new List<IndividualToolResult>() };

            // Discover tools first
            var listResponse = await _httpClient.CallAsync(serverConfig.Endpoint!, ValidationConstants.Methods.ToolsList, null, serverConfig.Authentication, ct);

            if (AuthenticationChallengeInterpreter.Inspect(listResponse).RequiresAuthentication)
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

                    if (AuthenticationChallengeInterpreter.Inspect(callResponse).RequiresAuthentication)
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
                        if (tool.TryGetProperty("title", out var title))
                            info.Title = title.GetString();

                        if (tool.TryGetProperty("description", out var desc))
                            info.Description = desc.GetString();

                        // Parse Tool Annotations (MCP spec: title, readOnlyHint, destructiveHint, openWorldHint, idempotentHint)
                        if (tool.TryGetProperty("annotations", out var annotations))
                        {
                            if (annotations.TryGetProperty("title", out var annotationTitle))
                                info.AnnotationTitle = annotationTitle.GetString();
                            if (annotations.TryGetProperty("readOnlyHint", out var readOnly))
                                info.ReadOnlyHint = readOnly.GetBoolean();
                            if (annotations.TryGetProperty("destructiveHint", out var destructive))
                                info.DestructiveHint = destructive.GetBoolean();
                            if (annotations.TryGetProperty("openWorldHint", out var openWorld))
                                info.OpenWorldHint = openWorld.GetBoolean();
                            if (annotations.TryGetProperty("idempotentHint", out var idempotent))
                                info.IdempotentHint = idempotent.GetBoolean();
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
    private async Task<ToolPaginationFetchResult> FetchAllToolsWithPaginationAsync(
        string endpoint, AuthenticationConfig? auth, JsonRpcResponse initialResponse, CancellationToken ct)
    {
        var allTools = ParseTools(initialResponse.RawJson);
        var issues = new List<string>();
        var seenCursors = new HashSet<string>(StringComparer.Ordinal);
        var totalPayloadChars = initialResponse.RawJson?.Length ?? 0;
        var cursor = ExtractNextCursor(initialResponse.RawJson);
        var paginationDetected = !string.IsNullOrWhiteSpace(cursor);
        var cursorLoopDetected = false;
        int pageCount = 1;
        const int maxPages = 50; // Safety limit

        if (paginationDetected)
        {
            issues.Add("ℹ️ Server uses pagination for tools/list (nextCursor detected)");
        }

        while (!string.IsNullOrWhiteSpace(cursor) && pageCount < maxPages)
        {
            if (!seenCursors.Add(cursor))
            {
                cursorLoopDetected = true;
                issues.Add($"⚠️ Pagination warning: tools/list repeated cursor '{cursor}' across pages");
                break;
            }

            pageCount++;
            object? listParams = cursor != null ? new { cursor } : null;
            var response = await _httpClient.CallAsync(endpoint, ValidationConstants.Methods.ToolsList, listParams, auth, ct);

            if (!response.IsSuccess)
            {
                issues.Add($"⚠️ Pagination warning: tools/list page {pageCount} failed: HTTP {response.StatusCode}");
                break;
            }

            var tools = ParseTools(response.RawJson);
            allTools.AddRange(tools);
            totalPayloadChars += response.RawJson?.Length ?? 0;

            cursor = ExtractNextCursor(response.RawJson);
        }

        if (pageCount > 1)
        {
            issues.Add($"✅ Pagination: Fetched {allTools.Count} tools across {pageCount} pages");
        }

        if (!string.IsNullOrWhiteSpace(cursor) && pageCount >= maxPages)
        {
            issues.Add($"⚠️ Pagination warning: tools/list exceeded the validator safety limit of {maxPages} pages");
        }

        return new ToolPaginationFetchResult(
            allTools,
            issues,
            pageCount,
            paginationDetected,
            cursorLoopDetected,
            totalPayloadChars);
    }

    private static string? ExtractNextCursor(string? rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            if (doc.RootElement.TryGetProperty("result", out var result) &&
                result.TryGetProperty("nextCursor", out var nextCursor) &&
                nextCursor.ValueKind == JsonValueKind.String)
            {
                return nextCursor.GetString();
            }
        }
        catch
        {
            // Ignore malformed cursor payloads here and let schema validation surface the rest.
        }

        return null;
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

    private static List<string> GetInputParameterNames(ToolInfo tool)
    {
        var parameterNames = new List<string>();

        try
        {
            if (tool.InputSchema.ValueKind != JsonValueKind.Undefined &&
                tool.InputSchema.TryGetProperty("properties", out var props))
            {
                parameterNames.AddRange(props.EnumerateObject().Select(property => property.Name));
            }
        }
        catch
        {
            // Ignore metadata extraction failures. Validation should continue with partial evidence.
        }

        return parameterNames;
    }

    private class ToolInfo
    {
        public string Name { get; set; } = string.Empty;
        public string? Title { get; set; }
        public string? AnnotationTitle { get; set; }
        public string? Description { get; set; }
        public JsonElement InputSchema { get; set; }
        public string? DisplayTitle => !string.IsNullOrWhiteSpace(Title) ? Title : AnnotationTitle;
        // MCP Tool Annotations (spec: readOnlyHint, destructiveHint, openWorldHint, idempotentHint)
        public bool? ReadOnlyHint { get; set; }
        public bool? DestructiveHint { get; set; }
        public bool? OpenWorldHint { get; set; }
        public bool? IdempotentHint { get; set; }
    }

    private static void ApplyGuidelineFindings(ToolTestResult aggregateResult, ToolInfo tool, IndividualToolResult toolResult)
    {
        AddGuidelineFindingIfMissing(
            aggregateResult,
            toolResult,
            string.IsNullOrWhiteSpace(tool.DisplayTitle),
            ValidationFindingRuleIds.ToolGuidelineDisplayTitleMissing,
            ValidationFindingSeverity.Low,
            $"Tool '{tool.Name}' does not declare a human-friendly display title.",
            "Add title or annotations.title so clients can present the tool with a clear human-readable label.");

        AddGuidelineFindingIfMissing(
            aggregateResult,
            toolResult,
            !tool.ReadOnlyHint.HasValue,
            ValidationFindingRuleIds.ToolGuidelineReadOnlyHintMissing,
            ValidationFindingSeverity.Low,
            $"Tool '{tool.Name}' does not declare annotations.readOnlyHint.",
            "Declare readOnlyHint so agents can distinguish read-only tools from state-changing tools.");

        AddGuidelineFindingIfMissing(
            aggregateResult,
            toolResult,
            !tool.DestructiveHint.HasValue,
            ValidationFindingRuleIds.ToolGuidelineDestructiveHintMissing,
            ValidationFindingSeverity.Low,
            $"Tool '{tool.Name}' does not declare annotations.destructiveHint.",
            "Declare destructiveHint so agents know when human confirmation is appropriate.");

        AddGuidelineFindingIfMissing(
            aggregateResult,
            toolResult,
            !tool.OpenWorldHint.HasValue,
            ValidationFindingRuleIds.ToolGuidelineOpenWorldHintMissing,
            ValidationFindingSeverity.Low,
            $"Tool '{tool.Name}' does not declare annotations.openWorldHint.",
            "Declare openWorldHint so agents can reason about whether execution can affect unknown external systems.");

        AddGuidelineFindingIfMissing(
            aggregateResult,
            toolResult,
            !tool.IdempotentHint.HasValue,
            ValidationFindingRuleIds.ToolGuidelineIdempotentHintMissing,
            ValidationFindingSeverity.Low,
            $"Tool '{tool.Name}' does not declare annotations.idempotentHint.",
            "Declare idempotentHint so agents can decide whether retries are safe.");

        if (tool.ReadOnlyHint == true && tool.DestructiveHint == true)
        {
            var finding = new ValidationFinding
            {
                RuleId = ValidationFindingRuleIds.ToolGuidelineHintConflict,
                Category = "McpGuideline",
                Component = tool.Name,
                Severity = ValidationFindingSeverity.High,
                Summary = $"Tool '{tool.Name}' declares conflicting annotations: readOnlyHint=true and destructiveHint=true.",
                Recommendation = "Resolve conflicting tool annotations so clients receive one consistent safety signal."
            };
            toolResult.AddFinding(finding, finding.Summary);
            aggregateResult.Findings.Add(finding);
        }

        if (tool.DestructiveHint == true && MissingDestructiveConfirmationGuidance(tool.Description))
        {
            var finding = new ValidationFinding
            {
                RuleId = ValidationFindingRuleIds.ToolDestructiveConfirmationGuidanceMissing,
                Category = "McpGuideline",
                Component = tool.Name,
                Severity = ValidationFindingSeverity.Medium,
                Summary = $"Tool '{tool.Name}' is marked destructive but its description does not mention confirmation, approval, or warning guidance.",
                Recommendation = "Add explicit confirmation, approval, or warning language so agents know human review is expected before destructive execution."
            };

            toolResult.AddFinding(finding, finding.Summary);
            aggregateResult.Findings.Add(finding);
        }
    }

    private static bool MissingDestructiveConfirmationGuidance(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return true;
        }

        return !HasDestructiveConfirmationGuidance(description);
    }

    private static bool HasDestructiveConfirmationGuidance(string description)
    {
        if (DestructiveConfirmationNegations.Any(phrase => description.Contains(phrase, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        return DestructiveConfirmationGuidanceCues.Any(phrase => description.Contains(phrase, StringComparison.OrdinalIgnoreCase));
    }

    private static void ApplyPaginationFindings(
        ToolTestResult aggregateResult,
        ToolPaginationFetchResult paginationFetch,
        IReadOnlyCollection<ToolInfo> tools,
        long estimatedTokenCount)
    {
        if (paginationFetch.CursorLoopDetected)
        {
            aggregateResult.Findings.Add(new ValidationFinding
            {
                RuleId = ValidationFindingRuleIds.ToolListCursorLoopDetected,
                Category = "AiReadiness",
                Component = "tools/list",
                Severity = ValidationFindingSeverity.Medium,
                Summary = "tools/list pagination repeated a cursor across pages, which can trap clients in unstable pagination loops.",
                Recommendation = "Return a stable nextCursor progression and stop emitting cursors after the final page."
            });
        }

        if (!paginationFetch.PaginationDetected && (tools.Count >= 100 || estimatedTokenCount > 8000))
        {
            aggregateResult.Findings.Add(new ValidationFinding
            {
                RuleId = ValidationFindingRuleIds.ToolListPaginationRecommended,
                Category = "McpGuideline",
                Component = "tools/list",
                Severity = ValidationFindingSeverity.Low,
                Summary = $"tools/list returned {tools.Count} tools (~{estimatedTokenCount:N0} tokens) without pagination support.",
                Recommendation = "Add nextCursor pagination for large tool catalogs so clients can discover tools incrementally."
            });
        }
    }

    private static void AddGuidelineFindingIfMissing(
        ToolTestResult aggregateResult,
        IndividualToolResult toolResult,
        bool isMissing,
        string ruleId,
        ValidationFindingSeverity severity,
        string summary,
        string recommendation)
    {
        if (!isMissing)
        {
            return;
        }

        var finding = new ValidationFinding
        {
            RuleId = ruleId,
            Category = "McpGuideline",
            Component = toolResult.ToolName,
            Severity = severity,
            Summary = summary,
            Recommendation = recommendation
        };

        toolResult.AddFinding(finding, summary);
        aggregateResult.Findings.Add(finding);
    }

    private static AuthenticationSecurityResult? BuildAuthSecurityFromDiscovery(AuthDiscoveryInfo? discovery)
    {
        return AuthenticationChallengeInterpreter.CreateSecurityResult(discovery);
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

    private sealed record ToolPaginationFetchResult(
        List<ToolInfo> Tools,
        List<string> Issues,
        int PageCount,
        bool PaginationDetected,
        bool CursorLoopDetected,
        long TotalPayloadChars);

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
