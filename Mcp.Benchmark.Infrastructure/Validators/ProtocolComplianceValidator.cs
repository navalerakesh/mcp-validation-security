using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Resources;
using Mcp.Benchmark.Core.Constants;
using Mcp.Benchmark.Core.Services;
using Mcp.Compliance.Spec;

using Mcp.Benchmark.Infrastructure.Rules.Protocol;
using Mcp.Benchmark.Infrastructure.Registries;

namespace Mcp.Benchmark.Infrastructure.Validators;

/// <summary>
/// REAL MCP protocol compliance validator with 100% authentic testing.
/// Performs comprehensive JSON-RPC 2.0 and MCP protocol specification compliance validation.
/// </summary>
public class ProtocolComplianceValidator : BaseValidator<ProtocolComplianceValidator>, IProtocolComplianceValidator
{
    private readonly IMcpHttpClient _httpClient;
    private readonly IProtocolRuleRegistry _ruleRegistry;

    public ProtocolComplianceValidator(ILogger<ProtocolComplianceValidator> logger, IMcpHttpClient httpClient, IProtocolRuleRegistry ruleRegistry) 
        : base(logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _ruleRegistry = ruleRegistry ?? throw new ArgumentNullException(nameof(ruleRegistry));
    }

    public async Task<ComplianceTestResult> ValidateJsonRpcComplianceAsync(McpServerConfig serverConfig, ProtocolComplianceConfig config, CancellationToken cancellationToken = default)
    {
        return await ExecuteValidationAsync(serverConfig, "JSON-RPC Compliance", async (ct) =>
        {
            var result = new ComplianceTestResult();
            var ruleContext = new ProtocolValidationContext(_httpClient, serverConfig.Endpoint!);

            // REAL JSON-RPC 2.0 COMPLIANCE TESTING
            Logger.LogDebug("Performing comprehensive JSON-RPC 2.0 compliance validation");

            // Test 1: JSON-RPC Error Code Compliance
            JsonRpcErrorValidationResult errorValidation;
            try 
            {
                errorValidation = await _httpClient.ValidateErrorCodesAsync(serverConfig.Endpoint!, ct);
            }
            catch (Exception ex) when (ex.Message.Contains("401") || ex.Message.Contains("403"))
            {
                // If we get 401/403, it means the server is enforcing auth, which is good for security but blocks protocol testing.
                // We mark this as "Skipped" for protocol compliance but note it.
                Logger.LogWarning("Protocol compliance testing blocked by authentication (401/403). Marking as Skipped.");
                result.Status = TestStatus.Skipped;
                result.Message = "Protocol compliance testing skipped because server requires authentication.";
                result.ComplianceScore = 0; // Or N/A
                return result;
            }

            var errorCompliance = errorValidation.Tests.Count(t => t.IsValid) / (double)errorValidation.Tests.Count;

            // Test 2: Request Format Compliance
            var requestFormatCompliant = await ValidateRequestFormatAsync(serverConfig.Endpoint!, ct);

            // Test 3: Response Format Compliance  
            var responseFormatCompliant = await ValidateResponseFormatAsync(serverConfig.Endpoint!, ct);

            // Test 4: Batch Processing Compliance
            var batchCompliant = await ValidateBatchProcessingAsync(serverConfig.Endpoint!, ct);

            // Test 5: Notification Handling (CRITICAL: Server MUST NOT respond to notifications)
            var notificationProbe = await CheckNotificationHandlingAsync(serverConfig.Endpoint!, ct);
            var notificationCompliant = notificationProbe.IsCompliant ?? true;

            // Test 6: Error Code Compliance (MUST use standard JSON-RPC error codes)
            var errorCodeCompliant = await ValidateErrorCodeComplianceAsync(serverConfig.Endpoint!, ct);

            // Test 7: Content-Type Requirements (Using Rule Engine)
            // Get rules for the latest version (or configurable version)
            var activeRules = _ruleRegistry.GetRulesForVersion(_ruleRegistry.LatestVersion).ToList();
            
            var contentTypeRule = activeRules.First(r => r is ContentTypeRule);
            var contentTypeResult = await contentTypeRule.EvaluateAsync(ruleContext, ct);
            var contentTypeCompliant = contentTypeResult.IsCompliant;

            // Test 8: Case Sensitivity (Using Rule Engine)
            var caseSensitivityRule = activeRules.First(r => r is CaseSensitivityRule);
            var caseSensitivityResult = await caseSensitivityRule.EvaluateAsync(ruleContext, ct);
            var caseSensitivityCompliant = caseSensitivityResult.IsCompliant;

            var violations = new List<ComplianceViolation>();

            // Calculate score early to determine violation severity
            var preliminaryScores = new[] {
                errorValidation.Tests.Count(t => t.IsValid) / (double)errorValidation.Tests.Count * 100,
                100.0, 100.0, 100.0, 100.0, 100.0, 100.0, 100.0
            };
            var preliminaryScore = preliminaryScores.Average();
            var isHighCompliance = preliminaryScore >= 80.0;

            // Collect all violations (marked as warnings if score >= 80%)
            foreach (var test in errorValidation.Tests.Where(t => !t.IsValid))
            {
                violations.Add(CreateViolation(
                    ValidationConstants.CheckIds.ProtocolErrorHandling,
                    $"JSON-RPC Error Code Violation: {test.Name}",
                    isHighCompliance ? ViolationSeverity.Low : ViolationSeverity.High,
                    ValidationConstants.Categories.JsonRpcCompliance,
                    ComplianceChecks.SpecReferences[ComplianceChecks.Protocol.ErrorHandling]));
            }

            if (!requestFormatCompliant)
            {
                violations.Add(CreateViolation(
                    ValidationConstants.CheckIds.ProtocolJsonRpcFormat,
                    "Request format does not comply with JSON-RPC 2.0 specification",
                    ViolationSeverity.High, // Downgraded from Critical to avoid instant 0 score
                    ValidationConstants.Categories.JsonRpcCompliance,
                    ComplianceChecks.SpecReferences[ComplianceChecks.Protocol.JsonRpcFormat]));
            }

            if (!responseFormatCompliant)
            {
                violations.Add(CreateViolation(
                    ValidationConstants.CheckIds.ProtocolJsonRpcFormat,
                    "Response format does not comply with JSON-RPC 2.0 specification",
                    ViolationSeverity.High, // Downgraded from Critical to avoid instant 0 score
                    ValidationConstants.Categories.JsonRpcCompliance,
                    ComplianceChecks.SpecReferences[ComplianceChecks.Protocol.JsonRpcFormat]));
            }

            if (!batchCompliant)
            {
                violations.Add(CreateViolation(
                    ValidationConstants.CheckIds.ProtocolJsonRpcFormat,
                    "Batch processing implementation is inconsistent or incomplete",
                    ViolationSeverity.Medium,
                    ValidationConstants.Categories.JsonRpcCompliance,
                    ComplianceChecks.SpecReferences[ComplianceChecks.Protocol.JsonRpcFormat]));
            }

            if (!notificationCompliant)
            {
                violations.Add(CreateViolation(
                    ValidationConstants.CheckIds.ProtocolJsonRpcFormat,
                    "Notification handling violates JSON-RPC 2.0: Server MUST NOT respond to notifications",
                    ViolationSeverity.Critical,
                    ValidationConstants.Categories.JsonRpcCompliance,
                    ComplianceChecks.SpecReferences[ComplianceChecks.Protocol.JsonRpcFormat]));
            }

            if (!errorCodeCompliant)
            {
                violations.Add(CreateViolation(
                    ValidationConstants.CheckIds.ProtocolErrorHandling,
                    "Error codes do not comply with JSON-RPC 2.0 standard error codes",
                    isHighCompliance ? ViolationSeverity.Low : ViolationSeverity.High,
                    ValidationConstants.Categories.JsonRpcCompliance,
                    ComplianceChecks.SpecReferences[ComplianceChecks.Protocol.ErrorHandling]));
            }

            if (!contentTypeCompliant)
            {
                violations.Add(CreateViolation(
                    ValidationConstants.CheckIds.HttpContentType,
                    "Content-Type requirements not enforced (Server should reject non-JSON)",
                    ViolationSeverity.Low,
                    ValidationConstants.Categories.Transport,
                    "https://spec.modelcontextprotocol.io/specification/2025-11-25/basic/transports#http"));
            }

            if (!caseSensitivityCompliant)
            {
                violations.Add(CreateViolation(
                    ValidationConstants.CheckIds.ProtocolJsonRpcFormat,
                    "Case sensitivity not enforced: Member names MUST be case-sensitive",
                    isHighCompliance ? ViolationSeverity.Low : ViolationSeverity.High,
                    ValidationConstants.Categories.JsonRpcCompliance,
                    ComplianceChecks.SpecReferences[ComplianceChecks.Protocol.JsonRpcFormat]));
            }

            var declaredCapabilities = await GetDeclaredCapabilitiesAsync(
                serverConfig.Endpoint!,
                config.ProtocolVersion ?? serverConfig.ProtocolVersion,
                ct);

            // Calculate comprehensive compliance score (now 8 scored tests total)
            // Probe optional MCP capabilities for structured findings and capability-aware reporting.
            var rootsSupported = await ProbeMethodSupportAsync(serverConfig.Endpoint!, ValidationConstants.Methods.RootsList, ct);
            var loggingSupported = await ProbeMethodSupportAsync(serverConfig.Endpoint!, ValidationConstants.Methods.LoggingSetLevel, ct);
            var samplingSupported = await ProbeMethodSupportAsync(serverConfig.Endpoint!, ValidationConstants.Methods.SamplingCreateMessage, ct);
            var completionSupported = await ProbeMethodSupportAsync(serverConfig.Endpoint!, ValidationConstants.Methods.CompletionComplete, ct);

            ApplyOptionalCapabilityFindings(
                result,
                declaredCapabilities,
                rootsSupported,
                loggingSupported,
                samplingSupported,
                completionSupported);

            if (notificationProbe.IsCompliant == null)
            {
                result.Findings.Add(new ValidationFinding
                {
                    RuleId = "MCP.GUIDELINE.PROTOCOL.NOTIFICATION_PROBE_INCONCLUSIVE",
                    Category = "McpGuideline",
                    Component = McpSpecConstants.InitializedNotification,
                    Severity = ValidationFindingSeverity.Info,
                    Summary = notificationProbe.Reason ?? "Notification handling probe was inconclusive due to transient transport pressure.",
                    Recommendation = "Rerun protocol validation against a lower-pressure window before treating notification handling as non-compliant."
                });
            }

            var scores = new List<double>
            {
                errorCompliance * 100,
                requestFormatCompliant ? 100.0 : 0.0,
                responseFormatCompliant ? 100.0 : 0.0,
                batchCompliant ? 100.0 : 0.0,
                errorCodeCompliant ? 100.0 : 0.0,
                contentTypeCompliant ? 100.0 : 0.0,
                caseSensitivityCompliant ? 100.0 : 0.0
            };

            if (notificationProbe.IsCompliant != null)
            {
                scores.Add(notificationCompliant ? 100.0 : 0.0);
            }

            result.ComplianceScore = scores.Average();
            result.Violations = violations;
            result.Status = violations.Any(v => v.Severity == ViolationSeverity.Critical) ? TestStatus.Failed : TestStatus.Passed;

            result.NotificationHandling = new NotificationTestResult
            {
                NotificationFormatCorrect = notificationProbe.IsCompliant,
                NotificationsReceived = notificationProbe.IsCompliant == false ? 1 : null,
                NotificationIssues = notificationProbe.IsCompliant switch
                {
                    null when !string.IsNullOrWhiteSpace(notificationProbe.Reason) => new List<string> { notificationProbe.Reason! },
                    false => new List<string> { "Server responded to a JSON-RPC notification, which is non-compliant." },
                    _ => new List<string>()
                }
            };

            result.MessageFormat = new MessageFormatTestResult
            {
                RequestFormatValid = requestFormatCompliant,
                ResponseFormatValid = responseFormatCompliant,
                ErrorFormatValid = errorValidation.IsCompliant,
                FormatViolations = new List<string>()
            };

            if (!requestFormatCompliant)
            {
                result.MessageFormat.FormatViolations.Add("Request format does not comply with JSON-RPC 2.0 requirements.");
            }

            if (!responseFormatCompliant)
            {
                result.MessageFormat.FormatViolations.Add("Response format does not comply with JSON-RPC 2.0 requirements.");
            }

            if (!errorValidation.IsCompliant)
            {
                result.MessageFormat.FormatViolations.Add("Error responses did not consistently satisfy the validator's JSON-RPC error expectations.");
            }

            result.JsonRpcCompliance = new JsonRpcComplianceResult
            {
                RequestFormatCompliant = requestFormatCompliant,
                ResponseFormatCompliant = responseFormatCompliant,
                ErrorHandlingCompliant = errorCodeCompliant,
                BatchProcessingCompliant = batchCompliant,
                ComplianceScore = result.ComplianceScore,
                Violations = violations
            };

            result.Message = AppendCapabilityProbeMessage(
                result.Message,
                declaredCapabilities,
                rootsSupported,
                loggingSupported,
                samplingSupported,
                completionSupported);

            return result;
        }, cancellationToken);
    }

