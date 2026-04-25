using System.Text.Json;
using Microsoft.Extensions.Logging;
using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Constants;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Infrastructure.Utilities;

namespace Mcp.Benchmark.Infrastructure.Validators;

/// <summary>
/// Validates protocol error handling using the transport's malformed-request and resilience probes.
/// </summary>
public class ErrorHandlingValidator : BaseValidator<ErrorHandlingValidator>, IErrorHandlingValidator
{
    private readonly IMcpHttpClient _httpClient;

    public ErrorHandlingValidator(ILogger<ErrorHandlingValidator> logger, IMcpHttpClient httpClient)
        : base(logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public async Task<ErrorHandlingTestResult> ValidateErrorHandlingAsync(McpServerConfig serverConfig, ErrorHandlingConfig config, CancellationToken cancellationToken = default)
    {
        return await ExecuteValidationAsync(serverConfig, "Error Handling Validation", async ct =>
        {
            var result = new ErrorHandlingTestResult();
            var errorCodeValidation = await _httpClient.ValidateErrorCodesAsync(serverConfig.Endpoint!, ct);
            var unsupportedProbeCount = 0;

            if (config.TestInvalidMethods)
            {
                AddScenarioResult(
                    result,
                    errorCodeValidation,
                    expectedErrorCode: -32601,
                    scenarioName: "Invalid Method Call",
                    errorType: "invalid-method",
                    expectedResponse: "JSON-RPC error code -32601 (Method not found).",
                    unsupportedMessage: "Invalid-method probing is enabled, but the active transport did not expose a method-not-found error test.");
            }

            if (config.TestMalformedJson)
            {
                AddScenarioResult(
                    result,
                    errorCodeValidation,
                    expectedErrorCode: -32700,
                    scenarioName: "Malformed JSON Payload",
                    errorType: "malformed-json",
                    expectedResponse: "JSON-RPC error code -32700 (Parse error).",
                    unsupportedMessage: "Malformed JSON probing is enabled, but the active transport did not expose a parse-error test.");
            }

            if (config.TestGracefulDegradation)
            {
                AddScenarioResult(
                    result,
                    errorCodeValidation,
                    expectedErrorCode: -32600,
                    scenarioName: "Graceful Degradation On Invalid Request",
                    errorType: "invalid-request",
                    expectedResponse: "JSON-RPC error code -32600 (Invalid request).",
                    unsupportedMessage: "Graceful-degradation probing is enabled, but the active transport did not expose an invalid-request error test.");
            }

            if (config.TestTimeoutHandling)
            {
                AddResilienceProbeResult(
                    result,
                    await _httpClient.ProbeTimeoutRecoveryAsync(serverConfig.Endpoint!, ct),
                    scenarioName: "Timeout Handling Recovery",
                    errorType: "timeout-handling",
                    expectedResponse: "Validator-induced timeout should surface a transport failure and the endpoint should respond to a follow-up recovery probe.");
            }

            if (config.TestConnectionInterruption)
            {
                AddResilienceProbeResult(
                    result,
                    await _httpClient.ProbeConnectionInterruptionRecoveryAsync(serverConfig.Endpoint!, ct),
                    scenarioName: "Connection Interruption Recovery",
                    errorType: "connection-interruption",
                    expectedResponse: "Validator-induced transport interruption should not leave the endpoint unresponsive to a follow-up recovery probe.");
            }

            if (config.CustomErrorScenarios.Count > 0)
            {
                unsupportedProbeCount += config.CustomErrorScenarios.Count;
                result.Findings.Add(new ValidationFinding
                {
                    RuleId = "MCP.GUIDELINE.ERROR_HANDLING.CUSTOM_SCENARIOS_NOT_EXECUTED",
                    Category = "ErrorHandling",
                    Component = "custom-scenarios",
                    Severity = ValidationFindingSeverity.Info,
                    Summary = $"{config.CustomErrorScenarios.Count} custom error scenario(s) were configured, but free-form scenario execution is not implemented for the current transport contract.",
                    Recommendation = "Extend the error-handling transport contract with explicit scenario payloads before treating custom error scenarios as covered.",
                    Source = ValidationRuleSource.Guideline
                });
            }

            result.ErrorScenariosTestCount = result.ErrorScenarioResults.Count;
            result.ErrorScenariosHandledCorrectly = result.ErrorScenarioResults.Count(scenario => scenario.HandledCorrectly);

            if (result.ErrorScenariosTestCount == 0)
            {
                result.Status = TestStatus.Skipped;
                result.Score = 0;
                result.Message = unsupportedProbeCount > 0
                    ? "Error-handling validation was configured, but none of the requested probes are executable with the current transport abstraction."
                    : "Error-handling validation did not execute any scenarios.";
                return result;
            }

            result.Score = Math.Round(result.ErrorScenariosHandledCorrectly / (double)result.ErrorScenariosTestCount * 100.0, 2);
            result.Status = result.ErrorScenariosHandledCorrectly == result.ErrorScenariosTestCount
                ? TestStatus.Passed
                : TestStatus.Failed;

            result.Message = BuildResultMessage(result, unsupportedProbeCount);
            return result;
        }, cancellationToken);
    }

    private static void AddScenarioResult(
        ErrorHandlingTestResult result,
        JsonRpcErrorValidationResult validation,
        int expectedErrorCode,
        string scenarioName,
        string errorType,
        string expectedResponse,
        string unsupportedMessage)
    {
        var errorTest = validation.Tests.FirstOrDefault(test => test.ExpectedErrorCode == expectedErrorCode);
        if (errorTest == null)
        {
            result.Findings.Add(CreateUnsupportedProbeFinding(errorType, unsupportedMessage));
            return;
        }

        var actualResponse = DescribeResponse(errorTest.ActualResponse);
        var scenarioResult = new ErrorScenarioResult
        {
            ScenarioName = scenarioName,
            ErrorType = errorType,
            HandledCorrectly = errorTest.IsValid,
            ExpectedResponse = expectedResponse,
            ActualResponse = actualResponse,
            ErrorHandlingTimeMs = errorTest.ActualResponse?.ElapsedMs ?? 0,
            GracefulRecovery = errorTest.IsValid
        };

        if (!errorTest.IsValid)
        {
            var classification = JsonRpcResponseInspector.Classify(errorTest.ActualResponse);
            scenarioResult.AdditionalIssues.Add("Server did not return the expected structured JSON-RPC error response.");
            scenarioResult.AdditionalIssues.Add(DescribeSurfaceIssue(classification, expectedErrorCode));
            result.Findings.Add(new ValidationFinding
            {
                RuleId = "MCP.ERROR_HANDLING.NON_STANDARD_ERROR_RESPONSE",
                Category = "Protocol",
                Component = errorType,
                Severity = ValidationFindingSeverity.High,
                Summary = BuildScenarioSummary(scenarioName, expectedErrorCode, classification),
                Recommendation = ComplianceRecommendations.GetRecommendation(ValidationConstants.CheckIds.ProtocolErrorHandling, "Error Code Violation"),
                Source = ValidationRuleSource.Spec,
                SpecReference = ComplianceChecks.SpecReferences[ComplianceChecks.Protocol.ErrorHandling],
                Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["expectedErrorCode"] = expectedErrorCode.ToString(),
                    ["actualResponse"] = actualResponse,
                    ["responseSurface"] = ToSurfaceLabel(classification.Surface),
                    ["httpStatus"] = classification.StatusCode.ToString(),
                    ["contentType"] = classification.ContentType ?? string.Empty,
                    ["observedErrorCode"] = classification.ErrorCode?.ToString() ?? string.Empty
                }
            });
        }

        result.ErrorScenarioResults.Add(scenarioResult);
    }

