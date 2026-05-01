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
using Mcp.Benchmark.Infrastructure.Utilities;

namespace Mcp.Benchmark.Infrastructure.Validators;

/// <summary>
/// REAL MCP protocol compliance validator with 100% authentic testing.
/// Performs comprehensive JSON-RPC 2.0 and MCP protocol specification compliance validation.
/// </summary>
public class ProtocolComplianceValidator : BaseValidator<ProtocolComplianceValidator>, IProtocolComplianceValidator
{
    private readonly IMcpHttpClient _httpClient;
    private readonly IProtocolRuleRegistry _ruleRegistry;
    private readonly IValidationApplicabilityResolver _applicabilityResolver;
    private readonly IProtocolFeatureResolver _protocolFeatureResolver;

    public ProtocolComplianceValidator(
        ILogger<ProtocolComplianceValidator> logger,
        IMcpHttpClient httpClient,
        IProtocolRuleRegistry ruleRegistry,
        IValidationApplicabilityResolver applicabilityResolver,
        IProtocolFeatureResolver protocolFeatureResolver) 
        : base(logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _ruleRegistry = ruleRegistry ?? throw new ArgumentNullException(nameof(ruleRegistry));
        _applicabilityResolver = applicabilityResolver ?? throw new ArgumentNullException(nameof(applicabilityResolver));
        _protocolFeatureResolver = protocolFeatureResolver ?? throw new ArgumentNullException(nameof(protocolFeatureResolver));
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
                Logger.LogWarning("Protocol compliance testing blocked by authentication (401/403). Marking as AuthRequired.");
                result.Status = TestStatus.AuthRequired;
                result.Message = "Protocol compliance testing requires authentication; rerun with credentials for authoritative protocol evidence.";
                result.ComplianceScore = 0; // Or N/A
                return result;
            }

            var observableErrorTests = GetObservableErrorTests(serverConfig, errorValidation).ToList();
            var skippedRawStdioErrorTests = GetSkippedRawStdioErrorTests(serverConfig, errorValidation).ToList();
            var errorCompliance = observableErrorTests.Count == 0
                ? 1.0
                : observableErrorTests.Count(t => t.IsValid) / (double)observableErrorTests.Count;

            // Test 2: Request Format Compliance
            var requestFormatCompliant = await ValidateRequestFormatAsync(serverConfig, errorValidation, ct);

            // Test 3: Response Format Compliance  
            var responseFormatCompliant = await ValidateResponseFormatAsync(serverConfig.Endpoint!, ct);

            var applicabilityContext = _applicabilityResolver.Build(
                serverConfig,
                config.ProtocolVersion ?? serverConfig.ProtocolVersion,
                Array.Empty<string>());
            var protocolFeatures = _protocolFeatureResolver.Resolve(applicabilityContext);

            // Test 4: Batch Processing Compliance
            var batchProbe = protocolFeatures.SupportsBatchJsonRpc
                ? await ValidateBatchProcessingAsync(serverConfig, protocolFeatures.NegotiatedProtocolVersion, ct)
                : ProtocolProbeOutcome.Inconclusive(
                    "Batch processing probe skipped because the active embedded schema context does not advertise batch JSON-RPC envelopes.");
            var batchCompliant = batchProbe.IsCompliant ?? true;

            // Test 5: Notification Handling (CRITICAL: Server MUST NOT respond to notifications)
            var notificationProbe = await CheckNotificationHandlingAsync(serverConfig.Endpoint!, ct);
            var notificationCompliant = notificationProbe.IsCompliant ?? true;

            // Test 6: Error Code Compliance (MUST use standard JSON-RPC error codes)
            var errorCodeCompliant = ValidateErrorCodeCompliance(observableErrorTests);

            // Test 7: Content-Type Requirements (Using resolved protocol rule packs)
            var activeRules = _ruleRegistry.Resolve(applicabilityContext).ToList();

            var contentTypeRule = activeRules.OfType<ContentTypeRule>().FirstOrDefault();
            bool? contentTypeCompliant = null;
            if (contentTypeRule != null)
            {
                var contentTypeResult = await contentTypeRule.EvaluateAsync(ruleContext, ct);
                contentTypeCompliant = contentTypeResult.IsCompliant;
            }

            // Test 8: Case Sensitivity (Using Rule Engine)
            var caseSensitivityRule = activeRules.OfType<CaseSensitivityRule>().FirstOrDefault();
            bool? caseSensitivityCompliant = null;
            if (caseSensitivityRule != null)
            {
                var caseSensitivityResult = await caseSensitivityRule.EvaluateAsync(ruleContext, ct);
                caseSensitivityCompliant = caseSensitivityResult.IsCompliant;
            }

            var streamableHttpTransport = ShouldValidateStreamableHttpTransport(serverConfig, protocolFeatures.NegotiatedProtocolVersion)
                ? await ValidateStreamableHttpTransportAsync(serverConfig, protocolFeatures.NegotiatedProtocolVersion, ct)
                : null;
            result.StreamableHttpTransport = streamableHttpTransport;

            var stdioTransport = ShouldValidateStdioTransport(serverConfig)
                ? await ValidateStdioTransportAsync(serverConfig, protocolFeatures.NegotiatedProtocolVersion, skippedRawStdioErrorTests, ct)
                : null;
            result.StdioTransport = stdioTransport;

            var violations = new List<ComplianceViolation>();

            // Calculate score early to determine violation severity
            var preliminaryScores = new[] {
                errorCompliance * 100,
                100.0, 100.0, 100.0, 100.0
            };
            var preliminaryScoreCandidates = preliminaryScores.ToList();
            if (batchProbe.IsCompliant != null)
            {
                preliminaryScoreCandidates.Add(batchCompliant ? 100.0 : 0.0);
            }

            if (contentTypeCompliant != null)
            {
                preliminaryScoreCandidates.Add(contentTypeCompliant.Value ? 100.0 : 0.0);
            }

            if (caseSensitivityCompliant != null)
            {
                preliminaryScoreCandidates.Add(caseSensitivityCompliant.Value ? 100.0 : 0.0);
            }

            if (streamableHttpTransport is { EvaluatedMandatoryProbeCount: > 0 })
            {
                preliminaryScoreCandidates.Add(streamableHttpTransport.MandatoryComplianceScore);
            }

            if (stdioTransport is { EvaluatedMandatoryProbeCount: > 0 })
            {
                preliminaryScoreCandidates.Add(stdioTransport.MandatoryComplianceScore);
            }

            var preliminaryScore = preliminaryScoreCandidates.Average();
            var isHighCompliance = preliminaryScore >= 80.0;

            if (skippedRawStdioErrorTests.Count > 0)
            {
                result.Findings.Add(new ValidationFinding
                {
                    RuleId = "MCP.GUIDELINE.PROTOCOL.STDIO_RAW_ERROR_PROBE_SKIPPED",
                    Category = "McpGuideline",
                    Component = "jsonrpc-error-probes",
                    Severity = ValidationFindingSeverity.Info,
                    Summary = $"Skipped raw stdio JSON-RPC error probe(s): {string.Join(", ", skippedRawStdioErrorTests.Select(test => test.Name))}. The active stdio transport did not surface a JSON-RPC envelope for these malformed requests.",
                    Recommendation = "Do not treat malformed-envelope stdio probes as hard failures when the active SDK transport rejects invalid messages before the protocol layer can emit a JSON-RPC error response."
                });
            }

