using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Constants;
using Mcp.Benchmark.Core.Services;

using Mcp.Benchmark.Infrastructure.Strategies.Scoring;
using Mcp.Compliance.Spec;
using Mcp.Benchmark.Infrastructure.Utilities;

namespace Mcp.Benchmark.Infrastructure.Validators;

public class PromptValidator : BaseValidator<PromptValidator>, IPromptValidator
{
    private static readonly string[] PromptSafetySensitiveTerms =
    [
        "delete",
        "destroy",
        "drop",
        "remove",
        "purge",
        "wipe",
        "reset",
        "revoke",
        "disable",
        "overwrite"
    ];

    private static readonly string[] PromptSafetyGuidanceCues =
    [
        "confirm",
        "confirmation",
        "approve",
        "approval",
        "review",
        "warning",
        "warn",
        "caution",
        "danger",
        "irreversible",
        "authorized",
        "authorization",
        "human"
    ];

    private static readonly string[] PromptRequiredArgumentGuidanceCues =
    [
        "required",
        "provide",
        "include",
        "supply",
        "all arguments",
        "all inputs",
        "before running",
        "before execution"
    ];

    private static readonly string[] PromptInjectionProneArgumentNames =
    [
        "code",
        "content",
        "input",
        "instructions",
        "markdown",
        "prompt",
        "query",
        "text",
        "url",
        "uri"
    ];

    private static readonly string[] PromptInjectionGuidanceCues =
    [
        "delimit",
        "escape",
        "ignore instructions",
        "injection",
        "sanitize",
        "treat as data",
        "untrusted",
        "validate"
    ];

    private static readonly string[] PromptInputOutputValidationCues =
    [
        "audit",
        "check",
        "review",
        "sanitize",
        "validate",
        "verify"
    ];

    private readonly IMcpHttpClient _httpClient;
    private readonly ISchemaValidator _schemaValidator;
    private readonly IScoringStrategy<PromptTestResult> _scoringStrategy;
    private readonly ISchemaRegistry _schemaRegistry;
    private readonly IContentSafetyAnalyzer _contentSafetyAnalyzer;

    public PromptValidator(
        ILogger<PromptValidator> logger,
        IMcpHttpClient httpClient,
        ISchemaValidator schemaValidator,
        ISchemaRegistry schemaRegistry,
        IContentSafetyAnalyzer contentSafetyAnalyzer)
        : base(logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _schemaValidator = schemaValidator ?? throw new ArgumentNullException(nameof(schemaValidator));
        _schemaRegistry = schemaRegistry ?? throw new ArgumentNullException(nameof(schemaRegistry));
        _contentSafetyAnalyzer = contentSafetyAnalyzer ?? throw new ArgumentNullException(nameof(contentSafetyAnalyzer));
        _scoringStrategy = new PromptScoringStrategy();
    }