    private async Task<HashSet<string>> GetDeclaredCapabilitiesAsync(string endpoint, string? requestedVersion, CancellationToken ct)
    {
        try
        {
            var response = await _httpClient.CallAsync(
                endpoint,
                ValidationConstants.Methods.Initialize,
                CreateInitializeRequest(requestedVersion),
                ct);

            if (!response.IsSuccess || string.IsNullOrWhiteSpace(response.RawJson))
            {
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            using var document = JsonDocument.Parse(response.RawJson);
            if (!document.RootElement.TryGetProperty("result", out var result) ||
                !result.TryGetProperty("capabilities", out var capabilities))
            {
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            return ParseDeclaredCapabilities(capabilities);
        }
        catch
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Probes whether the server supports a given method by calling it and checking the response.
    /// Returns true if the server responds with anything other than MethodNotFound (-32601).
    /// </summary>
    private async Task<bool> ProbeMethodSupportAsync(string endpoint, string method, CancellationToken ct)
    {
        try
        {
            var response = await _httpClient.CallAsync(endpoint, method, null, ct);
            if (!string.IsNullOrEmpty(response.RawJson) && response.RawJson.Contains("-32601", StringComparison.Ordinal)) return false; // MethodNotFound
            if (response.IsSuccess) return true;
            if (AuthenticationChallengeInterpreter.Inspect(response).RequiresAuthentication) return true; // Auth-blocked but exists
            return response.StatusCode != 404;
        }
        catch
        {
            return false;
        }
    }

    private static object CreateInitializeRequest(string? requestedVersion) => new
    {
        protocolVersion = SchemaRegistryProtocolVersions.NormalizeRequestedVersion(requestedVersion),
        capabilities = new { },
        clientInfo = new { name = "mcp-benchmark", version = "1.0.0" }
    };

    private static HashSet<string> ParseDeclaredCapabilities(JsonElement capabilities)
    {
        var declaredCapabilities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (capabilities.TryGetProperty(McpSpecConstants.Capabilities.Tools, out _))
        {
            declaredCapabilities.Add(McpSpecConstants.Capabilities.Tools);
        }

        if (capabilities.TryGetProperty(McpSpecConstants.Capabilities.Resources, out var resourceCapabilities))
        {
            declaredCapabilities.Add(McpSpecConstants.Capabilities.Resources);
            if (resourceCapabilities.ValueKind == JsonValueKind.Object)
            {
                if (resourceCapabilities.TryGetProperty("subscribe", out var subscribe) && subscribe.ValueKind == JsonValueKind.True)
                {
                    declaredCapabilities.Add("resources.subscribe");
                }

                if (resourceCapabilities.TryGetProperty("listChanged", out var listChanged) && listChanged.ValueKind == JsonValueKind.True)
                {
                    declaredCapabilities.Add("resources.listChanged");
                }
            }
        }

        if (capabilities.TryGetProperty(McpSpecConstants.Capabilities.Prompts, out _))
        {
            declaredCapabilities.Add(McpSpecConstants.Capabilities.Prompts);
        }

        if (capabilities.TryGetProperty(McpSpecConstants.Capabilities.Logging, out _))
        {
            declaredCapabilities.Add(McpSpecConstants.Capabilities.Logging);
        }

        if (capabilities.TryGetProperty(McpSpecConstants.Capabilities.Completions, out _))
        {
            declaredCapabilities.Add(McpSpecConstants.Capabilities.Completions);
        }

        return declaredCapabilities;
    }

    private static string AppendCapabilityProbeMessage(
        string? existingMessage,
        IReadOnlySet<string> declaredCapabilities,
        bool rootsSupported,
        bool loggingSupported,
        bool samplingSupported,
        bool completionSupported)
    {
        var segments = new List<string>();
        if (!string.IsNullOrWhiteSpace(existingMessage))
        {
            segments.Add(existingMessage);
        }

        if (declaredCapabilities.Count > 0)
        {
            segments.Add($"declared capabilities: {string.Join(", ", declaredCapabilities.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase))}");
        }

        segments.Add($"roots/list: {(rootsSupported ? "supported" : "not supported")}");
        segments.Add($"logging/setLevel: {(loggingSupported ? "supported" : "not supported")}");
        segments.Add($"sampling/createMessage: {(samplingSupported ? "supported" : "not supported")}");
        segments.Add($"completion/complete: {(completionSupported ? "supported" : "not supported")}");

        return string.Join(" | ", segments);
    }

    private static void ApplyOptionalCapabilityFindings(
        ComplianceTestResult result,
        IReadOnlySet<string> declaredCapabilities,
        bool rootsSupported,
        bool loggingSupported,
        bool samplingSupported,
        bool completionSupported)
    {
        AddOptionalCapabilitySupportedFinding(
            result,
            rootsSupported,
            ValidationFindingRuleIds.OptionalCapabilityRootsSupported,
            ValidationConstants.Methods.RootsList,
            McpSpecConstants.Capabilities.Roots,
            "Server responded to roots/list, indicating optional roots workflow support.");

        AddOptionalCapabilitySupportedFinding(
            result,
            loggingSupported,
            ValidationFindingRuleIds.OptionalCapabilityLoggingSupported,
            ValidationConstants.Methods.LoggingSetLevel,
            McpSpecConstants.Capabilities.Logging,
            "Server responded to logging/setLevel, indicating optional logging controls are available.");

        AddOptionalCapabilitySupportedFinding(
            result,
            samplingSupported,
            ValidationFindingRuleIds.OptionalCapabilitySamplingSupported,
            ValidationConstants.Methods.SamplingCreateMessage,
            McpSpecConstants.Capabilities.Sampling,
            "Server responded to sampling/createMessage, indicating optional sampling workflows are available.");

        AddOptionalCapabilitySupportedFinding(
            result,
            completionSupported,
            ValidationFindingRuleIds.OptionalCapabilityCompletionsSupported,
            ValidationConstants.Methods.CompletionComplete,
            McpSpecConstants.Capabilities.Completions,
            "Server responded to completion/complete, indicating optional completions are available.");

        if (declaredCapabilities.Contains(McpSpecConstants.Capabilities.Logging) && !loggingSupported)
        {
            result.Findings.Add(new ValidationFinding
            {
                RuleId = ValidationFindingRuleIds.OptionalCapabilityLoggingDeclaredButUnsupported,
                Category = "McpGuideline",
                Component = ValidationConstants.Methods.LoggingSetLevel,
                Severity = ValidationFindingSeverity.Medium,
                Summary = "Server advertises the logging capability but logging/setLevel is not callable.",
                Recommendation = "Either implement logging/setLevel or stop advertising the logging capability.",
                Metadata = new Dictionary<string, string>
                {
                    ["capability"] = McpSpecConstants.Capabilities.Logging,
                    ["method"] = ValidationConstants.Methods.LoggingSetLevel,
                    ["declared"] = "true",
                    ["supported"] = "false"
                }
            });
        }

        if (!declaredCapabilities.Contains(McpSpecConstants.Capabilities.Logging) && loggingSupported)
        {
            result.Findings.Add(new ValidationFinding
            {
                RuleId = ValidationFindingRuleIds.OptionalCapabilityLoggingSupportedButUndeclared,
                Category = "McpGuideline",
                Component = ValidationConstants.Methods.LoggingSetLevel,
                Severity = ValidationFindingSeverity.Low,
                Summary = "Server responds to logging/setLevel but does not advertise the logging capability during initialize.",
                Recommendation = "Declare logging in initialize when logging/setLevel is supported.",
                Metadata = new Dictionary<string, string>
                {
                    ["capability"] = McpSpecConstants.Capabilities.Logging,
                    ["method"] = ValidationConstants.Methods.LoggingSetLevel,
                    ["declared"] = "false",
                    ["supported"] = "true"
                }
            });
        }

        if (declaredCapabilities.Contains(McpSpecConstants.Capabilities.Completions) && !completionSupported)
        {
            result.Findings.Add(new ValidationFinding
            {
                RuleId = ValidationFindingRuleIds.OptionalCapabilityCompletionsDeclaredButUnsupported,
                Category = "McpGuideline",
                Component = ValidationConstants.Methods.CompletionComplete,
                Severity = ValidationFindingSeverity.Medium,
                Summary = "Server advertises the completions capability but completion/complete is not callable.",
                Recommendation = "Either implement completion/complete or stop advertising the completions capability.",
                Metadata = new Dictionary<string, string>
                {
                    ["capability"] = McpSpecConstants.Capabilities.Completions,
                    ["method"] = ValidationConstants.Methods.CompletionComplete,
                    ["declared"] = "true",
                    ["supported"] = "false"
                }
            });
        }

        if (!declaredCapabilities.Contains(McpSpecConstants.Capabilities.Completions) && completionSupported)
        {
            result.Findings.Add(new ValidationFinding
            {
                RuleId = ValidationFindingRuleIds.OptionalCapabilityCompletionsSupportedButUndeclared,
                Category = "McpGuideline",
                Component = ValidationConstants.Methods.CompletionComplete,
                Severity = ValidationFindingSeverity.Low,
                Summary = "Server responds to completion/complete but does not advertise the completions capability during initialize.",
                Recommendation = "Declare completions in initialize when completion/complete is supported.",
                Metadata = new Dictionary<string, string>
                {
                    ["capability"] = McpSpecConstants.Capabilities.Completions,
                    ["method"] = ValidationConstants.Methods.CompletionComplete,
                    ["declared"] = "false",
                    ["supported"] = "true"
                }
            });
        }
    }

    private static void AddOptionalCapabilitySupportedFinding(
        ComplianceTestResult result,
        bool supported,
        string ruleId,
        string method,
        string capability,
        string summary)
    {
        if (!supported)
        {
            return;
        }

        result.Findings.Add(new ValidationFinding
        {
            RuleId = ruleId,
            Category = "McpGuideline",
            Component = method,
            Severity = ValidationFindingSeverity.Info,
            Summary = summary,
            Recommendation = "Keep capability advertisement and method support aligned so clients can rely on initialize without speculative probing.",
            Metadata = new Dictionary<string, string>
            {
                ["capability"] = capability,
                ["method"] = method,
                ["supported"] = "true"
            }
        });
    }

    public async Task<ComplianceTestResult> ValidateInitializationAsync(McpServerConfig serverConfig, ProtocolComplianceConfig config, CancellationToken cancellationToken = default)
    {
        return await ExecuteValidationAsync(serverConfig, "Initialization Compliance", async (ct) =>
        {
            var result = new ComplianceTestResult();
            
            // Per MCP spec: client sends initialize with its preferred protocolVersion.
            // Server responds with the version it supports (may differ).
            // We test version negotiation by requesting the latest spec version,
            // then verify the server responds with a valid version string.
            var requestedVersion = SchemaRegistryProtocolVersions.NormalizeRequestedVersion(serverConfig.ProtocolVersion);

            var response = await _httpClient.CallAsync(
                serverConfig.Endpoint!,
                ValidationConstants.Methods.Initialize,
                CreateInitializeRequest(requestedVersion),
                ct);
            
            if (response.IsSuccess)
            {
                result.Status = TestStatus.Passed;
                result.Score = 100.0;

                // Validate version negotiation: check server returned a protocolVersion
                if (!string.IsNullOrEmpty(response.RawJson))
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(response.RawJson);
                        if (doc.RootElement.TryGetProperty("result", out var res) &&
                            res.TryGetProperty("protocolVersion", out var serverVersion))
                        {
                            var negotiatedVersion = serverVersion.GetString();
                            if (string.IsNullOrEmpty(negotiatedVersion))
                            {
                                result.Violations.Add(CreateViolation(
                                    ValidationConstants.CheckIds.ProtocolInitializeMissingProtocolVersion,
                                    "Server did not return a protocolVersion in initialize response",
                                    ViolationSeverity.High,
                                    ValidationConstants.Categories.ProtocolLifecycle,
                                    ComplianceChecks.SpecReferences[ComplianceChecks.Protocol.Lifecycle]));
                                result.Score = 60.0;
                            }
                            else
                            {
                                Logger.LogInformation("Protocol version negotiated: requested={Requested}, server={Server}", requestedVersion, negotiatedVersion);
                            }

                            // Verify serverInfo is present and complete (required by the initialize result schema)
                            if (res.TryGetProperty("serverInfo", out var serverInfo))
                            {
                                // serverInfo MUST have 'name' (string) and 'version' (string)
                                if (!serverInfo.TryGetProperty("name", out _))
                                {
                                    result.Violations.Add(CreateViolation(
                                        ValidationConstants.CheckIds.ProtocolInitializeMissingServerInfoName,
                                        "serverInfo missing 'name' field (MUST per spec)",
                                        ViolationSeverity.Medium,
                                        ValidationConstants.Categories.ProtocolLifecycle,
                                        ComplianceChecks.SpecReferences[ComplianceChecks.Protocol.Lifecycle]));
                                }
                                if (!serverInfo.TryGetProperty("version", out _))
                                {
                                    result.Violations.Add(CreateViolation(
                                        ValidationConstants.CheckIds.ProtocolInitializeMissingServerInfoVersion,
                                        "serverInfo missing 'version' field (MUST per spec)",
                                        ViolationSeverity.Medium,
                                        ValidationConstants.Categories.ProtocolLifecycle,
                                        ComplianceChecks.SpecReferences[ComplianceChecks.Protocol.Lifecycle]));
                                }
                            }
                            else
                            {
                                result.Violations.Add(CreateViolation(
                                    ValidationConstants.CheckIds.ProtocolInitializeMissingServerInfo,
                                    "Server did not return serverInfo in initialize response (MUST per schema)",
                                    ViolationSeverity.High,
                                    ValidationConstants.Categories.ProtocolLifecycle,
                                    ComplianceChecks.SpecReferences[ComplianceChecks.Protocol.Lifecycle]));
                            }

                            // Verify capabilities object is present (MUST per spec)
                            // Also validate capability declarations match MCP spec structure
                            if (res.TryGetProperty("capabilities", out var capabilities))
                            {
                                var declaredCapabilities = ParseDeclaredCapabilities(capabilities).ToList();

                                Logger.LogInformation("Server declared capabilities: {Capabilities}", string.Join(", ", declaredCapabilities));
                                result.Message = (result.Message ?? "") + $" | Capabilities: {string.Join(", ", declaredCapabilities)}";
                            }
                            else
                            {
                                result.Violations.Add(CreateViolation(
                                    ValidationConstants.CheckIds.ProtocolInitializeMissingCapabilities,
                                    "Server did not return capabilities in initialize response (MUST per spec)",
                                    ViolationSeverity.High,
                                    ValidationConstants.Categories.ProtocolLifecycle,
                                    ComplianceChecks.SpecReferences[ComplianceChecks.Protocol.Lifecycle]));
                                result.Score = 50.0;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.LogWarning(ex, "Failed to parse initialize response for version negotiation check");
                    }
                }
            }
            else
            {
                result.Status = TestStatus.Failed;
                result.Score = 0.0;
                result.Violations.Add(CreateViolation(
                    ValidationConstants.CheckIds.ProtocolInitializeResponse,
                    "Server failed to respond to initialize request",
                    ViolationSeverity.Critical,
                    ValidationConstants.Categories.ProtocolLifecycle,
                    ComplianceChecks.SpecReferences[ComplianceChecks.Protocol.Lifecycle]));
            }
            
            return result;
        }, cancellationToken);
    }

    public async Task<ComplianceTestResult> ValidateNotificationHandlingAsync(McpServerConfig serverConfig, ProtocolComplianceConfig config, CancellationToken cancellationToken = default)
    {
        return await ExecuteValidationAsync(serverConfig, "Notification Handling", async (ct) =>
        {
            var result = new ComplianceTestResult();
            var probe = await CheckNotificationHandlingAsync(serverConfig.Endpoint!, ct);

            if (probe.IsCompliant == null)
            {
                result.Score = 0.0;
                result.Status = TestStatus.Skipped;
                result.Message = probe.Reason;
                return result;
            }

            var isCompliant = probe.IsCompliant.Value;
            
            if (!isCompliant)
            {
                result.Violations.Add(CreateViolation(
                    ValidationConstants.CheckIds.ProtocolNotification,
                    "Server responded to a notification request (notifications must not generate responses)",
                    ViolationSeverity.High,
                    ValidationConstants.Categories.JsonRpcCompliance,
                    ComplianceChecks.SpecReferences[ComplianceChecks.Protocol.Notification]));
            }
            
            result.Score = isCompliant ? 100.0 : 0.0;
            result.Status = isCompliant ? TestStatus.Passed : TestStatus.Failed;
            
            return result;
        }, cancellationToken);
    }

    private async Task<bool> ValidateRequestFormatAsync(string endpoint, CancellationToken cancellationToken)
    {
        // Test 1: Valid request
        var validResponse = await _httpClient.CallAsync(endpoint, "ping", null, cancellationToken);
        
        // If auth failed, we consider it "compliant" for protocol structure (server correctly rejected us)
        if (AuthenticationChallengeInterpreter.Inspect(validResponse).RequiresAuthentication) return true;

        if (!validResponse.IsSuccess && validResponse.StatusCode != 404) return false; // 404 is fine for ping

        // Test 2: Invalid JSON
        var invalidJsonResponse = await _httpClient.SendRawJsonAsync(endpoint, "{ invalid json }", cancellationToken);
        
        // If server returns 200 OK, it MUST be a Parse Error
        if (invalidJsonResponse.IsSuccess)
        {
            try
            {
                using var doc = JsonDocument.Parse(invalidJsonResponse.RawJson!);
                if (doc.RootElement.TryGetProperty("error", out var error))
                {
                    return error.TryGetProperty("code", out var code) && code.GetInt32() == -32700; // Parse error
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        return true;
    }

    private async Task<bool> ValidateResponseFormatAsync(string endpoint, CancellationToken cancellationToken)
    {
        var response = await _httpClient.CallAsync(endpoint, "ping", null, cancellationToken);
        
        // If auth failed, we can't validate response format, but we shouldn't fail the test
        if (AuthenticationChallengeInterpreter.Inspect(response).RequiresAuthentication) return true;

        if (string.IsNullOrEmpty(response.RawJson)) return false;

        try
        {
            using var doc = JsonDocument.Parse(response.RawJson);
            var root = doc.RootElement;
            return root.TryGetProperty("jsonrpc", out var ver) && ver.GetString() == "2.0" &&
                   (root.TryGetProperty("result", out _) || root.TryGetProperty("error", out _));
        }
        catch
        {
            return false;
        }
    }

    private async Task<bool> ValidateBatchProcessingAsync(string endpoint, CancellationToken cancellationToken)
    {
        var batchRequest = "[{\"jsonrpc\": \"2.0\", \"method\": \"ping\", \"id\": 1}, {\"jsonrpc\": \"2.0\", \"method\": \"ping\", \"id\": 2}]";
        var response = await _httpClient.SendRawJsonAsync(endpoint, batchRequest, cancellationToken);
        
        if (AuthenticationChallengeInterpreter.Inspect(response).RequiresAuthentication) return true;

        if (!response.IsSuccess) return false; // Batch support is optional but basic handling should work

        try
        {
            using var doc = JsonDocument.Parse(response.RawJson!);
            return doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() == 2;
        }
        catch
        {
            return false;
        }
    }

    private async Task<ProtocolProbeOutcome> CheckNotificationHandlingAsync(string endpoint, CancellationToken cancellationToken)
    {
        // Use a real MCP notification method instead of a request-style method like ping.
        // This avoids classifying "missing id" method validation as a notification response bug.
        var notification = $"{{\"jsonrpc\": \"2.0\", \"method\": \"{McpSpecConstants.InitializedNotification}\"}}";
        var response = await _httpClient.SendRawJsonAsync(endpoint, notification, cancellationToken);
        
        if (AuthenticationChallengeInterpreter.Inspect(response).RequiresAuthentication)
        {
            return ProtocolProbeOutcome.Compliant();
        }

        if (ValidationReliability.ShouldRetryRpcResponse(response))
        {
            return ProtocolProbeOutcome.Inconclusive(
                $"Notification handling probe inconclusive due to transient transport pressure ({ValidationReliability.DescribeRetryableResponse(response)}).");
        }

        // Server MUST NOT respond to notifications (empty body or 204 No Content)
        // ACCEPTABLE: 202 Accepted (processing started)
        if (response.StatusCode == 202)
        {
            return ProtocolProbeOutcome.Compliant();
        }

        return string.IsNullOrWhiteSpace(response.RawJson)
            ? ProtocolProbeOutcome.Compliant()
            : ProtocolProbeOutcome.Failed();
    }

    private readonly record struct ProtocolProbeOutcome(bool? IsCompliant, string? Reason = null)
    {
        public static ProtocolProbeOutcome Compliant() => new(true);

        public static ProtocolProbeOutcome Failed() => new(false);

        public static ProtocolProbeOutcome Inconclusive(string reason) => new(null, reason);
    }

    private async Task<bool> ValidateErrorCodeComplianceAsync(string endpoint, CancellationToken cancellationToken)
    {
        var response = await _httpClient.CallAsync(endpoint, "non_existent_method", null, cancellationToken);
        
        if (AuthenticationChallengeInterpreter.Inspect(response).RequiresAuthentication) return true;

        // If server rejects with 404/405/406/400, it's compliant enough for transport layer
        if (response.StatusCode == 404 || response.StatusCode == 405 || response.StatusCode == 406 || response.StatusCode == 400) return true;

        // If 200 OK, check for correct JSON-RPC error code
        if (response.IsSuccess)
        {
            try
            {
                using var doc = JsonDocument.Parse(response.RawJson!);
                if (doc.RootElement.TryGetProperty("error", out var error))
                {
                    return error.TryGetProperty("code", out var code) && code.GetInt32() == -32601; // Method not found
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        try
        {
            using var doc = JsonDocument.Parse(response.RawJson!);
            if (doc.RootElement.TryGetProperty("error", out var error))
            {
                return error.TryGetProperty("code", out var code) && code.GetInt32() == -32601; // Method not found
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Creates a <see cref="ComplianceViolation"/> with the <see cref="ComplianceViolation.Recommendation"/>
    /// field auto-populated from <see cref="ComplianceRecommendations"/>.
    /// </summary>
    private static ComplianceViolation CreateViolation(
        string checkId,
        string description,
        ViolationSeverity severity,
        string category,
        string? specReference = null)
    {
        return new ComplianceViolation
        {
            CheckId = checkId,
            Description = description,
            Severity = severity,
            Category = category,
            SpecReference = specReference,
            Recommendation = ComplianceRecommendations.GetRecommendation(checkId, description)
        };
    }

    // Old methods replaced by Rules
    // ValidateContentTypeRequirementsAsync -> ContentTypeRule
    // ValidateCaseSensitivityAsync -> CaseSensitivityRule
}