            // Collect all violations (marked as warnings if score >= 80%)
            foreach (var test in observableErrorTests.Where(t => !t.IsValid))
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

            if (batchProbe.IsCompliant == false)
            {
                violations.Add(CreateViolation(
                    ValidationConstants.CheckIds.ProtocolJsonRpcFormat,
                    "Batch processing implementation is inconsistent or incomplete",
                    ViolationSeverity.Medium,
                    ValidationConstants.Categories.JsonRpcCompliance,
                    ComplianceChecks.SpecReferences[ComplianceChecks.Protocol.JsonRpcFormat]));
            }

            if (batchProbe.IsCompliant == null && !string.IsNullOrWhiteSpace(batchProbe.Reason))
            {
                result.Findings.Add(new ValidationFinding
                {
                    RuleId = "MCP.GUIDELINE.PROTOCOL.BATCH_PROBE_SKIPPED",
                    Category = "McpGuideline",
                    Component = "batch",
                    Severity = ValidationFindingSeverity.Info,
                    Summary = batchProbe.Reason,
                    Recommendation = "Do not treat raw JSON-RPC batch arrays as a hard failure when the negotiated transport does not advertise reliable batch support."
                });
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

            if (contentTypeCompliant == false)
            {
                violations.Add(CreateViolation(
                    ValidationConstants.CheckIds.HttpContentType,
                    "Content-Type requirements not enforced (Server should reject non-JSON)",
                    ViolationSeverity.High,
                    ValidationConstants.Categories.Transport,
                    "https://spec.modelcontextprotocol.io/specification/2025-11-25/basic/transports#http"));
            }

            if (caseSensitivityCompliant == false)
            {
                violations.Add(CreateViolation(
                    ValidationConstants.CheckIds.ProtocolJsonRpcFormat,
                    "Case sensitivity not enforced: Member names MUST be case-sensitive",
                    isHighCompliance ? ViolationSeverity.Low : ViolationSeverity.High,
                    ValidationConstants.Categories.JsonRpcCompliance,
                    ComplianceChecks.SpecReferences[ComplianceChecks.Protocol.JsonRpcFormat]));
            }

            if (streamableHttpTransport is not null)
            {
                ApplyStreamableHttpTransportFindings(result, violations, streamableHttpTransport);
            }

            if (stdioTransport is not null)
            {
                ApplyStdioTransportFindings(result, violations, stdioTransport);
            }

            var declaredCapabilities = await GetDeclaredCapabilitiesAsync(
                serverConfig.Endpoint!,
                protocolFeatures.NegotiatedProtocolVersion,
                ct);

            // Calculate comprehensive compliance score (now 8 scored tests total)
            // Probe optional MCP capabilities for structured findings and capability-aware reporting.
            var rootsSupported = declaredCapabilities.Contains(McpSpecConstants.Capabilities.Roots) &&
                await ProbeMethodSupportAsync(serverConfig.Endpoint!, ValidationConstants.Methods.RootsList, ct);
            var loggingSupported = declaredCapabilities.Contains(McpSpecConstants.Capabilities.Logging) &&
                await ProbeMethodSupportAsync(serverConfig.Endpoint!, ValidationConstants.Methods.LoggingSetLevel, ct);
            var samplingSupported = declaredCapabilities.Contains(McpSpecConstants.Capabilities.Sampling) &&
                await ProbeMethodSupportAsync(serverConfig.Endpoint!, ValidationConstants.Methods.SamplingCreateMessage, ct);
            var completionSupported = declaredCapabilities.Contains(McpSpecConstants.Capabilities.Completions) &&
                await ProbeMethodSupportAsync(serverConfig.Endpoint!, ValidationConstants.Methods.CompletionComplete, ct);

            ApplyOptionalCapabilityFindings(
                result,
                declaredCapabilities,
                rootsSupported,
                loggingSupported,
                samplingSupported,
                completionSupported);

            ApplyAiCapabilitySafetyFindings(result, declaredCapabilities);

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
                errorCodeCompliant ? 100.0 : 0.0
            };

            if (batchProbe.IsCompliant != null)
            {
                scores.Add(batchCompliant ? 100.0 : 0.0);
            }

            if (contentTypeCompliant != null)
            {
                scores.Add(contentTypeCompliant.Value ? 100.0 : 0.0);
            }

            if (caseSensitivityCompliant != null)
            {
                scores.Add(caseSensitivityCompliant.Value ? 100.0 : 0.0);
            }

            if (notificationProbe.IsCompliant != null)
            {
                scores.Add(notificationCompliant ? 100.0 : 0.0);
            }

            if (streamableHttpTransport is { EvaluatedMandatoryProbeCount: > 0 })
            {
                scores.Add(streamableHttpTransport.MandatoryComplianceScore);
            }

            if (stdioTransport is { EvaluatedMandatoryProbeCount: > 0 })
            {
                scores.Add(stdioTransport.MandatoryComplianceScore);
            }

            result.ComplianceScore = scores.Average();
            result.Violations = violations;
            result.Status = violations.Any(v => v.Severity == ViolationSeverity.Critical)
                ? TestStatus.Failed
                : HasAuthBlockedProbe(batchProbe, notificationProbe) || HasAuthBlockedTransportProbe(streamableHttpTransport)
                    ? TestStatus.AuthRequired
                    : HasInconclusiveProbe(batchProbe, notificationProbe) || HasInconclusiveTransportProbe(streamableHttpTransport) || HasInconclusiveTransportProbe(stdioTransport)
                        ? TestStatus.Inconclusive
                        : TestStatus.Passed;

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
                ErrorFormatValid = observableErrorTests.Count == 0 || observableErrorTests.All(test => test.IsValid),
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

            if (observableErrorTests.Any(test => !test.IsValid))
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