    private static string BuildScenarioSummary(string scenarioName, int expectedErrorCode, JsonRpcResponseClassification classification)
    {
        return classification.Surface switch
        {
            JsonRpcResponseSurface.HttpFrontDoorRejection =>
                $"Error-handling scenario '{scenarioName}' was rejected at the HTTP front door instead of returning JSON-RPC error code {expectedErrorCode}.",
            JsonRpcResponseSurface.JsonRpcErrorEnvelope when classification.ErrorCode is int observedCode && observedCode != expectedErrorCode =>
                $"Error-handling scenario '{scenarioName}' returned JSON-RPC error code {observedCode} instead of the expected {expectedErrorCode}.",
            JsonRpcResponseSurface.NonJsonRpcJson =>
                $"Error-handling scenario '{scenarioName}' returned JSON that was not a valid JSON-RPC error envelope for expected code {expectedErrorCode}.",
            _ =>
                $"Error-handling scenario '{scenarioName}' did not return the expected JSON-RPC error code {expectedErrorCode}."
        };
    }

    private static string DescribeSurfaceIssue(JsonRpcResponseClassification classification, int expectedErrorCode)
    {
        return classification.Surface switch
        {
            JsonRpcResponseSurface.HttpFrontDoorRejection =>
                "The endpoint rejected the payload at the HTTP front door instead of surfacing a JSON-RPC error envelope.",
            JsonRpcResponseSurface.JsonRpcErrorEnvelope when classification.ErrorCode is int observedCode && observedCode != expectedErrorCode =>
                $"The endpoint returned a structured JSON-RPC error, but with code {observedCode} instead of {expectedErrorCode}.",
            JsonRpcResponseSurface.NonJsonRpcJson =>
                "The endpoint returned JSON content, but not a valid JSON-RPC error envelope.",
            JsonRpcResponseSurface.EmptyBody =>
                "The endpoint returned no body for an error scenario that should have produced a JSON-RPC error envelope.",
            JsonRpcResponseSurface.TransportFailure =>
                "The validator observed a transport failure before a JSON-RPC error envelope was returned.",
            _ =>
                "The endpoint did not produce a standards-compliant JSON-RPC error envelope."
        };
    }