    public async Task<PromptTestResult> ValidatePromptDiscoveryAsync(McpServerConfig serverConfig, PromptTestingConfig config, CancellationToken cancellationToken = default)
    {
        return await ExecuteValidationAsync(serverConfig, "Prompt Discovery", async (ct) =>
        {
            var result = new PromptTestResult();
            var capabilitySnapshot = config.CapabilitySnapshot;
            var cachedResponse = CapabilitySnapshotUtils.CloneResponse(capabilitySnapshot?.Payload?.PromptListResponse);

            if (CapabilitySnapshotUtils.IsCapabilityExplicitlyNotAdvertised(capabilitySnapshot, McpSpecConstants.Capabilities.Prompts))
            {
                result.Status = TestStatus.Skipped;
                result.Score = 100.0;
                result.Message = "Server does not advertise the prompts capability.";
                result.Issues.Add("Prompts capability was not advertised during initialize; prompts/list and prompts/get probes were skipped.");
                return result;
            }

            // Test prompts/list
            var response = cachedResponse;
            if (response == null)
            {
                response = await _httpClient.CallAsync(serverConfig.Endpoint!, ValidationConstants.Methods.PromptsList, null, ct);
            }
            
            // MCP spec allows both public and auth-protected servers
            var authChallenge = AuthenticationChallengeInterpreter.Inspect(response);
            if (authChallenge.RequiresAuthentication)
            {
                result.Status = TestStatus.AuthRequired;
                result.PromptsDiscovered = 0;
                result.Score = 0;
                result.Issues.Add(authChallenge.HasWwwAuthenticateHeader 
                    ? "Auth required: server returned an authentication challenge; prompt contents were not validated."
                    : "Auth required: server rejected prompt discovery but did not include WWW-Authenticate metadata.");
                return result;
            }

            if (JsonRpcResponseInspector.IsMethodNotFound(response))
            {
                result.Status = TestStatus.Passed;
                result.PromptsDiscovered = 0;
                result.Score = 100;
                result.Issues.Add("✅ COMPLIANT: No prompts were advertised; no prompt executions were required");
                return result;
            }
            
            if (!response.IsSuccess)
            {
                result.Status = TestStatus.Failed;
                result.Issues.Add($"❌ NON-COMPLIANT: prompts/list failed: {response.Error}");
                return result;
            }

            var rawJson = response.RawJson ?? "{}";
            var jsonDoc = JsonDocument.Parse(rawJson);
            if (jsonDoc.RootElement.TryGetProperty("result", out var resultElement) &&
                resultElement.TryGetProperty("prompts", out var promptsElement) &&
                promptsElement.ValueKind == JsonValueKind.Array)
            {
                result.PromptsDiscovered = promptsElement.GetArrayLength();
                
                foreach (var prompt in promptsElement.EnumerateArray())
                {
                    var promptResult = new IndividualPromptResult
                    {
                        DiscoveredCorrectly = true,
                        MetadataValid = true
                    };

                    if (prompt.TryGetProperty("name", out var nameElement))
                    {
                        promptResult.PromptName = nameElement.GetString() ?? "";
                    }
                    else
                    {
                        promptResult.MetadataValid = false;
                        promptResult.AddFinding(new ValidationFinding
                        {
                            RuleId = ValidationFindingRuleIds.PromptMissingName,
                            Category = "ProtocolCompliance",
                            Component = "prompt",
                            Severity = ValidationFindingSeverity.High,
                            Summary = "Prompt is missing 'name' property",
                            Recommendation = "Return a stable name for every prompt listed by prompts/list."
                        }, "Missing 'name' property");
                    }

                    if (prompt.TryGetProperty("description", out var descElement))
                    {
                        promptResult.Description = descElement.GetString();
                    }

                    // Validate arguments structure if present
                    var requiredArgumentsCount = 0;
                    var argumentsMissingDescriptions = 0;
                    var promptArguments = new List<PromptArgumentInfo>();
                    if (prompt.TryGetProperty("arguments", out var argsElement))
                    {
                        if (argsElement.ValueKind != JsonValueKind.Array)
                        {
                            promptResult.MetadataValid = false;
                            promptResult.AddFinding(new ValidationFinding
                            {
                                RuleId = ValidationFindingRuleIds.PromptArgumentsNotArray,
                                Category = "ProtocolCompliance",
                                Component = string.IsNullOrWhiteSpace(promptResult.PromptName) ? "prompt" : promptResult.PromptName,
                                Severity = ValidationFindingSeverity.High,
                                Summary = "Prompt arguments must be an array when present.",
                                Recommendation = "Return prompt arguments as an array of argument metadata objects."
                            }, "'arguments' must be an array");
                        }
                        else
                        {
                            promptResult.ArgumentsCount = argsElement.GetArrayLength();
                            foreach (var arg in argsElement.EnumerateArray())
                            {
                                var argumentName = arg.TryGetProperty("name", out var argName) && argName.ValueKind == JsonValueKind.String
                                    ? argName.GetString() ?? string.Empty
                                    : string.Empty;
                                var argumentDescription = arg.TryGetProperty("description", out var argDescriptionElement) && argDescriptionElement.ValueKind == JsonValueKind.String
                                    ? argDescriptionElement.GetString()
                                    : null;

                                if (arg.TryGetProperty("required", out var requiredElement) &&
                                    requiredElement.ValueKind is JsonValueKind.True or JsonValueKind.False &&
                                    requiredElement.GetBoolean())
                                {
                                    requiredArgumentsCount++;
                                }

                                if (string.IsNullOrWhiteSpace(argumentName))
                                {
                                    promptResult.MetadataValid = false;
                                    promptResult.AddFinding(new ValidationFinding
                                    {
                                        RuleId = ValidationFindingRuleIds.PromptArgumentMissingName,
                                        Category = "ProtocolCompliance",
                                        Component = string.IsNullOrWhiteSpace(promptResult.PromptName) ? "prompt" : promptResult.PromptName,
                                        Severity = ValidationFindingSeverity.High,
                                        Summary = "Prompt argument is missing a name.",
                                        Recommendation = "Return a stable name for every prompt argument."
                                    }, "Argument missing 'name'");
                                }

                                if (string.IsNullOrWhiteSpace(argumentDescription))
                                {
                                    argumentsMissingDescriptions++;
                                }

                                promptArguments.Add(new PromptArgumentInfo(argumentName, argumentDescription));
                            }
                        }
                    }

                    ApplyPromptGuidelineFindings(result, promptResult, requiredArgumentsCount, argumentsMissingDescriptions, promptArguments);

                    promptResult.Status = promptResult.MetadataValid ? TestStatus.Passed : TestStatus.Failed;

                    // EXECUTION: prompts/get — actually retrieve the prompt messages
                    if (config.TestPromptExecution && promptResult.MetadataValid && !string.IsNullOrEmpty(promptResult.PromptName))
                    {
                        try
                        {
                            var getParams = new Dictionary<string, object> { { "name", promptResult.PromptName } };

                            // If the prompt has required arguments, supply dummy values
                            if (prompt.TryGetProperty("arguments", out var argsSchema) && argsSchema.ValueKind == JsonValueKind.Array)
                            {
                                var dummyArgs = new Dictionary<string, string>();
                                foreach (var arg in argsSchema.EnumerateArray())
                                {
                                    if (arg.TryGetProperty("name", out var argName) && arg.TryGetProperty("required", out var req) && req.GetBoolean())
                                    {
                                        dummyArgs[argName.GetString() ?? "arg"] = "mcp-benchmark-test";
                                    }
                                }
                                if (dummyArgs.Count > 0)
                                {
                                    getParams["arguments"] = dummyArgs;
                                }
                            }

                            var getStart = DateTime.UtcNow;
                            var getResponse = await _httpClient.CallAsync(serverConfig.Endpoint!, ValidationConstants.Methods.PromptsGet, getParams, serverConfig.Authentication, ct);
                            promptResult.ExecutionTimeMs = (DateTime.UtcNow - getStart).TotalMilliseconds;

                            if (getResponse.IsSuccess)
                            {
                                promptResult.ExecutionSuccessful = true;

                                // MCP Spec Compliance: Validate prompts/get response structure
                                // Response MUST have result.messages[] with role + content
                                if (!string.IsNullOrEmpty(getResponse.RawJson))
                                {
                                    try
                                    {
                                        using var getDoc = JsonDocument.Parse(getResponse.RawJson);
                                        if (getDoc.RootElement.TryGetProperty("result", out var getResult))
                                        {
                                            // Check messages[] array (MUST per spec)
                                            if (getResult.TryGetProperty("messages", out var messages) && messages.ValueKind == JsonValueKind.Array)
                                            {
                                                promptResult.Issues.Add($"✅ prompts/get: messages[] array present ({messages.GetArrayLength()} messages)");
                                                
                                                foreach (var msg in messages.EnumerateArray())
                                                {
                                                    // Each message MUST have 'role' (user|assistant)
                                                    if (!msg.TryGetProperty("role", out var role))
                                                    {
                                                        promptResult.AddFinding(new ValidationFinding
                                                        {
                                                            RuleId = ValidationFindingRuleIds.PromptMessageMissingRole,
                                                            Category = "ProtocolCompliance",
                                                            Component = promptResult.PromptName,
                                                            Severity = ValidationFindingSeverity.Critical,
                                                            Summary = "Prompt message missing 'role' field",
                                                            Recommendation = "Return role on every prompt message with a valid MCP role value."
                                                        }, "❌ MCP Compliance: message missing 'role' field (MUST be 'user' or 'assistant')");
                                                        promptResult.MetadataValid = false;
                                                    }
                                                    else
                                                    {
                                                        var roleStr = role.GetString();
                                                        if (roleStr is not ("user" or "assistant"))
                                                            promptResult.AddFinding(new ValidationFinding
                                                            {
                                                                RuleId = ValidationFindingRuleIds.PromptMessageInvalidRole,
                                                                Category = "ProtocolCompliance",
                                                                Component = promptResult.PromptName,
                                                                Severity = ValidationFindingSeverity.High,
                                                                Summary = $"Prompt message role '{roleStr}' is invalid",
                                                                Recommendation = "Use valid MCP prompt roles such as user or assistant.",
                                                                Metadata = { ["role"] = roleStr ?? string.Empty }
                                                            }, $"❌ MCP Compliance: message role '{roleStr}' invalid (MUST be 'user' or 'assistant')");
                                                    }

                                                    // Each message MUST have 'content' (object with type+text or array)
                                                    if (!msg.TryGetProperty("content", out var content))
                                                    {
                                                        promptResult.Issues.Add("❌ MCP Compliance: message missing 'content' field");
                                                        promptResult.MetadataValid = false;
                                                    }
                                                    else if (content.ValueKind == JsonValueKind.Object)
                                                    {
                                                        ValidatePromptContentBlock(result, promptResult, content);
                                                    }
                                                    else if (content.ValueKind == JsonValueKind.Array)
                                                    {
                                                        foreach (var contentBlock in content.EnumerateArray())
                                                        {
                                                            ValidatePromptContentBlock(result, promptResult, contentBlock);
                                                        }
                                                    }
                                                }
                                            }
                                            else
                                            {
                                                promptResult.AddFinding(new ValidationFinding
                                                {
                                                    RuleId = ValidationFindingRuleIds.PromptGetMissingMessagesArray,
                                                    Category = "ProtocolCompliance",
                                                    Component = promptResult.PromptName,
                                                    Severity = ValidationFindingSeverity.Critical,
                                                    Summary = "prompts/get result missing 'messages' array",
                                                    Recommendation = "Return result.messages as an array for prompts/get responses."
                                                }, "❌ MCP Compliance: prompts/get result missing 'messages' array (MUST per spec)");
                                                promptResult.MetadataValid = false;
                                            }
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        promptResult.Issues.Add($"⚠️ Failed to validate prompts/get response: {ex.Message}");
                                    }
                                }
                                else
                                {
                                    promptResult.Issues.Add("✅ prompts/get returned successfully");
                                }
                            }
                            else if (getResponse.StatusCode == 400)
                            {
                                // 400 is acceptable when we send dummy arguments that don't match real constraints
                                promptResult.ExecutionSuccessful = true;
                                promptResult.Issues.Add("⚠️ prompts/get returned 400 (expected with dummy arguments)");
                            }
                            else
                            {
                                promptResult.ExecutionSuccessful = false;
                                promptResult.Issues.Add($"❌ prompts/get failed: {getResponse.Error ?? getResponse.StatusCode.ToString()}");
                            }
                        }
                        catch (Exception ex)
                        {
                            promptResult.ExecutionSuccessful = false;
                            promptResult.Issues.Add($"❌ prompts/get exception: {ex.Message}");
                        }
                    }

                    // Static, metadata-only content safety analysis
                    if (!string.IsNullOrWhiteSpace(promptResult.PromptName) || !string.IsNullOrWhiteSpace(promptResult.Description))
                    {
                        var safetyContext = ContentSafetyAnalysisContext.FromServerConfig(serverConfig);
                        var safetyFindings = _contentSafetyAnalyzer.AnalyzePrompt(
                            promptResult.PromptName,
                            promptResult.Description,
                            promptResult.ArgumentsCount,
                            safetyContext);

                        if (safetyFindings.Count > 0)
                        {
                            promptResult.ContentSafetyFindings.AddRange(safetyFindings);
                            result.ContentSafetyFindings.AddRange(safetyFindings);
                        }
                    }

                    result.PromptResults.Add(promptResult);
                }
            }

                    // Schema-based validation of prompts/list response shape (best-effort)
                    var protocolVersion = SchemaValidationHelpers.ResolveProtocolVersion(_schemaRegistry, serverConfig.ProtocolVersion);
                    if (SchemaValidationHelpers.TryValidateListResult(
                    _schemaRegistry,
                    _schemaValidator,
                    protocolVersion,
                    SchemaValidationHelpers.ListPromptsResultDefinition,
                    rawJson,
                    Logger,
                    out var schemaValidationResult) &&
                schemaValidationResult is not null &&
                !schemaValidationResult.IsValid)
            {
                var schemaErrors = schemaValidationResult.Errors ?? new List<string>();
                var hasProcessingError = SchemaValidationHelpers.HasSchemaProcessingError(schemaValidationResult);
                if (!hasProcessingError)
                {
                    result.Status = TestStatus.Failed;
                }

                result.Issues.Add(SchemaValidationHelpers.FormatListSchemaIssueHeader(ValidationConstants.Methods.PromptsList, hasProcessingError));
                foreach (var error in schemaErrors)
                {
                    result.Issues.Add($"   • {error}");
                }
            }
            
            if (result.Status != TestStatus.Failed)
            {
                result.PromptsTestPassed = result.PromptResults.Count(r => r.Status == TestStatus.Passed);
                result.PromptsTestFailed = result.PromptResults.Count(r => r.Status == TestStatus.Failed);
                result.Status = result.PromptsTestFailed == 0 ? TestStatus.Passed : TestStatus.Failed;
            }
            
            // Calculate Score using Strategy
            result.Score = _scoringStrategy.CalculateScore(result);

            result.Issues.Add(result.PromptsDiscovered == 0
                ? "✅ COMPLIANT: No prompts were advertised; no prompt executions were required"
                : $"✅ COMPLIANT: {result.PromptsDiscovered} prompts discovered and validated");
            
            return result;
        }, cancellationToken);
    }

    public async Task<PromptTestResult> ValidatePromptExecutionAsync(McpServerConfig serverConfig, PromptTestingConfig config, CancellationToken cancellationToken = default)
    {
        // Discovery already includes prompts/get execution when TestPromptExecution is enabled.
        // This method provides a standalone entry point for execution-only testing.
        return await ValidatePromptDiscoveryAsync(serverConfig, config, cancellationToken);
    }

    private static void ApplyPromptGuidelineFindings(
        PromptTestResult aggregateResult,
        IndividualPromptResult promptResult,
        int requiredArgumentsCount,
        int argumentsMissingDescriptions,
        IReadOnlyCollection<PromptArgumentInfo> promptArguments)
    {
        AddPromptFindingIf(
            aggregateResult,
            promptResult,
            !string.IsNullOrWhiteSpace(promptResult.PromptName) && string.IsNullOrWhiteSpace(promptResult.Description),
            ValidationFindingRuleIds.PromptGuidelineDescriptionMissing,
            ValidationFindingSeverity.Low,
            $"Prompt '{promptResult.PromptName}' does not include a description.",
            "Add a concise prompt description so clients and agents can understand when to use this prompt.");

        AddPromptFindingIf(
            aggregateResult,
            promptResult,
            promptResult.ArgumentsCount > 0 && argumentsMissingDescriptions > 0,
            ValidationFindingRuleIds.PromptGuidelineArgumentDescriptionMissing,
            ValidationFindingSeverity.Low,
            $"Prompt '{promptResult.PromptName}' has {argumentsMissingDescriptions}/{promptResult.ArgumentsCount} arguments without descriptions.",
            "Describe each prompt argument so agents can populate required inputs without guessing.");

        AddPromptFindingIf(
            aggregateResult,
            promptResult,
            requiredArgumentsCount >= 3 && MissingPromptArgumentComplexityGuidance(promptResult.Description),
            ValidationFindingRuleIds.PromptArgumentComplexityGuidanceMissing,
            ValidationFindingSeverity.Low,
            $"Prompt '{promptResult.PromptName}' requires {requiredArgumentsCount} inputs but its description does not explain that callers must supply multiple required arguments.",
            "Mention when a prompt expects multiple required inputs so callers can prepare the full argument set before execution.");

        AddPromptFindingIf(
            aggregateResult,
            promptResult,
            PromptLooksSafetySensitive(promptResult) && MissingPromptSafetyGuidance(promptResult.Description),
            ValidationFindingRuleIds.PromptSafetyGuidanceMissing,
            ValidationFindingSeverity.Medium,
            $"Prompt '{promptResult.PromptName}' appears to describe destructive or high-impact actions but does not mention confirmation, approval, or warning guidance.",
            "Add explicit confirmation, approval, or warning language when a prompt can guide destructive or high-impact operations.");

        AddPromptFindingIf(
            aggregateResult,
            promptResult,
            PromptHasInjectionProneArguments(promptArguments) && MissingPromptInjectionGuidance(promptResult.Description, promptArguments),
            ValidationFindingRuleIds.PromptInjectionGuidanceMissing,
            ValidationFindingSeverity.Medium,
            $"Prompt '{promptResult.PromptName}' accepts freeform or instruction-like arguments without prompt-injection guardrail guidance.",
            "Document how callers should delimit, sanitize, or treat user-supplied prompt/code/content arguments as untrusted data before inserting them into prompt messages.");

        AddPromptFindingIf(
            aggregateResult,
            promptResult,
            promptArguments.Count > 0 && MissingPromptInputOutputValidationGuidance(promptResult.Description, promptArguments),
            ValidationFindingRuleIds.PromptInputOutputValidationAdvisory,
            ValidationFindingSeverity.Low,
            $"Prompt '{promptResult.PromptName}' accepts arguments but does not describe input or output validation expectations.",
            "Describe expected input validation, output review, or sanitization steps for prompts that transform caller-provided arguments into model-visible messages.");
    }

    private static void ValidatePromptContentBlock(
        PromptTestResult aggregateResult,
        IndividualPromptResult promptResult,
        JsonElement content)
    {
        var component = string.IsNullOrWhiteSpace(promptResult.PromptName) ? "prompt" : promptResult.PromptName;
        if (content.ValueKind != JsonValueKind.Object)
        {
            AddPromptFinding(aggregateResult, promptResult, new ValidationFinding
            {
                RuleId = ValidationFindingRuleIds.PromptContentMissingType,
                Category = "ProtocolCompliance",
                Component = component,
                Severity = ValidationFindingSeverity.High,
                Summary = "Prompt content block must be an object with a type field.",
                Recommendation = "Return each prompt content block as an object with a supported content type."
            }, "❌ MCP Compliance: content block must be an object with a type field");
            return;
        }

        foreach (var finding in McpContentValidationUtils.ValidateAnnotations(
            content,
            component,
            ValidationFindingRuleIds.PromptAnnotationInvalid,
            "ProtocolCompliance"))
        {
            AddPromptFinding(aggregateResult, promptResult, finding, finding.Summary);
        }

        if (!content.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(type.GetString()))
        {
            AddPromptFinding(aggregateResult, promptResult, new ValidationFinding
            {
                RuleId = ValidationFindingRuleIds.PromptContentMissingType,
                Category = "ProtocolCompliance",
                Component = component,
                Severity = ValidationFindingSeverity.High,
                Summary = "Prompt content block missing required type field.",
                Recommendation = "Return a supported prompt content type on every content block."
            }, "❌ MCP Compliance: content block missing 'type' field");
            return;
        }

        switch (type.GetString())
        {
            case "image":
            case "audio":
                ValidatePromptBinaryContent(aggregateResult, promptResult, content, type.GetString()!);
                break;
            case "resource":
                ValidatePromptEmbeddedResource(aggregateResult, promptResult, content);
                break;
        }
    }

    private static void ValidatePromptBinaryContent(
        PromptTestResult aggregateResult,
        IndividualPromptResult promptResult,
        JsonElement content,
        string contentType)
    {
        var component = string.IsNullOrWhiteSpace(promptResult.PromptName) ? "prompt" : promptResult.PromptName;
        if (!content.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.String || !McpContentValidationUtils.IsValidBase64(data.GetString()))
        {
            AddPromptFinding(aggregateResult, promptResult, new ValidationFinding
            {
                RuleId = ValidationFindingRuleIds.PromptContentBlobInvalidBase64,
                Category = "ProtocolCompliance",
                Component = component,
                Severity = ValidationFindingSeverity.High,
                Summary = $"Prompt {contentType} content is missing valid base64 data.",
                Recommendation = "Encode prompt image/audio payloads as base64 strings in the data field.",
                Metadata =
                {
                    ["contentType"] = contentType
                }
            }, $"❌ MCP Compliance: prompt {contentType} content data must be valid base64");
        }

        if (!content.TryGetProperty("mimeType", out var mimeType) || mimeType.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(mimeType.GetString()))
        {
            AddPromptFinding(aggregateResult, promptResult, new ValidationFinding
            {
                RuleId = ValidationFindingRuleIds.PromptEmbeddedResourceInvalid,
                Category = "ProtocolCompliance",
                Component = component,
                Severity = ValidationFindingSeverity.Medium,
                Summary = $"Prompt {contentType} content is missing mimeType.",
                Recommendation = "Return a valid MIME type for prompt image and audio content."
            }, $"❌ MCP Compliance: prompt {contentType} content missing mimeType");
        }
    }

    private static void ValidatePromptEmbeddedResource(
        PromptTestResult aggregateResult,
        IndividualPromptResult promptResult,
        JsonElement content)
    {
        var component = string.IsNullOrWhiteSpace(promptResult.PromptName) ? "prompt" : promptResult.PromptName;
        if (!content.TryGetProperty("resource", out var resource) || resource.ValueKind != JsonValueKind.Object)
        {
            AddPromptFinding(aggregateResult, promptResult, new ValidationFinding
            {
                RuleId = ValidationFindingRuleIds.PromptEmbeddedResourceInvalid,
                Category = "ProtocolCompliance",
                Component = component,
                Severity = ValidationFindingSeverity.High,
                Summary = "Prompt embedded resource content is missing the resource object.",
                Recommendation = "Return embedded resources as content blocks with type=resource and a resource object."
            }, "❌ MCP Compliance: embedded resource content missing resource object");
            return;
        }

        foreach (var finding in McpContentValidationUtils.ValidateAnnotations(
            resource,
            component,
            ValidationFindingRuleIds.PromptAnnotationInvalid,
            "ProtocolCompliance"))
        {
            AddPromptFinding(aggregateResult, promptResult, finding, finding.Summary);
        }

        var hasUri = resource.TryGetProperty("uri", out var uri) && uri.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(uri.GetString());
        var hasMimeType = resource.TryGetProperty("mimeType", out var mimeType) && mimeType.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(mimeType.GetString());
        var hasText = resource.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String;
        var hasBlob = resource.TryGetProperty("blob", out var blob) && blob.ValueKind == JsonValueKind.String;

        if (!hasUri || !hasMimeType || (!hasText && !hasBlob))
        {
            AddPromptFinding(aggregateResult, promptResult, new ValidationFinding
            {
                RuleId = ValidationFindingRuleIds.PromptEmbeddedResourceInvalid,
                Category = "ProtocolCompliance",
                Component = component,
                Severity = ValidationFindingSeverity.High,
                Summary = "Prompt embedded resource must include uri, mimeType, and either text or blob content.",
                Recommendation = "Return embedded resources with a valid uri, mimeType, and exactly one usable text or base64 blob payload.",
                Metadata =
                {
                    ["hasUri"] = hasUri.ToString().ToLowerInvariant(),
                    ["hasMimeType"] = hasMimeType.ToString().ToLowerInvariant(),
                    ["hasPayload"] = (hasText || hasBlob).ToString().ToLowerInvariant()
                }
            }, "❌ MCP Compliance: embedded resource missing uri, mimeType, or payload");
        }

        if (hasBlob && !McpContentValidationUtils.IsValidBase64(blob.GetString()))
        {
            AddPromptFinding(aggregateResult, promptResult, new ValidationFinding
            {
                RuleId = ValidationFindingRuleIds.PromptContentBlobInvalidBase64,
                Category = "ProtocolCompliance",
                Component = component,
                Severity = ValidationFindingSeverity.High,
                Summary = "Prompt embedded resource blob is not valid base64.",
                Recommendation = "Encode binary embedded-resource payloads as base64 strings in the blob field."
            }, "❌ MCP Compliance: embedded resource blob must be valid base64");
        }

        if (hasUri && EmbeddedResourceLooksSensitive(uri.GetString()))
        {
            AddPromptFinding(aggregateResult, promptResult, new ValidationFinding
            {
                RuleId = ValidationFindingRuleIds.PromptEmbeddedResourceSafetyAdvisory,
                Category = "AiSafety",
                Component = component,
                Severity = ValidationFindingSeverity.Medium,
                Summary = "Prompt embeds a sensitive-looking resource directly into model-visible messages.",
                Recommendation = "Require authorization and user review before embedding sensitive resources into prompts, and use annotations.audience/priority to clarify who should see the content.",
                Metadata =
                {
                    ["resourceUri"] = uri.GetString() ?? string.Empty,
                    ["safetyLane"] = "prompt-embedded-resource"
                }
            }, "Prompt embeds a sensitive-looking resource; require authorization and review before model exposure.");
        }
    }

    private static void AddPromptFinding(
        PromptTestResult aggregateResult,
        IndividualPromptResult promptResult,
        ValidationFinding finding,
        string issue)
    {
        if (IsBlockingProtocolFinding(finding))
        {
            promptResult.MetadataValid = false;
            promptResult.Status = TestStatus.Failed;
        }

        promptResult.AddFinding(finding, issue);
        aggregateResult.Findings.Add(finding);
    }

    private static bool IsBlockingProtocolFinding(ValidationFinding finding)
    {
        return string.Equals(finding.Category, "ProtocolCompliance", StringComparison.OrdinalIgnoreCase) &&
            finding.Severity >= ValidationFindingSeverity.High;
    }

    private static void AddPromptFindingIf(
        PromptTestResult aggregateResult,
        IndividualPromptResult promptResult,
        bool condition,
        string ruleId,
        ValidationFindingSeverity severity,
        string summary,
        string recommendation)
    {
        if (!condition)
        {
            return;
        }

        var finding = new ValidationFinding
        {
            RuleId = ruleId,
            Category = "McpGuideline",
            Component = string.IsNullOrWhiteSpace(promptResult.PromptName) ? "prompt" : promptResult.PromptName,
            Severity = severity,
            Summary = summary,
            Recommendation = recommendation
        };

        promptResult.Findings.Add(finding);
        aggregateResult.Findings.Add(finding);
    }

    private static bool MissingPromptArgumentComplexityGuidance(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return true;
        }

        return !PromptRequiredArgumentGuidanceCues.Any(cue => description.Contains(cue, StringComparison.OrdinalIgnoreCase));
    }