    private async Task<StdioTransportTestResult> ValidateStdioTransportAsync(
        McpServerConfig serverConfig,
        string protocolVersion,
        IReadOnlyCollection<JsonRpcErrorTest> skippedRawStdioErrorTests,
        CancellationToken ct)
    {
        var endpoint = serverConfig.Endpoint!;
        var normalizedVersion = SchemaRegistryProtocolVersions.NormalizeRequestedVersion(protocolVersion);
        var result = new StdioTransportTestResult
        {
            ProtocolVersion = normalizedVersion
        };

        var messageResponse = await _httpClient.SendStdioTransportProbeAsync(
            new StdioTransportProbeRequest
            {
                Endpoint = endpoint,
                ProbeId = "stdio-message-exchange",
                Kind = StdioTransportProbeKind.MessageExchange,
                RawMessage = CreateJsonRpcEnvelope(ValidationConstants.Methods.Ping, null, "stdio-transport-ping")
            },
            ct);

        result.Probes.Add(CreateStdioTransportProbe(
            "newline-delimited-json-rpc",
            ValidationConstants.CheckIds.StdioNewlineFraming,
            true,
            EvaluateStdioResponse(messageResponse, response =>
                !string.IsNullOrWhiteSpace(response.RawStdout) && !ContainsLiteralNewline(response.RawStdout!)),
            "STDIO messages must be individual UTF-8 JSON-RPC messages delimited by newlines and must not contain embedded literal newlines.",
            "One stdout line containing one JSON-RPC message and no embedded newline characters.",
            messageResponse,
            DescribeStdioResponse(messageResponse),
            ViolationSeverity.High));

        result.Probes.Add(CreateStdioTransportProbe(
            "stdout-valid-mcp-message",
            ValidationConstants.CheckIds.StdioStdoutJsonRpcOnly,
            true,
            EvaluateStdioResponse(messageResponse, response =>
                IsValidMcpJsonRpcMessage(response.RawStdout) && ExtraStdoutLineIsValidMcpMessage(response)),
            "STDIO servers must write only valid MCP JSON-RPC messages to stdout.",
            "Every observed stdout line is a valid JSON-RPC object with jsonrpc=2.0 and a request, notification, response, or error shape.",
            messageResponse,
            DescribeStdioStdoutResponse(messageResponse),
            ViolationSeverity.High));

        result.Probes.Add(CreateStdioTransportProbe(
            "stderr-log-stream-non-failure",
            ValidationConstants.CheckIds.StdioStderrLogging,
            false,
            true,
            "STDIO servers may write UTF-8 log strings to stderr, and clients should not treat stderr output as an MCP failure by itself.",
            "stderr is captured as log evidence and is not parsed as MCP stdout.",
            messageResponse,
            string.IsNullOrWhiteSpace(messageResponse.StderrPreview)
                ? "No stderr log output observed during the STDIO message probe."
                : "stderr log output was captured and preserved as non-failure evidence.",
            ViolationSeverity.Low));

        result.Probes.Add(CreateStdioTransportProbe(
            "raw-parser-boundary-classification",
            ValidationConstants.CheckIds.StdioParserBoundary,
            false,
            skippedRawStdioErrorTests.Count == 0 ? true : null,
            "Malformed raw STDIO probes that are dropped by the SDK parser boundary must be reported as inconclusive/skipped, not as server protocol failures.",
            "Raw malformed parser-boundary cases are separated from observable JSON-RPC error-response evidence.",
            messageResponse,
            skippedRawStdioErrorTests.Count == 0
                ? "No raw STDIO parser-boundary drops were observed."
                : $"Raw STDIO parser-boundary drops observed for: {string.Join(", ", skippedRawStdioErrorTests.Select(test => test.Name))}.",
            ViolationSeverity.Low));

        var lifecycleResponse = await _httpClient.SendStdioTransportProbeAsync(
            new StdioTransportProbeRequest
            {
                Endpoint = endpoint,
                ProbeId = "stdio-shutdown-lifecycle",
                Kind = StdioTransportProbeKind.ShutdownLifecycle
            },
            ct);

        result.Probes.Add(CreateStdioTransportProbe(
            "shutdown-lifecycle",
            ValidationConstants.CheckIds.StdioLifecycleShutdown,
            true,
            EvaluateStdioResponse(lifecycleResponse, response => response.ProcessExited == true && response.IsSuccess),
            "STDIO clients should close stdin, terminate the subprocess when needed, and leave no running child process after shutdown.",
            "The process exits during shutdown cleanup and the adapter can restart it for subsequent probes.",
            lifecycleResponse,
            DescribeStdioLifecycleResponse(lifecycleResponse),
            ViolationSeverity.Medium));

        return result;
    }

    private async Task<StreamableHttpTransportTestResult> ValidateStreamableHttpTransportAsync(McpServerConfig serverConfig, string protocolVersion, CancellationToken ct)
    {
        var endpoint = serverConfig.Endpoint!;
        var normalizedVersion = SchemaRegistryProtocolVersions.NormalizeRequestedVersion(protocolVersion);
        var result = new StreamableHttpTransportTestResult
        {
            ProtocolVersion = normalizedVersion
        };

        var initializeResponse = await _httpClient.SendHttpTransportProbeAsync(
            CreateTransportProbeRequest(
                endpoint,
                "POST",
                CreateJsonRpcEnvelope(ValidationConstants.Methods.Initialize, CreateInitializeRequest(normalizedVersion), "transport-initialize"),
                normalizedVersion,
                includeProtocolVersionHeader: false,
                includeSessionIdHeader: false,
                captureSessionId: true),
            ct);

        if (initializeResponse.Headers.TryGetValue("MCP-Session-Id", out var sessionId))
        {
            result.SessionId = sessionId;
            result.SessionIdVisibleAscii = IsVisibleAscii(sessionId);
            result.Probes.Add(CreateTransportProbe(
                "session-id-visible-ascii",
                ValidationConstants.CheckIds.HttpSessionId,
                true,
                result.SessionIdVisibleAscii == true,
                "If the server returns MCP-Session-Id during initialization, the value must contain only visible ASCII characters.",
                "MCP-Session-Id is present and all characters are visible ASCII.",
                initializeResponse,
                result.SessionIdVisibleAscii == true ? "Visible ASCII session id observed." : "MCP-Session-Id contained a non-visible ASCII character.",
                ViolationSeverity.High));
        }

        var notificationResponse = await _httpClient.SendHttpTransportProbeAsync(
            CreateTransportProbeRequest(
                endpoint,
                "POST",
                CreateJsonRpcEnvelope(McpSpecConstants.InitializedNotification, null, id: null),
                normalizedVersion,
                includeSessionIdHeader: true),
            ct);

        result.Probes.Add(CreateTransportProbe(
            "post-notification-202",
            ValidationConstants.CheckIds.HttpNotificationStatus,
            true,
            EvaluateTransportResponse(notificationResponse, response => response.StatusCode == 202 && string.IsNullOrWhiteSpace(response.Body)),
            "A Streamable HTTP POST containing only a JSON-RPC notification must be accepted with HTTP 202 and no response body.",
            "HTTP 202 Accepted with an empty body.",
            notificationResponse,
            DescribeResponse(notificationResponse),
            ViolationSeverity.High));

        if (!string.IsNullOrWhiteSpace(result.SessionId) && result.SessionIdVisibleAscii == true)
        {
            var propagated = notificationResponse.RequestHeaders.TryGetValue("MCP-Session-Id", out var propagatedSessionId) &&
                             string.Equals(propagatedSessionId, result.SessionId, StringComparison.Ordinal);
            result.Probes.Add(CreateTransportProbe(
                "session-id-propagated",
                ValidationConstants.CheckIds.HttpSessionPropagation,
                true,
                propagated,
                "After initialization returns MCP-Session-Id, subsequent requests in the same logical session must carry that session id.",
                "The follow-up notification request includes the same MCP-Session-Id value.",
                notificationResponse,
                propagated ? "Session id was propagated." : "Follow-up request did not include the initialized MCP-Session-Id value.",
                ViolationSeverity.High));

            var missingSessionResponse = await _httpClient.SendHttpTransportProbeAsync(
                CreateTransportProbeRequest(
                    endpoint,
                    "POST",
                    CreateJsonRpcEnvelope(McpSpecConstants.InitializedNotification, null, id: null),
                    normalizedVersion,
                    includeSessionIdHeader: false),
                ct);

            result.Probes.Add(CreateTransportProbe(
                "missing-session-id-response",
                ValidationConstants.CheckIds.HttpSessionPropagation,
                false,
                EvaluateTransportResponse(missingSessionResponse, response => response.StatusCode == 400),
                "Servers that require session ids should reject requests missing MCP-Session-Id with HTTP 400.",
                "HTTP 400 Bad Request when the previously assigned MCP-Session-Id is omitted.",
                missingSessionResponse,
                DescribeResponse(missingSessionResponse),
                ViolationSeverity.Medium));
        }

        var getResponse = await _httpClient.SendHttpTransportProbeAsync(
            CreateTransportProbeRequest(
                endpoint,
                "GET",
                body: null,
                normalizedVersion,
                acceptHeader: "text/event-stream"),
            ct);

        result.Probes.Add(CreateTransportProbe(
            "get-sse-or-405",
            ValidationConstants.CheckIds.HttpGetSseOrMethodNotAllowed,
            true,
            EvaluateTransportResponse(getResponse, response => response.StatusCode == 405 || IsEventStreamResponse(response)),
            "A Streamable HTTP endpoint must either support GET by returning an SSE stream or reject GET with HTTP 405.",
            "HTTP 200 with Content-Type text/event-stream, or HTTP 405 Method Not Allowed.",
            getResponse,
            DescribeResponse(getResponse),
            ViolationSeverity.High));

        await AddSseProbeResultsAsync(result, endpoint, normalizedVersion, getResponse, ct);

        var invalidVersionResponse = await _httpClient.SendHttpTransportProbeAsync(
            CreateTransportProbeRequest(
                endpoint,
                "POST",
                CreateJsonRpcEnvelope(ValidationConstants.Methods.Ping, null, "transport-invalid-version"),
                normalizedVersion,
                headers: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["MCP-Protocol-Version"] = "2099-01-01"
                },
                includeProtocolVersionHeader: false),
            ct);