    private static string ToSurfaceLabel(JsonRpcResponseSurface surface)
    {
        return surface switch
        {
            JsonRpcResponseSurface.JsonRpcErrorEnvelope => "jsonrpc-envelope",
            JsonRpcResponseSurface.HttpFrontDoorRejection => "http-front-door",
            JsonRpcResponseSurface.AuthenticationChallenge => "authentication-challenge",
            JsonRpcResponseSurface.TransportFailure => "transport-failure",
            JsonRpcResponseSurface.EmptyBody => "empty-body",
            JsonRpcResponseSurface.NonJsonRpcJson => "non-jsonrpc-json",
            _ => "unknown"
        };
    }

    private static void AddResilienceProbeResult(
        ErrorHandlingTestResult result,
        TransportResilienceProbeResult probe,
        string scenarioName,
        string errorType,
        string expectedResponse)
    {
        if (!probe.Executed)
        {
            result.Findings.Add(CreateUnsupportedProbeFinding(
                errorType,
                probe.ActualOutcome ?? $"{scenarioName} is configured, but the active transport could not execute the resilience probe."));
            return;
        }

        var actualOutcome = FirstNonEmpty(
            probe.ActualOutcome,
            DescribeResponse(probe.FailureResponse),
            "No transport outcome captured.");

        var scenarioResult = new ErrorScenarioResult
        {
            ScenarioName = scenarioName,
            ErrorType = errorType,
            HandledCorrectly = probe.HandledCorrectly,
            ExpectedResponse = expectedResponse,
            ActualResponse = actualOutcome,
            ErrorHandlingTimeMs = probe.FailureElapsedMs,
            GracefulRecovery = probe.GracefulRecovery,
            AdditionalIssues = probe.Notes.ToList()
        };

        if (!probe.FailureObserved)
        {
            scenarioResult.AdditionalIssues.Add("Expected transport failure was not observed during the resilience probe.");
        }

        if (!probe.GracefulRecovery)
        {
            scenarioResult.AdditionalIssues.Add("Endpoint did not prove recovery after the injected transport failure.");
        }

        result.ErrorScenarioResults.Add(scenarioResult);
        result.RecoveryResults.Add(new RecoveryTestResult
        {
            ScenarioName = scenarioName,
            FailureType = errorType,
            RecoverySuccessful = probe.GracefulRecovery,
            RecoveryTimeMs = probe.RecoveryElapsedMs,
            DataIntegrityMaintained = probe.GracefulRecovery,
            ServiceAvailabilityRestored = probe.GracefulRecovery,
            RecoveryLosses = probe.GracefulRecovery
                ? new List<string>()
                : new List<string> { FirstNonEmpty(DescribeResponse(probe.RecoveryResponse), probe.ActualOutcome, "Recovery probe did not produce a usable response.") }
        });

        if (probe.HandledCorrectly)
        {
            return;
        }

        result.Findings.Add(new ValidationFinding
        {
            RuleId = "MCP.ERROR_HANDLING.RESILIENCE_PROBE_FAILED",
            Category = "ErrorHandling",
            Component = errorType,
            Severity = ValidationFindingSeverity.High,
            Summary = $"Error-handling resilience probe '{scenarioName}' did not recover cleanly after the injected transport failure.",
            Recommendation = "Ensure timed-out or interrupted requests are cleaned up so a follow-up MCP request can still complete without restarting or recreating transport state.",
            Source = ValidationRuleSource.Guideline,
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["failureObserved"] = probe.FailureObserved.ToString(),
                ["gracefulRecovery"] = probe.GracefulRecovery.ToString(),
                ["actualOutcome"] = actualOutcome
            }
        });
    }

    private static ValidationFinding CreateUnsupportedProbeFinding(string component, string summary)
    {
        return new ValidationFinding
        {
            RuleId = "MCP.GUIDELINE.ERROR_HANDLING.PROBE_NOT_EXECUTED",
            Category = "ErrorHandling",
            Component = component,
            Severity = ValidationFindingSeverity.Info,
            Summary = summary,
            Recommendation = "Extend the transport harness if this probe must become a first-class validation requirement.",
            Source = ValidationRuleSource.Guideline
        };
    }

    private static string BuildResultMessage(ErrorHandlingTestResult result, int unsupportedProbeCount)
    {
        var handledSummary = $"Validated {result.ErrorScenariosTestCount} error scenario(s); {result.ErrorScenariosHandledCorrectly} handled correctly.";
        if (unsupportedProbeCount == 0)
        {
            return handledSummary;
        }

        return $"{handledSummary} {unsupportedProbeCount} configured probe(s) were not executable with the current transport abstraction.";
    }

    private static string DescribeResponse(JsonRpcResponse? response)
    {
        if (response == null)
        {
            return "No response received.";
        }

        var summary = $"HTTP {response.StatusCode}";
        if (TryReadJsonRpcError(response.RawJson, out var code, out var message))
        {
            return string.IsNullOrWhiteSpace(message)
                ? $"{summary}; JSON-RPC error {code}."
                : $"{summary}; JSON-RPC error {code}: {message}";
        }

        if (!string.IsNullOrWhiteSpace(response.Error))
        {
            return $"{summary}; {response.Error}";
        }

        if (!string.IsNullOrWhiteSpace(response.RawJson))
        {
            var rawPreview = response.RawJson.Length > 200 ? response.RawJson[..200] : response.RawJson;
            return $"{summary}; {rawPreview}";
        }

        return summary;
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return string.Empty;
    }

    private static bool TryReadJsonRpcError(string? rawJson, out int code, out string? message)
    {
        code = 0;
        message = null;

        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(rawJson);
            if (!document.RootElement.TryGetProperty("error", out var errorElement) ||
                !errorElement.TryGetProperty("code", out var codeElement))
            {
                return false;
            }

            code = codeElement.GetInt32();
            if (errorElement.TryGetProperty("message", out var messageElement) &&
                messageElement.ValueKind == JsonValueKind.String)
            {
                message = messageElement.GetString();
            }

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}