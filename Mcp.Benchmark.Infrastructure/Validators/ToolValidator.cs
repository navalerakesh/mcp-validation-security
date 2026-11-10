using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;

using System.Diagnostics;
using System.Linq;
using Microsoft.Extensions.Logging;
using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Infrastructure.Services;
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
        return await ExecuteValidationAsync<ToolTestResult>(serverConfig, "Tool Discovery", async (result, ct) =>
        {
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

            if (CapabilitySnapshotUtils.IsCapabilityExplicitlyNotAdvertised(capabilitySnapshot, McpSpecConstants.Capabilities.Tools))
            {
                result.Status = TestStatus.Skipped;
                result.Score = 100.0;
                result.Message = "Server does not advertise the tools capability.";
                result.Issues.Add("Tools capability was not advertised during initialize; tools/list and tools/call probes were skipped.");
                return result;
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
                result.Status = TestStatus.AuthRequired;
                result.Score = 0.0;
                result.ToolsTestPassed = 0;

                var authIssues = new List<string>();
                var authHeaderValue = AuthenticationChallengeInterpreter.SanitizeForEvidence(toolsListAuthChallenge);
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

                        var acquiredAuthentication = new AuthenticationConfig
                        {
                            Type = "bearer",
                            Token = token
                        };

                        // Retry the request
                        toolsListStartTime = DateTime.UtcNow;
                        toolsListResponse = await _httpClient.CallAsync(
                            serverConfig.Endpoint!,
                            ValidationConstants.Methods.ToolsList,
                            null,
                            acquiredAuthentication,
                            ct);
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
                        result.Status = TestStatus.AuthRequired;
                        result.Score = 0.0;
                        result.ToolsTestPassed = 0;
                        result.Issues.Add("Tool validation requires authentication: interactive authentication failed or was not completed.");

                        result.ToolResults.Add(new IndividualToolResult
                        {
                            ToolName = "tools/list (Auth Check)",
                            Status = TestStatus.AuthRequired,
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

                    result.Status = TestStatus.AuthRequired;
                    result.Score = 0.0;
                    result.ToolsTestPassed = 0;

                    result.ToolResults.Add(new IndividualToolResult
                    {
                        ToolName = "tools/list (Auth Check)",
                        Status = TestStatus.AuthRequired,
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
                if (ValidationReliability.ShouldRetryRpcResponse(toolsListResponse))
                {
                    result.Status = TestStatus.Inconclusive;
                    result.Message = $"tools/list probe inconclusive: {ValidationReliability.DescribeRetryableResponse(toolsListResponse)}";
                    result.Issues.Add($"⚠️ Tool discovery probe inconclusive due to transient transport pressure ({ValidationReliability.DescribeRetryableResponse(toolsListResponse)}). Not treated as a contract failure.");
                    return result;
                }

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

            // Treat the cached tools/list payload as the authoritative first page, then
            // continue paginated discovery so capability snapshots do not truncate catalogs.
            result.CatalogEnumeration = new CatalogEnumerationEvidence();
            var paginationFetch = await FetchAllToolsWithPaginationAsync(serverConfig.Endpoint!, serverConfig.Authentication, toolsListResponse,
                (tools, rejections) =>
                {
                    result.ToolsDiscovered = tools.Count;
                    result.DiscoveredToolNames = tools.Select(tool => tool.Name).ToList();
                    result.ToolsTestFailed = checked((int)result.CatalogEnumeration.RejectedItems);
                    result.Issues.AddRange(rejections.Except(result.Issues, StringComparer.Ordinal));
                }, () => RecordMalformedCatalogItem(result), result.CatalogEnumeration, ct);
            var toolsList = paginationFetch.Tools;
            toolsListPayloadChars = paginationFetch.TotalPayloadChars;
            result.Issues.AddRange(paginationFetch.Issues.Except(result.Issues, StringComparer.Ordinal));
            result.ToolsTestFailed = paginationFetch.RejectedItems;
            ApplyPaginationFindings(result, paginationFetch, toolsList, toolsListPayloadChars / 4);

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

            }

            if (toolsList.Count == 0)
            {
                result.Status = result.ToolsTestFailed > 0 ? TestStatus.Failed : TestStatus.Passed;
                if (result.ToolsTestFailed > 0)
                {
                    result.Message = "Tool catalog could not be fully recovered; malformed entries were rejected.";
                    return result;
                }
                if (!paginationFetch.IsComplete)
                {
                    result.Status = result.CatalogEnumeration.Termination == CatalogEnumerationTermination.AuthenticationRequired
                        ? TestStatus.AuthRequired : TestStatus.Inconclusive;
                    result.Message = "Tool catalog enumeration did not complete; an empty catalog has not been established.";
                    return result;
                }
                result.ToolsTestPassed++;
                result.Issues.Add(CapabilitySnapshotUtils.IsCapabilityAdvertised(capabilitySnapshot, McpSpecConstants.Capabilities.Tools)
                    ? "Tools capability was advertised, but tools/list returned an empty catalog; no tool executions were required."
                    : "No tools were returned by tools/list; no tool executions were required.");
                result.ToolResults.Add(new IndividualToolResult
                {
                    ToolName = "tools/list",
                    Status = TestStatus.Passed,
                    DiscoveredCorrectly = true,
                    MetadataValid = true,
                    ExecutionSuccessful = false,
                    ExecutionTimeMs = toolsListDuration,
                    Issues = new List<string> { "No tools discovered" }
                });
                return result;
            }

            // Validate each tool and apply static content safety analysis
            foreach (var tool in toolsList)
            {
                var toolResult = await ValidateIndividualToolAsync(serverConfig, tool, config, ct);
                result.ToolResults.Add(toolResult);
                ct.ThrowIfCancellationRequested();
                ApplyGuidelineFindings(result, tool, toolResult, config.CapabilitySnapshot);
                result.AiSafetyControlEvidence.AddRange(toolResult.AiSafetyControlEvidence);

                // Static, metadata-only content safety analysis
                var safetyContext = ContentSafetyAnalysisContext.FromServerConfig(serverConfig, toolResult.AiSafetyControlEvidence);
                var safetyFindings = _contentSafetyAnalyzer.AnalyzeTool(tool.Name, safetyContext);
                if (safetyFindings.Count > 0)
                {
                    toolResult.ContentSafetyFindings.AddRange(safetyFindings);
                    result.ContentSafetyFindings.AddRange(safetyFindings);
                }

                if (toolResult.Status == TestStatus.Passed) result.ToolsTestPassed++;
                else if (toolResult.Status == TestStatus.Failed) result.ToolsTestFailed++;
            }

            // Calculate Score using Strategy
            result.Score = _scoringStrategy.CalculateScore(result);

            if (capabilitySnapshot?.Payload != null)
            {
                if (capabilitySnapshot.Payload.ToolInvocationAttempted && capabilitySnapshot.Payload.ToolInvocationSucceeded)
                {
                    result.Issues.Add("✅ SDK invocation of the first discovered tool succeeded");
                }
                else if (capabilitySnapshot.Payload.ToolInvocationAttempted && capabilitySnapshot.Payload.ToolListingSucceeded)
                {
                    result.Issues.Add("⚠️ SDK invocation of the first discovered tool failed; ensure the tool accepts empty arguments");
                }
            }

            result.Status = DetermineAggregateToolStatus(result.ToolResults, result.ToolsTestFailed);
            if (!paginationFetch.IsComplete && result.Status == TestStatus.Passed)
            {
                result.Status = result.CatalogEnumeration.Termination == CatalogEnumerationTermination.AuthenticationRequired
                    ? TestStatus.AuthRequired : TestStatus.Inconclusive;
                result.Message = "Tool catalog enumeration did not complete; retained tool results cover only the observed portion.";
            }

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
                InputSchema = tool.HasInputSchema ? tool.InputSchema : default
            }).ToList(),
            rawJson,
            totalPayloadChars);

        var catalogComplete = result.CatalogEnumeration?.IsComplete != false;
        result.AiReadinessScore = catalogComplete ? analysis.Score : -1;
        result.AiReadinessAnalyzedSchemaCount = analysis.AnalyzedSchemaCount;
        result.AiReadinessInspectionIssues = analysis.InspectionIssues;
        result.EstimatedTokenCount = analysis.EstimatedTokenCount;

        foreach (var finding in analysis.Findings)
        {
            result.AddAiReadinessFinding(finding, finding.Summary);
        }

        if (catalogComplete && !string.IsNullOrWhiteSpace(analysis.SummaryIssue))
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
            AnthropicMaxResultSizeChars = tool.AnthropicMaxResultSizeChars,
            AppMetadata = tool.AppMetadata,
            InputParameterNames = GetInputParameterNames(tool)
        };
        ApplyAiSafetyControlEvidence(result, tool);
        ValidateToolMetadata(tool, result);

        if (!config.TestToolExecution && !config.TestParameterValidation)
        {
            result.Status = result.Issues.Any(issue => issue.StartsWith("❌", StringComparison.Ordinal))
                ? TestStatus.Failed
                : TestStatus.Passed;
            result.ExecutionSuccessful = false;
            result.Issues.Add("Tool execution was not attempted under the active execution policy; metadata and schema checks only.");
            return result;
        }

        try
        {
            if (!TryCreateToolCallParams(tool, out var toolCallParams))
            {
                result.Status = result.MetadataValid ? TestStatus.Inconclusive : TestStatus.Failed;
                result.ExecutionSuccessful = false;
                result.Issues.Add("Tool invocation was not attempted: a bounded schema-valid synthetic argument set could not be established.");
                return result;
            }
            var toolCallStartTime = DateTime.UtcNow;
            var toolCallResponse = await _httpClient.CallAsync(serverConfig.Endpoint!, ValidationConstants.Methods.ToolsCall, toolCallParams, ct);
            var toolCallDuration = (DateTime.UtcNow - toolCallStartTime).TotalMilliseconds;
            result.ExecutionTimeMs = toolCallDuration;

            if (AuthenticationChallengeInterpreter.Inspect(toolCallResponse).RequiresAuthentication)
            {
                result.Status = TestStatus.AuthRequired;
                result.ExecutionSuccessful = false;
                result.Issues.Add("Tool call requires authentication; secure challenge observed, execution not validated.");
            }
            else if (toolCallResponse.StatusCode > 0 && ValidationReliability.ShouldRetryRpcResponse(toolCallResponse))
            {
                result.Status = TestStatus.Inconclusive;
                result.ExecutionSuccessful = false;
                result.Issues.Add($"⚠️ Tool call probe inconclusive due to transient transport pressure ({ValidationReliability.DescribeRetryableResponse(toolCallResponse)}). Not treated as a contract failure.");
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
                        ValidateToolCallResponseStructure(toolCallResponse.RawJson, result, tool);
                    }
                }
            }
            else if (toolCallResponse.StatusCode is 400 or 404)
            {
                result.Status = TestStatus.Inconclusive;
                result.ExecutionSuccessful = false;
                result.Issues.Add($"Tool invocation returned HTTP {toolCallResponse.StatusCode}; routing or argument rejection does not establish successful execution.");
            }
            else if (toolCallResponse.StatusCode == 500)
            {
                // 500 = server crashed processing our dummy input. This IS a real failure.
                result.Status = TestStatus.Failed;
                result.ExecutionSuccessful = false;
                result.Issues.Add("❌ Server crashed (500 Internal Server Error) on tool call with test parameters");
            }
            else if (toolCallResponse.StatusCode <= 0)
            {
                result.Status = TestStatus.Inconclusive;
                result.ExecutionSuccessful = false;
                result.Issues.Add("Tool invocation did not produce an HTTP response; execution remains unverified.");
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
            result.Status = ct.IsCancellationRequested ? TestStatus.Cancelled : TestStatus.Inconclusive;
            result.ExecutionSuccessful = false;
            result.Issues.Add("Tool invocation was cancelled or timed out; execution remains unverified.");
        }
        catch (Exception ex)
        {
            result.Status = TestStatus.Failed;
            result.ExecutionSuccessful = false;
            result.Issues.Add($"Tool call exception: {ex.Message}");
        }

        ApplyBlockingToolFindingStatus(result);
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
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("error", out var errorObj) &&
                errorObj.ValueKind == JsonValueKind.Object)
            {
                if (errorObj.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.Number)
                    errorCode = code.GetInt32();
                if (errorObj.TryGetProperty("message", out var msg) && msg.ValueKind == JsonValueKind.String)
                    errorMessage = msg.GetString() ?? "";
                return true;
            }
        }
        catch { /* Not valid JSON-RPC error format */ }
        return false;
    }

    private void ValidateToolMetadata(ToolInfo tool, IndividualToolResult result)
    {
        // The MCP schema requires the `name` property be present (Tool.required = ["inputSchema","name"])
        // and a string. Missing/empty name is therefore a true Spec violation.
        if (string.IsNullOrEmpty(tool.Name))
        {
            AddToolSpecFinding(
                result,
                ValidationFindingRuleIds.ToolNameInvalid,
                ValidationFindingSeverity.High,
                "Tool name is missing or empty.",
                "Return a non-empty 'name' string for every tool in tools/list per the MCP Tool schema (required field).");
        }
        else if (tool.Name != tool.Name.Trim() || ContainsControlCharacter(tool.Name))
        {
            // Per official spec §5.1.1 the format constraints (1-128 chars, A-Z/a-z/0-9/_-./, no
            // spaces, etc.) are SHOULDs not MUSTs. Surface these as Guideline, not Spec.
            AddToolGuidelineFinding(
                result,
                ValidationFindingRuleIds.ToolNameFormatGuideline,
                ValidationFindingSeverity.Medium,
                $"Tool name '{tool.Name}' contains leading/trailing whitespace or control characters.",
                "Per MCP §5.1.1 (SHOULD), use only A-Z, a-z, 0-9, underscore, hyphen, or dot in tool names; avoid whitespace and control characters.");
        }

        ValidateJsonSchemaProperty(
            result,
            tool.HasInputSchema,
            tool.InputSchema,
            "inputSchema",
            ValidationFindingRuleIds.ToolInputSchemaMissing,
            ValidationFindingRuleIds.ToolInputSchemaInvalid,
            ValidationFindingRuleIds.ToolInputSchemaRootTypeInvalid,
            isRequired: true);

        ValidateJsonSchemaProperty(
            result,
            tool.HasOutputSchema,
            tool.OutputSchema,
            "outputSchema",
            null,
            ValidationFindingRuleIds.ToolOutputSchemaInvalid,
            ValidationFindingRuleIds.ToolOutputSchemaRootTypeInvalid,
            isRequired: false);

        ValidateToolIcons(tool, result);
        ValidateToolExecutionMetadata(tool, result);
    }

    private void ValidateJsonSchemaProperty(
        IndividualToolResult result,
        bool isPresent,
        JsonElement schema,
        string propertyName,
        string? missingRuleId,
        string invalidRuleId,
        string invalidRootTypeRuleId,
        bool isRequired)
    {
        if (!isPresent)
        {
            if (isRequired && missingRuleId is not null)
            {
                AddToolSpecFinding(
                    result,
                    missingRuleId,
                    ValidationFindingSeverity.Critical,
                    $"Tool '{result.ToolName}' is missing required {propertyName} metadata.",
                    $"Declare {propertyName} as a JSON Schema object with root type 'object'.");
            }

            return;
        }

        if (schema.ValueKind != JsonValueKind.Object)
        {
            AddToolSpecFinding(
                result,
                invalidRuleId,
                ValidationFindingSeverity.Critical,
                $"Tool '{result.ToolName}' declares {propertyName}, but it is not a JSON object.",
                $"Declare {propertyName} as a JSON Schema object.");
            return;
        }

        if (!TryConvertToJsonNode(schema, out var schemaNode, out var conversionError) || schemaNode is null)
        {
            AddToolSpecFinding(
                result,
                invalidRuleId,
                ValidationFindingSeverity.High,
                $"Tool '{result.ToolName}' declares {propertyName}, but it could not be parsed for JSON Schema validation: {conversionError}",
                $"Return a valid JSON Schema object in {propertyName}.");
            return;
        }

        if (!_schemaValidator.IsValidSchemaDefinition(schemaNode, out var schemaErrors))
        {
            AddToolSpecFinding(
                result,
                invalidRuleId,
                ValidationFindingSeverity.High,
                $"Tool '{result.ToolName}' declares an invalid {propertyName} JSON Schema.",
                $"Fix {propertyName} so it parses as JSON Schema 2020-12.",
                schemaErrors);
        }

        if (!schema.TryGetProperty("type", out var typeProperty) || typeProperty.ValueKind != JsonValueKind.String)
        {
            AddToolSpecFinding(
                result,
                invalidRootTypeRuleId,
                ValidationFindingSeverity.High,
                $"Tool '{result.ToolName}' declares {propertyName} without required root type 'object'.",
                $"Set {propertyName}.type to 'object'.");
            return;
        }

        var rootType = typeProperty.GetString();
        if (!string.Equals(rootType, "object", StringComparison.Ordinal))
        {
            AddToolSpecFinding(
                result,
                invalidRootTypeRuleId,
                ValidationFindingSeverity.Critical,
                $"Tool '{result.ToolName}' declares {propertyName}.type='{rootType}', but MCP requires root type 'object'.",
                $"Set {propertyName}.type to 'object'.");
        }
    }

    private static void ValidateToolIcons(ToolInfo tool, IndividualToolResult result)
    {
        if (!tool.HasIcons)
        {
            return;
        }

        if (tool.Icons.ValueKind != JsonValueKind.Array)
        {
            AddToolSpecFinding(
                result,
                ValidationFindingRuleIds.ToolIconInvalid,
                ValidationFindingSeverity.High,
                $"Tool '{tool.Name}' declares icons, but icons is not an array.",
                "Declare icons as an array of icon objects with src values.");
            return;
        }

        var index = 0;
        foreach (var icon in tool.Icons.EnumerateArray())
        {
            if (icon.ValueKind != JsonValueKind.Object)
            {
                AddToolSpecFinding(
                    result,
                    ValidationFindingRuleIds.ToolIconInvalid,
                    ValidationFindingSeverity.High,
                    $"Tool '{tool.Name}' declares icons[{index}], but it is not an object.",
                    "Declare each icon as an object with a src URI.",
                    metadata: new Dictionary<string, string> { ["index"] = index.ToString() });
                index++;
                continue;
            }

            if (!icon.TryGetProperty("src", out var src) || src.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(src.GetString()))
            {
                AddToolSpecFinding(
                    result,
                    ValidationFindingRuleIds.ToolIconInvalid,
                    ValidationFindingSeverity.High,
                    $"Tool '{tool.Name}' declares icons[{index}] without required src URI.",
                    "Set icons[].src to an absolute http(s) URI or data URI.",
                    metadata: new Dictionary<string, string> { ["index"] = index.ToString() });
            }
            else if (!IsValidIconUri(src.GetString()))
            {
                AddToolSpecFinding(
                    result,
                    ValidationFindingRuleIds.ToolIconInvalid,
                    ValidationFindingSeverity.Medium,
                    $"Tool '{tool.Name}' declares icons[{index}].src that is not an absolute http(s) or data URI.",
                    "Use an absolute http(s) URI or a data URI for icons[].src.",
                    metadata: new Dictionary<string, string> { ["index"] = index.ToString(), ["src"] = src.GetString() ?? string.Empty });
            }

            if (icon.TryGetProperty("theme", out var theme) &&
                (theme.ValueKind != JsonValueKind.String || theme.GetString() is not ("dark" or "light")))
            {
                AddToolSpecFinding(
                    result,
                    ValidationFindingRuleIds.ToolIconInvalid,
                    ValidationFindingSeverity.Medium,
                    $"Tool '{tool.Name}' declares icons[{index}].theme outside the supported values dark/light.",
                    "Set icons[].theme to 'dark' or 'light', or omit it.",
                    metadata: new Dictionary<string, string> { ["index"] = index.ToString() });
            }

            if (icon.TryGetProperty("sizes", out var sizes) && !IsValidIconSizes(sizes))
            {
                AddToolSpecFinding(
                    result,
                    ValidationFindingRuleIds.ToolIconInvalid,
                    ValidationFindingSeverity.Medium,
                    $"Tool '{tool.Name}' declares icons[{index}].sizes with invalid entries.",
                    "Use 'any' or WxH strings such as '48x48' in icons[].sizes.",
                    metadata: new Dictionary<string, string> { ["index"] = index.ToString() });
            }

            index++;
        }
    }

    private static void ValidateToolExecutionMetadata(ToolInfo tool, IndividualToolResult result)
    {
        if (!tool.HasExecution)
        {
            return;
        }

        if (tool.Execution.ValueKind != JsonValueKind.Object)
        {
            AddToolSpecFinding(
                result,
                ValidationFindingRuleIds.ToolExecutionTaskSupportInvalid,
                ValidationFindingSeverity.High,
                $"Tool '{tool.Name}' declares execution metadata, but execution is not an object.",
                "Declare execution as an object and execution.taskSupport as forbidden, optional, or required.");
            return;
        }

        if (tool.Execution.TryGetProperty("taskSupport", out var taskSupport) &&
            (taskSupport.ValueKind != JsonValueKind.String || taskSupport.GetString() is not ("forbidden" or "optional" or "required")))
        {
            AddToolSpecFinding(
                result,
                ValidationFindingRuleIds.ToolExecutionTaskSupportInvalid,
                ValidationFindingSeverity.High,
                $"Tool '{tool.Name}' declares execution.taskSupport outside the supported values forbidden/optional/required.",
                "Set execution.taskSupport to 'forbidden', 'optional', or 'required'.");
        }
    }

    private static void ApplyBlockingToolFindingStatus(IndividualToolResult result)
    {
        if (result.Status == TestStatus.Inconclusive)
        {
            return;
        }

        if (result.Findings.Any(finding =>
                finding.EffectiveSource == ValidationRuleSource.Spec &&
                finding.Severity >= ValidationFindingSeverity.High))
        {
            result.Status = TestStatus.Failed;
            result.MetadataValid = false;
        }
    }

    private static void AddToolSpecFinding(
        IndividualToolResult result,
        string ruleId,
        ValidationFindingSeverity severity,
        string summary,
        string recommendation,
        IEnumerable<string>? details = null,
        Dictionary<string, string>? metadata = null)
    {
        var finding = new ValidationFinding
        {
            RuleId = ruleId,
            Category = "ProtocolCompliance",
            Component = result.ToolName,
            Severity = severity,
            Summary = summary,
            Recommendation = recommendation,
            Source = ValidationRuleSource.Spec,
            SpecReference = "https://spec.modelcontextprotocol.io/specification/2025-11-25/server/tools"
        };

        if (metadata != null)
        {
            foreach (var item in metadata)
            {
                finding.Metadata[item.Key] = item.Value;
            }
        }

        var issue = summary;
        if (details != null)
        {
            var detailList = details.Where(detail => !string.IsNullOrWhiteSpace(detail)).Take(3).ToList();
            if (detailList.Count > 0)
            {
                issue = $"{summary} Details: {string.Join("; ", detailList)}";
                for (var i = 0; i < detailList.Count; i++)
                {
                    finding.Metadata[$"error{i + 1}"] = detailList[i];
                }
            }
        }

        result.AddFinding(finding, issue);
        result.MetadataValid = false;
    }

    private static void AddToolGuidelineFinding(
        IndividualToolResult result,
        string ruleId,
        ValidationFindingSeverity severity,
        string summary,
        string recommendation)
    {
        var finding = new ValidationFinding
        {
            RuleId = ruleId,
            Category = "ProtocolCompliance",
            Component = result.ToolName,
            Severity = severity,
            Summary = summary,
            Recommendation = recommendation,
            Source = ValidationRuleSource.Guideline,
            SpecReference = "https://modelcontextprotocol.io/specification/2025-11-25/server/tools#tool-names"
        };
        result.AddFinding(finding, summary);
    }

    private static bool TryConvertToJsonNode(JsonElement element, out JsonNode? node, out string? error)
    {
        try
        {
            node = JsonNode.Parse(element.GetRawText());
            error = null;
            return node is not null;
        }
        catch (Exception ex)
        {
            node = null;
            error = ex.Message;
            return false;
        }
    }

    private static bool ContainsControlCharacter(string value)
    {
        return value.Any(char.IsControl);
    }

    private static bool IsValidIconUri(string? value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == "data");
    }

    private static bool IsValidIconSizes(JsonElement sizes)
    {
        if (sizes.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var size in sizes.EnumerateArray())
        {
            if (size.ValueKind != JsonValueKind.String || !IsValidIconSize(size.GetString()))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsValidIconSize(string? value)
    {
        if (string.Equals(value, "any", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var parts = value.Split('x', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length == 2 && parts.All(part => part.All(char.IsDigit) && int.TryParse(part, out var size) && size > 0);
    }

    /// <summary>
    /// Validates the tools/call response body against MCP spec:
    /// - result.content MUST be an array of Content objects
    /// - Each Content MUST have a "type" field ("text", "image", "audio", "resource")
    /// - result.isError MAY be omitted; omitted is equivalent to false per the MCP schema
    /// </summary>
    private void ValidateToolCallResponseStructure(string rawJson, IndividualToolResult result, ToolInfo tool)
    {
        try
        {
            using var doc = JsonDocument.Parse(rawJson);

            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return; // Not a JSON-RPC envelope; nothing to validate here.
            }

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

            if (resultObj.ValueKind != JsonValueKind.Object)
            {
                result.AddFinding(new ValidationFinding
                {
                    RuleId = ValidationFindingRuleIds.ToolCallMissingResultObject,
                    Category = "ProtocolCompliance",
                    Component = result.ToolName,
                    Severity = ValidationFindingSeverity.High,
                    Summary = "tools/call 'result' is not a JSON object",
                    Recommendation = "MCP requires the JSON-RPC result to be an object containing a 'content' array."
                }, "❌ MCP Compliance: tools/call 'result' must be a JSON object");
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

            ValidateStructuredContentAgainstOutputSchema(resultObj, result, tool);

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

    private void ValidateStructuredContentAgainstOutputSchema(JsonElement resultObj, IndividualToolResult result, ToolInfo tool)
    {
        if (!tool.HasOutputSchema)
        {
            return;
        }

        if (resultObj.TryGetProperty("isError", out var isError) &&
            isError.ValueKind == JsonValueKind.True)
        {
            return;
        }

        if (!resultObj.TryGetProperty("structuredContent", out var structuredContent))
        {
            AddToolSpecFinding(
                result,
                ValidationFindingRuleIds.ToolCallStructuredContentMissing,
                ValidationFindingSeverity.High,
                $"Tool '{tool.Name}' declares outputSchema but tools/call result omitted structuredContent.",
                "When outputSchema is declared and the tool succeeds, return result.structuredContent matching that schema.");
            return;
        }

        if (structuredContent.ValueKind != JsonValueKind.Object)
        {
            AddToolSpecFinding(
                result,
                ValidationFindingRuleIds.ToolCallStructuredContentInvalid,
                ValidationFindingSeverity.Critical,
                $"Tool '{tool.Name}' returned structuredContent that is not a JSON object.",
                "Return structuredContent as a JSON object that matches the declared outputSchema.");
            return;
        }

        var contentConverted = TryConvertToJsonNode(structuredContent, out var structuredContentNode, out var contentError);
        var schemaConverted = TryConvertToJsonNode(tool.OutputSchema, out var outputSchemaNode, out var schemaError);
        if (!contentConverted || structuredContentNode is null || !schemaConverted || outputSchemaNode is null)
        {
            AddToolSpecFinding(
                result,
                ValidationFindingRuleIds.ToolCallStructuredContentInvalid,
                ValidationFindingSeverity.High,
                $"Tool '{tool.Name}' returned structuredContent or outputSchema that could not be parsed for validation.",
                "Return parseable JSON objects for outputSchema and structuredContent.",
                new[] { contentError, schemaError }.OfType<string>());
            return;
        }

        var validationResult = _schemaValidator.Validate(structuredContentNode, outputSchemaNode);
        if (!validationResult.IsValid)
        {
            var hasProcessingError = SchemaValidationHelpers.HasSchemaProcessingError(validationResult);
            AddToolSpecFinding(
                result,
                ValidationFindingRuleIds.ToolCallStructuredContentSchemaMismatch,
                hasProcessingError ? ValidationFindingSeverity.Medium : ValidationFindingSeverity.High,
                hasProcessingError
                    ? $"Tool '{tool.Name}' declared outputSchema, but structuredContent validation could not be fully processed."
                    : $"Tool '{tool.Name}' returned structuredContent that does not match outputSchema.",
                "Return structuredContent that satisfies the declared outputSchema, or adjust outputSchema to match the stable structured result shape.",
                validationResult.Errors);
        }
    }

    public async Task<ToolTestResult> ValidateToolExecutionAsync(McpServerConfig serverConfig, ToolTestingConfig config, CancellationToken cancellationToken = default)
    {
        return await ExecuteValidationAsync<ToolTestResult>(serverConfig, "Tool Execution", async (result, ct) =>
        {
            // Discover tools first
            var listResponse = await _httpClient.CallAsync(serverConfig.Endpoint!, ValidationConstants.Methods.ToolsList, null, serverConfig.Authentication, ct);

            if (AuthenticationChallengeInterpreter.Inspect(listResponse).RequiresAuthentication)
            {
                result.Status = TestStatus.AuthRequired;
                result.Message = "Tool execution requires authentication; rerun with credentials to validate tool calls.";
                return result;
            }

            if (!listResponse.IsSuccess)
            {
                result.Status = TestStatus.Failed;
                result.Message = $"tools/list failed: HTTP {listResponse.StatusCode}";
                return result;
            }

            result.CatalogEnumeration = new CatalogEnumerationEvidence();
            var pagination = await FetchAllToolsWithPaginationAsync(serverConfig.Endpoint!, serverConfig.Authentication, listResponse,
                (retained, rejections) =>
                {
                    result.ToolsDiscovered = retained.Count;
                    result.DiscoveredToolNames = retained.Select(tool => tool.Name).ToList();
                    result.ToolsTestFailed = checked((int)result.CatalogEnumeration.RejectedItems);
                    result.Issues.AddRange(rejections.Except(result.Issues, StringComparer.Ordinal));
                }, () => RecordMalformedCatalogItem(result), result.CatalogEnumeration, ct);
            var parseIssues = pagination.Issues;
            var tools = pagination.Tools;
            result.ToolsDiscovered = tools.Count;
            result.DiscoveredToolNames = tools.Select(tool => tool.Name).ToList();
            result.ToolsTestFailed = pagination.RejectedItems;
            result.Issues.AddRange(parseIssues.Distinct(StringComparer.Ordinal));
            if (tools.Count == 0)
            {
                result.Status = pagination.RejectedItems > 0 ? TestStatus.Failed : pagination.IsComplete ? TestStatus.Passed :
                    result.CatalogEnumeration.Termination == CatalogEnumerationTermination.AuthenticationRequired ? TestStatus.AuthRequired : TestStatus.Inconclusive;
                result.Message = parseIssues.Count > 0 ? "Tool catalog could not be recovered." : "No tools to execute.";
                return result;
            }

            // Execute each tool with schema-aware safe payloads
            foreach (var tool in tools)
            {
                ct.ThrowIfCancellationRequested();
                var toolResult = new IndividualToolResult
                {
                    ToolName = tool.Name,
                    DiscoveredCorrectly = true,
                    MetadataValid = true
                };
                result.ToolResults.Add(toolResult);

                try
                {
                    if (!TryCreateToolCallParams(tool, out var toolCallParams))
                    {
                        toolResult.Status = TestStatus.Inconclusive;
                        toolResult.ExecutionSuccessful = false;
                        toolResult.Issues.Add("Tool invocation was not attempted: a bounded schema-valid synthetic argument set could not be established.");
                        continue;
                    }
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    var callResponse = await _httpClient.CallAsync(
                        serverConfig.Endpoint!,
                        ValidationConstants.Methods.ToolsCall,
                        toolCallParams,
                        serverConfig.Authentication,
                        ct);
                    sw.Stop();
                    toolResult.ExecutionTimeMs = sw.ElapsedMilliseconds;

                    if (AuthenticationChallengeInterpreter.Inspect(callResponse).RequiresAuthentication)
                    {
                        toolResult.Status = TestStatus.AuthRequired;
                        toolResult.ExecutionSuccessful = false;
                        toolResult.Issues.Add("Auth enforced on tools/call; secure challenge observed, call not validated.");
                    }
                    else if (callResponse.StatusCode > 0 && ValidationReliability.ShouldRetryRpcResponse(callResponse))
                    {
                        toolResult.Status = TestStatus.Inconclusive;
                        toolResult.ExecutionSuccessful = false;
                        toolResult.Issues.Add($"tools/call probe inconclusive due to transient transport pressure ({ValidationReliability.DescribeRetryableResponse(callResponse)})");
                    }
                    else if (callResponse.StatusCode <= 0 || callResponse.StatusCode is 400 or 404)
                    {
                        toolResult.Status = TestStatus.Inconclusive;
                        toolResult.ExecutionSuccessful = false;
                        toolResult.Issues.Add("Tool invocation did not establish the expected response; execution remains unverified.");
                    }
                    else if (callResponse.IsSuccess)
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
                catch (OperationCanceledException)
                {
                    toolResult.Status = ct.IsCancellationRequested ? TestStatus.Cancelled : TestStatus.Inconclusive;
                    toolResult.ExecutionSuccessful = false;
                    toolResult.Issues.Add("Tool invocation was cancelled or timed out; execution remains unverified.");
                    ct.ThrowIfCancellationRequested();
                }
                catch (Exception ex)
                {
                    toolResult.Status = TestStatus.Error;
                    toolResult.ExecutionSuccessful = false;
                    toolResult.Issues.Add("Tool invocation ended with a validator or transport error.");
                    Logger.LogError("Tool invocation failed with {ExceptionType}", ex.GetType().Name);
                }

                if (toolResult.Status == TestStatus.Passed) result.ToolsTestPassed++;
                else if (toolResult.Status == TestStatus.Failed) result.ToolsTestFailed++;
            }

            result.ToolsDiscovered = tools.Count;
            result.Score = _scoringStrategy.CalculateScore(result);
            result.Status = DetermineAggregateToolStatus(result.ToolResults, result.ToolsTestFailed);
            if (!pagination.IsComplete && result.Status == TestStatus.Passed)
                result.Status = result.CatalogEnumeration.Termination == CatalogEnumerationTermination.AuthenticationRequired
                    ? TestStatus.AuthRequired : TestStatus.Inconclusive;
            return result;
        }, cancellationToken);
    }

    private static TestStatus DetermineAggregateToolStatus(IEnumerable<IndividualToolResult> toolResults, int failedCount)
    {
        var materialized = toolResults.ToList();
        if (materialized.Any(result => result.Status == TestStatus.Error)) return TestStatus.Error;
        if (materialized.Any(result => result.Status == TestStatus.Cancelled)) return TestStatus.Cancelled;
        if (failedCount > 0)
        {
            return TestStatus.Failed;
        }

        if (materialized.Any(result => result.Status == TestStatus.AuthRequired))
        {
            return TestStatus.AuthRequired;
        }

        if (materialized.Any(result => result.Status == TestStatus.Inconclusive))
        {
            return TestStatus.Inconclusive;
        }

        return TestStatus.Passed;
    }

    public async Task<ToolTestResult> ValidateParameterValidationAsync(McpServerConfig serverConfig, string toolName, IEnumerable<ToolTestScenario> testScenarios, CancellationToken cancellationToken = default)
    {
        return await ExecuteValidationAsync<ToolTestResult>(serverConfig, $"Parameter Validation: {toolName}", async (result, ct) =>
        {
            await Task.CompletedTask;
            result.Status = TestStatus.Skipped;
            result.Message = "Parameter validation not yet fully implemented";
            return result;
        }, cancellationToken);
    }

    private static void RecordMalformedCatalogItem(ToolTestResult result)
    {
        if (result.Findings.Any(finding => finding.RuleId == ValidationFindingRuleIds.ToolListItemInvalid)) return;
        result.Findings.Add(new ValidationFinding
        {
            RuleId = ValidationFindingRuleIds.ToolListItemInvalid,
            Source = ValidationRuleSource.Spec,
            Severity = ValidationFindingSeverity.High,
            Category = "ToolDiscovery",
            Component = "tools/list",
            Summary = "A tool catalog item had an invalid object, name, description, title or annotation shape.",
            Recommendation = "Return tool objects with string names, string titles/descriptions and correctly typed annotations."
        });
    }

    private static ToolInfo? ParseToolItem(JsonElement tool, IList<string> issues, Action reportMalformedItem)
    {
        if (CatalogMetadataGuard.HasMalformedText(tool, "name", "title", "description") ||
            !tool.TryGetProperty("name", out var name) || HasMalformedToolMetadata(tool))
        {
            issues.Add("Tool catalog item rejected: invalid name, description, title or annotation shape.");
            reportMalformedItem();
            return null;
        }
        var info = new ToolInfo { Name = name.GetString() ?? string.Empty, RawDefinition = tool.Clone() };
        if (tool.TryGetProperty("inputSchema", out var schema))
        {
            info.HasInputSchema = true;
            info.InputSchema = schema.Clone();
        }
        else
        {
            using var empty = JsonDocument.Parse("{}");
            info.InputSchema = empty.RootElement.Clone();
        }
        if (tool.TryGetProperty("outputSchema", out var outputSchema))
        {
            info.HasOutputSchema = true;
            info.OutputSchema = outputSchema.Clone();
        }
        if (tool.TryGetProperty("icons", out var icons))
        {
            info.HasIcons = true;
            info.Icons = icons.Clone();
        }
        if (tool.TryGetProperty("execution", out var execution))
        {
            info.HasExecution = true;
            info.Execution = execution.Clone();
        }
        if (tool.TryGetProperty("title", out var title)) info.Title = title.GetString();
        if (tool.TryGetProperty("description", out var description)) info.Description = description.GetString();
        if (tool.TryGetProperty("annotations", out var annotations))
        {
            if (annotations.TryGetProperty("title", out var annotationTitle)) info.AnnotationTitle = annotationTitle.GetString();
            if (annotations.TryGetProperty("readOnlyHint", out var readOnly)) info.ReadOnlyHint = readOnly.GetBoolean();
            if (annotations.TryGetProperty("destructiveHint", out var destructive)) info.DestructiveHint = destructive.GetBoolean();
            if (annotations.TryGetProperty("openWorldHint", out var openWorld)) info.OpenWorldHint = openWorld.GetBoolean();
            if (annotations.TryGetProperty("idempotentHint", out var idempotent)) info.IdempotentHint = idempotent.GetBoolean();
        }
        if (tool.TryGetProperty("_meta", out var metadata) && metadata.ValueKind == JsonValueKind.Object)
        {
            info.AppMetadata = McpAppToolMetadataParser.Parse(metadata);
            if (metadata.TryGetProperty("anthropic/maxResultSizeChars", out var maxResultSizeChars) &&
                maxResultSizeChars.ValueKind == JsonValueKind.Number && maxResultSizeChars.TryGetInt64(out var overrideChars))
                info.AnthropicMaxResultSizeChars = overrideChars;
        }
        return info;
    }

    private static bool HasMalformedToolMetadata(JsonElement tool)
    {
        foreach (var property in tool.EnumerateObject())
        {
            if (property.Name is "title" or "description" && property.Value.ValueKind != JsonValueKind.String)
                return true;
            if (property.Name != "annotations") continue;
            if (property.Value.ValueKind != JsonValueKind.Object) return true;
            foreach (var annotation in property.Value.EnumerateObject())
            {
                if (annotation.Name == "title" && annotation.Value.ValueKind != JsonValueKind.String) return true;
                if (annotation.Name is "readOnlyHint" or "destructiveHint" or "openWorldHint" or "idempotentHint" &&
                    annotation.Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Fetches all tools with pagination support (handles nextCursor per MCP spec).
    /// </summary>
    private async Task<ToolPaginationFetchResult> FetchAllToolsWithPaginationAsync(
        string endpoint, AuthenticationConfig? auth, JsonRpcResponse initialResponse, Action<List<ToolInfo>, List<string>> retainInventory,
        Action reportMalformedItem, CatalogEnumerationEvidence enumeration, CancellationToken ct)
    {
        var parseIssues = new List<string>();
        var allTools = new List<ToolInfo>();
        var issues = new List<string>();
        await foreach (var item in BoundedCatalogReader.ReadAsync(initialResponse, "tools", CatalogEnumerationLimits.MaximumItems,
            enumeration, issues, (cursor, token) => _httpClient.CallAsync(endpoint, ValidationConstants.Methods.ToolsList, new { cursor }, auth, token), ct))
        {
            var tool = ParseToolItem(item, parseIssues, reportMalformedItem);
            if (tool is null) enumeration.RejectedItems++;
            else
            {
                allTools.Add(tool);
                enumeration.RecoveredItems++;
            }
            retainInventory(allTools, parseIssues);
        }
        if (enumeration.ContinuationObserved) issues.Add("Server uses pagination for tools/list (nextCursor detected).");
        if (enumeration.PageAttempts > 1)
            issues.Add($"Pagination: retained {allTools.Count} tools after {enumeration.PageAttempts} page attempts; enumeration {(enumeration.IsComplete ? "complete" : "incomplete")}.");
        issues.AddRange(parseIssues.Distinct(StringComparer.Ordinal));
        return new ToolPaginationFetchResult(
            allTools,
            issues,
            enumeration.PageAttempts,
            enumeration.ContinuationObserved,
            enumeration.Termination == CatalogEnumerationTermination.CursorLoop,
            enumeration.PayloadCharactersObserved,
            checked((int)enumeration.RejectedItems),
            enumeration.IsComplete);
    }

    private bool TryCreateToolCallParams(ToolInfo tool, out object? parameters)
    {
        parameters = null;
        if (!tool.HasInputSchema) return false;
        var inspection = new SchemaInspectionContext(tool.InputSchema);
        if (!inspection.TryCreateObjectCandidate(out var arguments) || arguments is null) return false;
        var schema = System.Text.Json.Nodes.JsonNode.Parse(tool.InputSchema.GetRawText());
        if (schema is null || !_schemaValidator.Validate(arguments, schema).IsValid) return false;
        parameters = new
        {
            name = tool.Name,
            arguments
        };
        return true;
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
        public JsonElement RawDefinition { get; set; }
        public bool HasInputSchema { get; set; }
        public JsonElement InputSchema { get; set; }
        public bool HasOutputSchema { get; set; }
        public JsonElement OutputSchema { get; set; }
        public bool HasIcons { get; set; }
        public JsonElement Icons { get; set; }
        public bool HasExecution { get; set; }
        public JsonElement Execution { get; set; }
        public string? DisplayTitle => !string.IsNullOrWhiteSpace(Title) ? Title : AnnotationTitle;
        // MCP Tool Annotations (spec: readOnlyHint, destructiveHint, openWorldHint, idempotentHint)
        public bool? ReadOnlyHint { get; set; }
        public bool? DestructiveHint { get; set; }
        public bool? OpenWorldHint { get; set; }
        public bool? IdempotentHint { get; set; }
        public long? AnthropicMaxResultSizeChars { get; set; }
        public McpAppToolMetadata? AppMetadata { get; set; }
    }

    private static void ApplyGuidelineFindings(
        ToolTestResult aggregateResult,
        ToolInfo tool,
        IndividualToolResult toolResult,
        TransportResult<CapabilitySummary>? capabilitySnapshot)
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
            tool.ReadOnlyHint != true && !tool.DestructiveHint.HasValue,
            ValidationFindingRuleIds.ToolGuidelineDestructiveHintMissing,
            ValidationFindingSeverity.Low,
            $"Write-capable tool '{tool.Name}' does not explicitly declare annotations.destructiveHint; MCP defaults it to true.",
            "Declare destructiveHint explicitly for write-capable tools when auditable intent is preferred; omission is interpreted as true by MCP.");

        AddGuidelineFindingIfMissing(
            aggregateResult,
            toolResult,
            !tool.OpenWorldHint.HasValue,
            ValidationFindingRuleIds.ToolGuidelineOpenWorldHintMissing,
            ValidationFindingSeverity.Low,
            $"Tool '{tool.Name}' does not explicitly declare annotations.openWorldHint; MCP defaults it to true.",
            "Declare openWorldHint explicitly when auditable server intent is preferred; omission is interpreted as true by MCP.");

        AddGuidelineFindingIfMissing(
            aggregateResult,
            toolResult,
            tool.ReadOnlyHint != true && !tool.IdempotentHint.HasValue,
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

        ApplyTaskSupportCapabilityFinding(aggregateResult, tool, toolResult, capabilitySnapshot);
    }

    private static void ApplyTaskSupportCapabilityFinding(
        ToolTestResult aggregateResult,
        ToolInfo tool,
        IndividualToolResult toolResult,
        TransportResult<CapabilitySummary>? capabilitySnapshot)
    {
        var taskSupport = GetTaskSupport(tool);
        if (taskSupport is not ("optional" or "required"))
        {
            return;
        }

        if (!CapabilitySnapshotUtils.IsCapabilityExplicitlyNotAdvertised(capabilitySnapshot, McpSpecConstants.Capabilities.TasksRequestsToolsCall))
        {
            return;
        }

        var finding = new ValidationFinding
        {
            RuleId = ValidationFindingRuleIds.AiTaskSupportWithoutCapability,
            Category = "AiSafety",
            Component = tool.Name,
            Severity = string.Equals(taskSupport, "required", StringComparison.OrdinalIgnoreCase)
                ? ValidationFindingSeverity.High
                : ValidationFindingSeverity.Medium,
            Summary = $"Tool '{tool.Name}' declares execution.taskSupport={taskSupport}, but the server did not advertise tasks.requests.tools.call.",
            Recommendation = "Only declare tool task support when initialize advertises tasks.requests.tools.call, or set execution.taskSupport to forbidden so clients do not schedule unsupported task-augmented tool calls.",
            SpecReference = "https://modelcontextprotocol.io/specification/2025-11-25/basic/lifecycle#capabilities",
            Metadata = new Dictionary<string, string>
            {
                ["tool"] = tool.Name,
                ["taskSupport"] = taskSupport,
                ["capability"] = McpSpecConstants.Capabilities.TasksRequestsToolsCall,
                ["declared"] = "false",
                ["serverTasksDeclared"] = CapabilitySnapshotUtils.IsCapabilityAdvertised(capabilitySnapshot, McpSpecConstants.Capabilities.Tasks).ToString().ToLowerInvariant(),
                ["safetyLane"] = "task-augmented-tools"
            }
        };

        toolResult.AddFinding(finding, finding.Summary);
        aggregateResult.Findings.Add(finding);
    }

    private static string? GetTaskSupport(ToolInfo tool)
    {
        if (!tool.HasExecution || tool.Execution.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!tool.Execution.TryGetProperty("taskSupport", out var taskSupport) || taskSupport.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return taskSupport.GetString();
    }

    private static bool MissingDestructiveConfirmationGuidance(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return true;
        }

        return !AiSafetyControlAnalyzer.HasHumanConfirmationLanguage(description);
    }

    private static void ApplyAiSafetyControlEvidence(IndividualToolResult toolResult, ToolInfo tool)
    {
        var analysis = AiSafetyControlAnalyzer.AnalyzeTool(new AiSafetyControlTarget
        {
            Name = tool.Name,
            Description = tool.Description,
            ReadOnlyHint = tool.ReadOnlyHint,
            DestructiveHint = tool.DestructiveHint,
            OpenWorldHint = tool.OpenWorldHint,
            ParameterNames = GetInputParameterNames(tool)
        });

        toolResult.AiSafetyControlEvidence.AddRange(analysis.Evidence);
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
        long TotalPayloadChars,
        int RejectedItems,
        bool IsComplete);

    private static JsonRpcResponse? CreateResponseFromSnapshot(TransportResult<CapabilitySummary>? snapshot)
    {
        if (snapshot?.Payload == null)
        {
            return null;
        }

        return CapabilitySnapshotUtils.CloneResponse(snapshot.Payload.ToolListResponse);
    }

    private List<ToolInfo>? BuildToolListFromSnapshot(CapabilitySummary? summary)
    {
        if (summary == null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(summary.ToolListResponse?.RawJson))
        {
            return null;
        }

        if (summary.Tools == null || summary.Tools.Count == 0)
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