    private static bool PromptLooksSafetySensitive(IndividualPromptResult promptResult)
    {
        var candidateText = $"{promptResult.PromptName} {promptResult.Description}";
        return PromptSafetySensitiveTerms.Any(term => candidateText.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private static bool MissingPromptSafetyGuidance(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return true;
        }

        return !PromptSafetyGuidanceCues.Any(cue => description.Contains(cue, StringComparison.OrdinalIgnoreCase));
    }

    private static bool PromptHasInjectionProneArguments(IReadOnlyCollection<PromptArgumentInfo> arguments)
    {
        return arguments.Any(argument =>
            PromptInjectionProneArgumentNames.Any(name => argument.Name.Contains(name, StringComparison.OrdinalIgnoreCase)) ||
            PromptInjectionProneArgumentNames.Any(name => argument.Description?.Contains(name, StringComparison.OrdinalIgnoreCase) == true));
    }

    private static bool MissingPromptInjectionGuidance(string? description, IReadOnlyCollection<PromptArgumentInfo> arguments)
    {
        var candidateText = string.Join(" ", new[] { description ?? string.Empty }
            .Concat(arguments.Select(argument => argument.Description ?? string.Empty)));
        return !PromptInjectionGuidanceCues.Any(cue => candidateText.Contains(cue, StringComparison.OrdinalIgnoreCase));
    }

    private static bool MissingPromptInputOutputValidationGuidance(string? description, IReadOnlyCollection<PromptArgumentInfo> arguments)
    {
        var candidateText = string.Join(" ", new[] { description ?? string.Empty }
            .Concat(arguments.Select(argument => argument.Description ?? string.Empty)));
        return !PromptInputOutputValidationCues.Any(cue => candidateText.Contains(cue, StringComparison.OrdinalIgnoreCase));
    }

    private static bool EmbeddedResourceLooksSensitive(string? uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
        {
            return false;
        }

        if (Uri.TryCreate(uri, UriKind.Absolute, out var parsed) && parsed.Scheme is "file" or "ssh" or "sftp" or "postgres" or "mysql" or "mongodb" or "redis" or "s3" or "vault")
        {
            return true;
        }

        return new[] { "secret", "token", "credential", "password", "private", "customer", "database", "admin", "ssh", "key" }
            .Any(term => uri.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private sealed record PromptArgumentInfo(string Name, string? Description);
}