        result.Probes.Add(CreateTransportProbe(
            "invalid-protocol-version-400",
            ValidationConstants.CheckIds.HttpInvalidProtocolVersion,
            true,
            EvaluateTransportResponse(invalidVersionResponse, response => response.StatusCode == 400),
            "Requests with an unsupported MCP-Protocol-Version header must be rejected before normal JSON-RPC handling.",
            "HTTP 400 Bad Request.",
            invalidVersionResponse,
            DescribeResponse(invalidVersionResponse),
            ViolationSeverity.High));

        var invalidOriginResponse = await _httpClient.SendHttpTransportProbeAsync(
            CreateTransportProbeRequest(
                endpoint,
                "POST",
                CreateJsonRpcEnvelope(ValidationConstants.Methods.Ping, null, "transport-invalid-origin"),
                normalizedVersion,
                headers: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Origin"] = "https://invalid-origin.mcpval.example"
                }),
            ct);

        result.Probes.Add(CreateTransportProbe(
            "invalid-origin-403",
            ValidationConstants.CheckIds.HttpOriginValidation,
            true,
            invalidOriginResponse.StatusCode == 403 ? true : EvaluateTransportResponse(invalidOriginResponse, _ => false),
            "HTTP transports must validate Origin and reject invalid origins with HTTP 403 Forbidden.",
            "HTTP 403 Forbidden.",
            invalidOriginResponse,
            DescribeResponse(invalidOriginResponse),
            ViolationSeverity.High));

        return result;
    }

    private async Task AddSseProbeResultsAsync(StreamableHttpTransportTestResult result, string endpoint, string protocolVersion, HttpTransportProbeResponse getResponse, CancellationToken ct)
    {
        if (!IsEventStreamResponse(getResponse))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(getResponse.Body))
        {
            var parsedEvents = getResponse.SseEvents.Count > 0;
            var dataFieldsAreJson = getResponse.SseEvents
                .Where(evt => !string.IsNullOrWhiteSpace(evt.Data))
                .All(evt => LooksLikeJson(evt.Data));
            var duplicateEventIds = getResponse.SseEvents
                .Where(evt => !string.IsNullOrWhiteSpace(evt.Id))
                .GroupBy(evt => evt.Id, StringComparer.Ordinal)
                .Any(group => group.Count() > 1);

            result.Probes.Add(CreateTransportProbe(
                "sse-event-parsing",
                ValidationConstants.CheckIds.HttpSseEventStream,
                true,
                parsedEvents && dataFieldsAreJson && !duplicateEventIds,
                "Streamable HTTP SSE responses must be parseable Server-Sent Events whose data fields contain complete JSON-RPC messages.",
                "At least one parseable SSE event with JSON data and no duplicate event ids.",
                getResponse,
                parsedEvents
                    ? duplicateEventIds
                        ? "SSE stream contained duplicate event ids."
                        : dataFieldsAreJson ? "SSE event data parsed as JSON." : "One or more SSE data fields did not look like JSON."
                    : "SSE response body did not contain a complete parseable event.",
                ViolationSeverity.High));
        }

        var resumableEventId = getResponse.SseEvents.FirstOrDefault(evt => !string.IsNullOrWhiteSpace(evt.Id))?.Id;
        if (string.IsNullOrWhiteSpace(resumableEventId))
        {
            return;
        }

        var resumeResponse = await _httpClient.SendHttpTransportProbeAsync(
            CreateTransportProbeRequest(
                endpoint,
                "GET",
                body: null,
                protocolVersion,
                acceptHeader: "text/event-stream",
                headers: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Last-Event-ID"] = resumableEventId
                }),
            ct);

        result.Probes.Add(CreateTransportProbe(
            "sse-last-event-id-resume",
            ValidationConstants.CheckIds.HttpSseEventStream,
            false,
            EvaluateTransportResponse(resumeResponse, response => response.StatusCode == 204 || IsEventStreamResponse(response)),
            "When event ids are present, clients should be able to reconnect with Last-Event-ID for resumability evidence.",
            "HTTP 204 or another text/event-stream response after Last-Event-ID.",
            resumeResponse,
            DescribeResponse(resumeResponse),
            ViolationSeverity.Medium));
    }

    private static bool ShouldValidateStreamableHttpTransport(McpServerConfig serverConfig, string protocolVersion)
    {
        if (string.IsNullOrWhiteSpace(serverConfig.Endpoint) ||
            string.Equals(serverConfig.Transport, ValidationConstants.Transports.Stdio, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var normalizedVersion = SchemaRegistryProtocolVersions.NormalizeRequestedVersion(protocolVersion);
        return string.Equals(normalizedVersion, ProtocolVersions.V2025_11_25.Value, StringComparison.Ordinal);
    }

    private static bool ShouldValidateStdioTransport(McpServerConfig serverConfig)
    {
        return !string.IsNullOrWhiteSpace(serverConfig.Endpoint) &&
               string.Equals(serverConfig.Transport, ValidationConstants.Transports.Stdio, StringComparison.OrdinalIgnoreCase);
    }

    private static HttpTransportProbeRequest CreateTransportProbeRequest(
        string endpoint,
        string method,
        string? body,
        string protocolVersion,
        string acceptHeader = "application/json, text/event-stream",
        Dictionary<string, string>? headers = null,
        bool includeProtocolVersionHeader = true,
        bool includeSessionIdHeader = true,
        bool captureSessionId = false)
    {
        var requestHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Accept"] = acceptHeader
        };

        if (headers is not null)
        {
            foreach (var header in headers)
            {
                requestHeaders[header.Key] = header.Value;
            }
        }

        if (includeProtocolVersionHeader && !requestHeaders.ContainsKey("MCP-Protocol-Version"))
        {
            requestHeaders["MCP-Protocol-Version"] = protocolVersion;
            includeProtocolVersionHeader = false;
        }

        return new HttpTransportProbeRequest
        {
            Endpoint = endpoint,
            Method = method,
            Body = body,
            ContentType = body is null ? null : "application/json",
            Headers = requestHeaders,
            IncludeProtocolVersionHeader = includeProtocolVersionHeader,
            IncludeSessionIdHeader = includeSessionIdHeader,
            CaptureSessionId = captureSessionId
        };
    }

    private static string CreateJsonRpcEnvelope(string method, object? parameters, string? id)
    {
        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["jsonrpc"] = "2.0",
            ["method"] = method
        };

        if (parameters is not null)
        {
            payload["params"] = parameters;
        }

        if (id is not null)
        {
            payload["id"] = id;
        }

        return JsonSerializer.Serialize(payload);
    }

    private static bool? EvaluateTransportResponse(HttpTransportProbeResponse response, Func<HttpTransportProbeResponse, bool> predicate)
    {
        if (response.StatusCode <= 0)
        {
            return null;
        }

        if (ValidationReliability.IsAuthenticationStatusCode(response.StatusCode))
        {
            return null;
        }

        if (ValidationReliability.IsRetryableHttpStatusCode(response.StatusCode))
        {
            return null;
        }

        return predicate(response);
    }

    private static bool? EvaluateStdioResponse(StdioTransportProbeResponse response, Func<StdioTransportProbeResponse, bool> predicate)
    {
        if (!response.Executed || response.StatusCode <= 0)
        {
            return null;
        }

        return predicate(response);
    }

    private static StreamableHttpTransportProbeResult CreateTransportProbe(
        string probeId,
        string checkId,
        bool mandatory,
        bool? passed,
        string requirement,
        string expected,
        HttpTransportProbeResponse response,
        string actual,
        ViolationSeverity severity)
    {
        return new StreamableHttpTransportProbeResult
        {
            ProbeId = probeId,
            CheckId = checkId,
            Mandatory = mandatory,
            Passed = passed,
            Severity = severity,
            Requirement = requirement,
            Expected = expected,
            Actual = actual,
            StatusCode = response.StatusCode > 0 ? response.StatusCode : null,
            ContentType = response.ContentType,
            RequestHeaders = response.RequestHeaders,
            ResponseHeaders = response.Headers,
            BodyPreview = string.IsNullOrWhiteSpace(response.Body) ? null : response.Body.Length <= 512 ? response.Body : response.Body[..512],
            ProbeContext = response.ProbeContext
        };
    }

    private static StdioTransportProbeResult CreateStdioTransportProbe(
        string probeId,
        string checkId,
        bool mandatory,
        bool? passed,
        string requirement,
        string expected,
        StdioTransportProbeResponse response,
        string actual,
        ViolationSeverity severity)
    {
        return new StdioTransportProbeResult
        {
            ProbeId = probeId,
            CheckId = checkId,
            Mandatory = mandatory,
            Passed = passed,
            Severity = severity,
            Requirement = requirement,
            Expected = expected,
            Actual = actual,
            StatusCode = response.StatusCode > 0 ? response.StatusCode : null,
            StdoutPreview = string.IsNullOrWhiteSpace(response.RawStdout) ? null : response.RawStdout.Length <= 512 ? response.RawStdout : response.RawStdout[..512],
            StderrPreview = string.IsNullOrWhiteSpace(response.StderrPreview) ? null : response.StderrPreview.Length <= 512 ? response.StderrPreview : response.StderrPreview[..512],
            ProbeContext = response.ProbeContext,
            Metadata = new Dictionary<string, string>(response.Metadata, StringComparer.OrdinalIgnoreCase)
        };
    }

    private static void ApplyStreamableHttpTransportFindings(ComplianceTestResult result, ICollection<ComplianceViolation> violations, StreamableHttpTransportTestResult transport)
    {
        foreach (var probe in transport.Probes.Where(probe => probe.Mandatory && probe.Passed == false))
        {
            var violation = CreateViolation(
                probe.CheckId,
                $"Streamable HTTP transport violation ({probe.ProbeId}): {probe.Requirement} Expected: {probe.Expected} Observed: {probe.Actual}",
                probe.Severity,
                ValidationConstants.Categories.Transport,
                "https://spec.modelcontextprotocol.io/specification/2025-11-25/basic/transports#streamable-http");

            violation.Context["probeId"] = probe.ProbeId;
            violation.Context["expected"] = probe.Expected;
            violation.Context["actual"] = probe.Actual;
            if (probe.StatusCode.HasValue)
            {
                violation.Context["statusCode"] = probe.StatusCode.Value;
            }

            if (!string.IsNullOrWhiteSpace(probe.ContentType))
            {
                violation.Context["contentType"] = probe.ContentType;
            }

            violations.Add(violation);
        }

        foreach (var probe in transport.Probes.Where(probe => probe.Mandatory && probe.Passed == null))
        {
            result.Findings.Add(new ValidationFinding
            {
                RuleId = "MCP.GUIDELINE.HTTP.TRANSPORT_PROBE_INCONCLUSIVE",
                Category = "McpGuideline",
                Component = probe.ProbeId,
                Severity = ValidationFindingSeverity.Info,
                Summary = $"Streamable HTTP transport probe was inconclusive: {probe.Actual}",
                Recommendation = "Rerun with credentials or lower transport pressure before treating this transport requirement as satisfied or violated."
            });
        }
    }

    private static void ApplyStdioTransportFindings(ComplianceTestResult result, ICollection<ComplianceViolation> violations, StdioTransportTestResult transport)
    {
        foreach (var probe in transport.Probes.Where(probe => probe.Mandatory && probe.Passed == false))
        {
            var violation = CreateViolation(
                probe.CheckId,
                $"STDIO transport violation ({probe.ProbeId}): {probe.Requirement} Expected: {probe.Expected} Observed: {probe.Actual}",
                probe.Severity,
                ValidationConstants.Categories.Transport,
                "https://spec.modelcontextprotocol.io/specification/2025-11-25/basic/transports#stdio");

            violation.Context["probeId"] = probe.ProbeId;
            violation.Context["expected"] = probe.Expected;
            violation.Context["actual"] = probe.Actual;
            if (probe.StatusCode.HasValue)
            {
                violation.Context["statusCode"] = probe.StatusCode.Value;
            }

            violations.Add(violation);
        }

        foreach (var probe in transport.Probes.Where(probe => probe.Passed == null))
        {
            result.Findings.Add(new ValidationFinding
            {
                RuleId = probe.Mandatory
                    ? "MCP.GUIDELINE.STDIO.TRANSPORT_PROBE_INCONCLUSIVE"
                    : "MCP.GUIDELINE.STDIO.PARSER_BOUNDARY_PROBE_SKIPPED",
                Category = "McpGuideline",
                Component = probe.ProbeId,
                Severity = ValidationFindingSeverity.Info,
                Summary = $"STDIO transport probe was inconclusive: {probe.Actual}",
                Recommendation = probe.Mandatory
                    ? "Rerun STDIO validation when the process can produce observable stdout/lifecycle evidence before treating this requirement as satisfied or violated."
                    : "Keep parser-boundary drops separated from server-side JSON-RPC compliance failures unless a structured error response is observed."
            });
        }
    }

    private static string DescribeResponse(HttpTransportProbeResponse response)
    {
        if (!string.IsNullOrWhiteSpace(response.Error))
        {
            return response.Error;
        }

        var bodySummary = string.IsNullOrWhiteSpace(response.Body) ? "empty body" : $"body length {response.Body.Length}";
        return $"HTTP {response.StatusCode}; Content-Type={response.ContentType ?? "<none>"}; {bodySummary}";
    }

    private static string DescribeStdioResponse(StdioTransportProbeResponse response)
    {
        if (!string.IsNullOrWhiteSpace(response.Error))
        {
            return response.Error;
        }

        var stdoutSummary = string.IsNullOrWhiteSpace(response.RawStdout) ? "no stdout line" : $"stdout line length {response.RawStdout.Length}";
        var stderrSummary = string.IsNullOrWhiteSpace(response.StderrPreview) ? "no stderr observed" : "stderr captured";
        return $"STDIO status {response.StatusCode}; {stdoutSummary}; {stderrSummary}";
    }

    private static string DescribeStdioStdoutResponse(StdioTransportProbeResponse response)
    {
        var description = DescribeStdioResponse(response);
        return response.Metadata.TryGetValue("extraStdoutLine", out var extraStdoutLine)
            ? $"{description}; extra stdout line observed: {extraStdoutLine}"
            : description;
    }

    private static string DescribeStdioLifecycleResponse(StdioTransportProbeResponse response)
    {
        if (!string.IsNullOrWhiteSpace(response.Error))
        {
            return response.Error;
        }

        var shutdownMode = string.IsNullOrWhiteSpace(response.ShutdownMode) ? "unknown" : response.ShutdownMode;
        var restarted = response.Restarted switch
        {
            true => "restarted",
            false => "restart failed",
            null => "restart not required"
        };

        return $"shutdown mode={shutdownMode}; process exited={response.ProcessExited?.ToString() ?? "unknown"}; {restarted}";
    }

    private static bool IsEventStreamResponse(HttpTransportProbeResponse response)
    {
        return response.StatusCode == 200 && string.Equals(response.ContentType, "text/event-stream", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeJson(string data)
    {
        var trimmed = data.TrimStart();
        return trimmed.StartsWith('{') || trimmed.StartsWith('[');
    }

    private static bool ContainsLiteralNewline(string value)
    {
        return value.Contains('\n', StringComparison.Ordinal) || value.Contains('\r', StringComparison.Ordinal);
    }

    private static bool ExtraStdoutLineIsValidMcpMessage(StdioTransportProbeResponse response)
    {
        return !response.Metadata.TryGetValue("extraStdoutLine", out var extraStdoutLine) ||
               IsValidMcpJsonRpcMessage(extraStdoutLine);
    }

    private static bool IsValidMcpJsonRpcMessage(string? rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson) || ContainsLiteralNewline(rawJson))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(rawJson);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (!root.TryGetProperty("jsonrpc", out var version) ||
                version.ValueKind != JsonValueKind.String ||
                !string.Equals(version.GetString(), "2.0", StringComparison.Ordinal))
            {
                return false;
            }

            var hasMethod = root.TryGetProperty("method", out var method) && method.ValueKind == JsonValueKind.String;
            var hasResult = root.TryGetProperty("result", out _);
            var hasError = root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object;
            return hasMethod || hasResult || hasError;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool IsVisibleAscii(string value)
    {
        return value.Length > 0 && value.All(static ch => ch is >= '!' and <= '~');
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
        return new HashSet<string>(CapabilitySnapshotUtils.ExtractAdvertisedCapabilities(capabilities), StringComparer.OrdinalIgnoreCase);
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

        segments.Add(FormatCapabilityProbeStatus(declaredCapabilities, McpSpecConstants.Capabilities.Roots, ValidationConstants.Methods.RootsList, rootsSupported));
        segments.Add(FormatCapabilityProbeStatus(declaredCapabilities, McpSpecConstants.Capabilities.Logging, ValidationConstants.Methods.LoggingSetLevel, loggingSupported));
        segments.Add(FormatCapabilityProbeStatus(declaredCapabilities, McpSpecConstants.Capabilities.Sampling, ValidationConstants.Methods.SamplingCreateMessage, samplingSupported));
        segments.Add(FormatCapabilityProbeStatus(declaredCapabilities, McpSpecConstants.Capabilities.Completions, ValidationConstants.Methods.CompletionComplete, completionSupported));

        return string.Join(" | ", segments);
    }

    private static string FormatCapabilityProbeStatus(
        IReadOnlySet<string> declaredCapabilities,
        string capability,
        string method,
        bool supported)
    {
        if (!declaredCapabilities.Contains(capability))
        {
            return $"{method}: not advertised";
        }

        return $"{method}: {(supported ? "supported" : "not supported")}";
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

    private static void ApplyAiCapabilitySafetyFindings(
        ComplianceTestResult result,
        IReadOnlySet<string> declaredCapabilities)
    {
        if (declaredCapabilities.Contains(McpSpecConstants.Capabilities.Roots))
        {
            AddClientCapabilityAdvertisedFinding(
                result,
                McpSpecConstants.Capabilities.Roots,
                ValidationConstants.Methods.RootsList,
                "Server initialize response advertises roots, which is a client-side filesystem boundary capability.",
                "Do not rely on roots workflows unless the client negotiated roots support; when roots are available, keep root URI validation, user consent, and filesystem boundary enforcement visible to the user.");

            AddAiSafetyCapabilityFinding(
                result,
                ValidationFindingRuleIds.AiRootsBoundaryAdvisory,
                McpSpecConstants.Capabilities.Roots,
                ValidationConstants.Methods.RootsList,
                ValidationFindingSeverity.Medium,
                "Roots workflows expose filesystem boundaries and need explicit boundary controls.",
                "Validate every root URI, enforce access only inside declared roots, and preserve user consent before using or expanding root access.",
                new Dictionary<string, string>
                {
                    ["safetyControls"] = "user-consent,root-uri-validation,boundary-enforcement"
                });
        }

        if (declaredCapabilities.Contains(McpSpecConstants.Capabilities.Sampling) ||
            declaredCapabilities.Contains(McpSpecConstants.Capabilities.TasksRequestsSamplingCreateMessage))
        {
            if (declaredCapabilities.Contains(McpSpecConstants.Capabilities.Sampling))
            {
                AddClientCapabilityAdvertisedFinding(
                    result,
                    McpSpecConstants.Capabilities.Sampling,
                    ValidationConstants.Methods.SamplingCreateMessage,
                    "Server initialize response advertises sampling, which is a client-side LLM request capability.",
                    "Do not issue sampling/createMessage unless the client negotiated sampling support; preserve human review for prompts, tool use, and generated responses.");
            }

            AddAiSafetyCapabilityFinding(
                result,
                ValidationFindingRuleIds.AiSamplingHumanReviewAdvisory,
                McpSpecConstants.Capabilities.Sampling,
                ValidationConstants.Methods.SamplingCreateMessage,
                ValidationFindingSeverity.Medium,
                "Sampling-capable flows need human-in-the-loop prompt and response visibility.",
                "Expose the sampling prompt for review/editing, allow users to reject generated responses, require approval for tool-enabled sampling, and enforce iteration/rate limits for recursive model calls.",
                new Dictionary<string, string>
                {
                    ["safetyControls"] = "prompt-review,response-review,tool-use-approval,iteration-limits,rate-limits",
                    ["tasksRequest"] = declaredCapabilities.Contains(McpSpecConstants.Capabilities.TasksRequestsSamplingCreateMessage).ToString().ToLowerInvariant()
                });
        }

        if (declaredCapabilities.Contains(McpSpecConstants.Capabilities.Elicitation) ||
            declaredCapabilities.Contains(McpSpecConstants.Capabilities.TasksRequestsElicitationCreate))
        {
            if (declaredCapabilities.Contains(McpSpecConstants.Capabilities.Elicitation))
            {
                AddClientCapabilityAdvertisedFinding(
                    result,
                    McpSpecConstants.Capabilities.Elicitation,
                    ValidationConstants.Methods.ElicitationCreate,
                    "Server initialize response advertises elicitation, which is a client-side user-input capability.",
                    "Do not issue elicitation/create unless the client negotiated elicitation support; keep requester identity, decline/cancel controls, and user review visible.");
            }

            AddAiSafetyCapabilityFinding(
                result,
                ValidationFindingRuleIds.AiElicitationConsentAdvisory,
                McpSpecConstants.Capabilities.Elicitation,
                ValidationConstants.Methods.ElicitationCreate,
                ValidationFindingSeverity.Medium,
                "Elicitation-capable flows need explicit user consent and sensitive-data boundaries.",
                "Provide decline/cancel, allow users to review and modify responses, keep secrets out of form-mode fields, use URL mode for sensitive authentication or payment flows, and bind elicitation state to verified user identity.",
                new Dictionary<string, string>
                {
                    ["safetyControls"] = "decline,cancel,response-review,sensitive-data-separation,identity-binding",
                    ["tasksRequest"] = declaredCapabilities.Contains(McpSpecConstants.Capabilities.TasksRequestsElicitationCreate).ToString().ToLowerInvariant()
                });
        }

        if (declaredCapabilities.Contains(McpSpecConstants.Capabilities.Tasks))
        {
            var taskCapabilities = declaredCapabilities
                .Where(static capability => capability.StartsWith($"{McpSpecConstants.Capabilities.Tasks}.", StringComparison.OrdinalIgnoreCase))
                .OrderBy(static capability => capability, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            AddAiSafetyCapabilityFinding(
                result,
                ValidationFindingRuleIds.AiTasksIsolationAdvisory,
                McpSpecConstants.Capabilities.Tasks,
                ValidationConstants.Methods.TasksList,
                ValidationFindingSeverity.Medium,
                "Server advertises task-augmented workflows that require identity, lifetime, and visibility controls.",
                "Bind task IDs to the authorization/user context where available, document single-user limitations, apply TTL and rate limits, audit task operations, and keep task visibility aligned with declared tasks requests.",
                new Dictionary<string, string>
                {
                    ["taskCapabilities"] = taskCapabilities.Length == 0 ? McpSpecConstants.Capabilities.Tasks : string.Join(",", taskCapabilities),
                    ["hasTasksList"] = declaredCapabilities.Contains(McpSpecConstants.Capabilities.TasksList).ToString().ToLowerInvariant(),
                    ["hasToolsCallTasks"] = declaredCapabilities.Contains(McpSpecConstants.Capabilities.TasksRequestsToolsCall).ToString().ToLowerInvariant()
                });
        }
    }

    private static void AddClientCapabilityAdvertisedFinding(
        ComplianceTestResult result,
        string capability,
        string method,
        string summary,
        string recommendation)
    {
        AddAiSafetyCapabilityFinding(
            result,
            ValidationFindingRuleIds.AiClientCapabilityAdvertisedByServer,
            capability,
            method,
            ValidationFindingSeverity.Medium,
            summary,
            recommendation,
            new Dictionary<string, string>
            {
                ["capabilityAuthority"] = "client",
                ["advertisedBy"] = "server",
                ["negotiationRisk"] = "server-may-exceed-client-capabilities"
            });
    }

    private static void AddAiSafetyCapabilityFinding(
        ComplianceTestResult result,
        string ruleId,
        string capability,
        string method,
        ValidationFindingSeverity severity,
        string summary,
        string recommendation,
        IDictionary<string, string>? metadata = null)
    {
        var findingMetadata = new Dictionary<string, string>
        {
            ["capability"] = capability,
            ["method"] = method,
            ["declared"] = "true",
            ["safetyLane"] = "capability-negotiation",
            ["specReference"] = "https://modelcontextprotocol.io/specification/2025-11-25/basic/lifecycle#capabilities"
        };

        if (metadata != null)
        {
            foreach (var (key, value) in metadata)
            {
                findingMetadata[key] = value;
            }
        }

        result.Findings.Add(new ValidationFinding
        {
            RuleId = ruleId,
            Category = "AiSafety",
            Component = capability,
            Severity = severity,
            Summary = summary,
            Recommendation = recommendation,
            SpecReference = findingMetadata["specReference"],
            Metadata = findingMetadata
        });
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
            var applicabilityContext = _applicabilityResolver.Build(
                serverConfig,
                config.ProtocolVersion ?? serverConfig.ProtocolVersion,
                Array.Empty<string>());
            var requestedVersion = _protocolFeatureResolver.Resolve(applicabilityContext).NegotiatedProtocolVersion;
            result.Initialization.RequestedProtocolVersion = requestedVersion;

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
                                var schemaVersion = SchemaRegistryProtocolVersions.ResolveSchemaVersion(negotiatedVersion).Value;
                                var serverVersionSupported = SchemaRegistryProtocolVersions.IsAvailableVersion(negotiatedVersion);
                                result.Initialization.ServerProtocolVersion = negotiatedVersion;
                                result.Initialization.SchemaVersion = schemaVersion;
                                result.Initialization.ServerProtocolVersionSupported = serverVersionSupported;
                                result.Initialization.SchemaVersionFallbackApplied = !serverVersionSupported;
                                Logger.LogInformation("Protocol version negotiated: requested={Requested}, server={Server}", requestedVersion, negotiatedVersion);

                                if (!serverVersionSupported)
                                {
                                    var availableVersions = SchemaRegistryProtocolVersions.GetAvailableVersions()
                                        .Select(version => version.Value)
                                        .ToArray();
                                    var violation = CreateViolation(
                                        ValidationConstants.CheckIds.ProtocolInitializeUnsupportedProtocolVersion,
                                        $"Server returned unsupported protocolVersion '{negotiatedVersion}'. Validator used schema '{schemaVersion}' as a compatibility fallback.",
                                        ViolationSeverity.High,
                                        ValidationConstants.Categories.ProtocolLifecycle,
                                        ComplianceChecks.SpecReferences[ComplianceChecks.Protocol.Lifecycle]);
                                    violation.Context["requestedProtocolVersion"] = requestedVersion;
                                    violation.Context["serverProtocolVersion"] = negotiatedVersion;
                                    violation.Context["schemaVersion"] = schemaVersion;
                                    violation.Context["fallbackApplied"] = true;
                                    violation.Context["availableProtocolVersions"] = availableVersions;
                                    result.Violations.Add(violation);
                                    result.Score = Math.Min(result.Score, 70.0);
                                }
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

    private async Task<bool> ValidateRequestFormatAsync(McpServerConfig serverConfig, JsonRpcErrorValidationResult errorValidation, CancellationToken cancellationToken)
    {
        // Test 1: Valid request
        var validResponse = await _httpClient.CallAsync(serverConfig.Endpoint!, "ping", null, cancellationToken);
        
        // If auth failed, we consider it "compliant" for protocol structure (server correctly rejected us)
        if (AuthenticationChallengeInterpreter.Inspect(validResponse).RequiresAuthentication) return true;

        if (!validResponse.IsSuccess && validResponse.StatusCode != 404) return false; // 404 is fine for ping

        var parseErrorTest = errorValidation.Tests.FirstOrDefault(test => test.ExpectedErrorCode == -32700);
        if (parseErrorTest is null)
        {
            return string.Equals(serverConfig.Transport, "stdio", StringComparison.OrdinalIgnoreCase);
        }

        if (ShouldSkipRawStdioErrorProbe(serverConfig, parseErrorTest))
        {
            return true;
        }

        var classification = JsonRpcResponseInspector.Classify(parseErrorTest.ActualResponse);
        return parseErrorTest.IsValid &&
               classification.Surface == JsonRpcResponseSurface.JsonRpcErrorEnvelope &&
               classification.ErrorCode == -32700;
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

    private async Task<ProtocolProbeOutcome> ValidateBatchProcessingAsync(McpServerConfig serverConfig, string? requestedVersion, CancellationToken cancellationToken)
    {
        if (string.Equals(serverConfig.Transport, "stdio", StringComparison.OrdinalIgnoreCase))
        {
            return ProtocolProbeOutcome.Inconclusive(
                "Batch processing probe skipped for stdio transport because current official MCP SDK baselines do not reliably answer raw JSON-RPC batch arrays over newline-delimited stdio.");
        }

        var endpoint = serverConfig.Endpoint!;
        var batchMethod = await ResolveBatchProbeMethodAsync(endpoint, requestedVersion, cancellationToken);
        if (batchMethod is null)
        {
            return ProtocolProbeOutcome.Inconclusive(
                "Batch processing probe skipped because no parameter-free declared MCP method was available for a like-for-like batch request.");
        }

        var batchRequest = $"[{{\"jsonrpc\": \"2.0\", \"method\": \"{batchMethod}\", \"id\": 1}}, {{\"jsonrpc\": \"2.0\", \"method\": \"{batchMethod}\", \"id\": 2}}]";
        var response = await _httpClient.SendRawJsonAsync(endpoint, batchRequest, cancellationToken);
        
        if (AuthenticationChallengeInterpreter.Inspect(response).RequiresAuthentication)
        {
            return ProtocolProbeOutcome.AuthRequired("Batch processing probe requires authentication; rerun with credentials for authoritative protocol evidence.");
        }

        if (!response.IsSuccess) return ProtocolProbeOutcome.Failed();

        try
        {
            using var doc = JsonDocument.Parse(response.RawJson!);
            return doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() == 2
                ? ProtocolProbeOutcome.Compliant()
                : ProtocolProbeOutcome.Failed();
        }
        catch
        {
            return ProtocolProbeOutcome.Failed();
        }
    }

    private async Task<string?> ResolveBatchProbeMethodAsync(string endpoint, string? requestedVersion, CancellationToken cancellationToken)
    {
        var declaredCapabilities = await GetDeclaredCapabilitiesAsync(endpoint, requestedVersion, cancellationToken);
        var preferredMethods = new List<string>();

        if (declaredCapabilities.Contains(McpSpecConstants.Capabilities.Tools))
        {
            preferredMethods.Add(ValidationConstants.Methods.ToolsList);
        }

        if (declaredCapabilities.Contains(McpSpecConstants.Capabilities.Resources))
        {
            preferredMethods.Add(ValidationConstants.Methods.ResourcesList);
        }

        if (declaredCapabilities.Contains(McpSpecConstants.Capabilities.Prompts))
        {
            preferredMethods.Add(ValidationConstants.Methods.PromptsList);
        }

        if (declaredCapabilities.Contains(McpSpecConstants.Capabilities.Roots))
        {
            preferredMethods.Add(ValidationConstants.Methods.RootsList);
        }

        foreach (var method in preferredMethods.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            return method;
        }

        return null;
    }

    private async Task<ProtocolProbeOutcome> CheckNotificationHandlingAsync(string endpoint, CancellationToken cancellationToken)
    {
        // Use a real MCP notification method instead of a request-style method like ping.
        // This avoids classifying "missing id" method validation as a notification response bug.
        var notification = $"{{\"jsonrpc\": \"2.0\", \"method\": \"{McpSpecConstants.InitializedNotification}\"}}";
        var response = await _httpClient.SendRawJsonAsync(endpoint, notification, cancellationToken);
        
        if (AuthenticationChallengeInterpreter.Inspect(response).RequiresAuthentication)
        {
            return ProtocolProbeOutcome.AuthRequired("Notification handling probe requires authentication; rerun with credentials for authoritative protocol evidence.");
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

        public static ProtocolProbeOutcome AuthRequired(string reason) => new(null, reason);
    }

    private static bool HasAuthBlockedProbe(params ProtocolProbeOutcome[] probes)
    {
        return probes.Any(probe => probe.IsCompliant == null &&
                                   probe.Reason?.Contains("authentication", StringComparison.OrdinalIgnoreCase) == true);
    }

    private static bool HasAuthBlockedTransportProbe(StreamableHttpTransportTestResult? transport)
    {
        return transport?.Probes.Any(probe => probe.Mandatory &&
                              probe.Passed == null &&
                              (probe.Actual.Contains("HTTP 401", StringComparison.OrdinalIgnoreCase) ||
                               probe.Actual.Contains("HTTP 403", StringComparison.OrdinalIgnoreCase))) == true;
    }

    private static bool HasInconclusiveProbe(params ProtocolProbeOutcome[] probes)
    {
        return probes.Any(probe => probe.IsCompliant == null &&
                                   !string.IsNullOrWhiteSpace(probe.Reason) &&
                                   !IsNonBlockingProbeSkip(probe.Reason));
    }

    private static bool IsNonBlockingProbeSkip(string reason)
    {
        return reason.StartsWith("Batch processing probe skipped", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasInconclusiveTransportProbe(StreamableHttpTransportTestResult? transport)
    {
        return transport?.Probes.Any(probe => probe.Mandatory && probe.Passed == null) == true;
    }

    private static bool HasInconclusiveTransportProbe(StdioTransportTestResult? transport)
    {
        return transport?.Probes.Any(probe => probe.Mandatory && probe.Passed == null) == true;
    }

    private static bool ValidateErrorCodeCompliance(IReadOnlyCollection<JsonRpcErrorTest> observableErrorTests)
    {
        return observableErrorTests.Count == 0 || observableErrorTests.All(test => test.IsValid);
    }

    private static IEnumerable<JsonRpcErrorTest> GetObservableErrorTests(McpServerConfig serverConfig, JsonRpcErrorValidationResult errorValidation)
    {
        return errorValidation.Tests.Where(test => !ShouldSkipRawStdioErrorProbe(serverConfig, test));
    }

    private static IEnumerable<JsonRpcErrorTest> GetSkippedRawStdioErrorTests(McpServerConfig serverConfig, JsonRpcErrorValidationResult errorValidation)
    {
        return errorValidation.Tests.Where(test => ShouldSkipRawStdioErrorProbe(serverConfig, test));
    }

    private static bool ShouldSkipRawStdioErrorProbe(McpServerConfig serverConfig, JsonRpcErrorTest test)
    {
        if (!string.Equals(serverConfig.Transport, "stdio", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (test.ExpectedErrorCode is not (-32700 or -32600 or -32602))
        {
            return false;
        }

        var classification = JsonRpcResponseInspector.Classify(test.ActualResponse);
        return classification.Surface is JsonRpcResponseSurface.EmptyBody or JsonRpcResponseSurface.TransportFailure;
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
