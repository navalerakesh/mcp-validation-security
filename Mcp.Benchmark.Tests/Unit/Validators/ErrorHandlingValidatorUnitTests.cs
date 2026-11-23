using FluentAssertions;
using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Constants;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Infrastructure.Validators;
using Mcp.Benchmark.Infrastructure.Scenarios;
using Mcp.Benchmark.Core.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace Mcp.Benchmark.Tests.Unit;

public class ErrorHandlingValidatorUnitTests
{
    [Theory]
    [InlineData("complete", ValidationCoverageStatus.Covered, AssuranceClaimOutcome.Satisfied)]
    [InlineData("missing", ValidationCoverageStatus.Unavailable, AssuranceClaimOutcome.Incomplete)]
    [InlineData("unknown", ValidationCoverageStatus.Unavailable, AssuranceClaimOutcome.Incomplete)]
    [InlineData("duplicate", ValidationCoverageStatus.Inconclusive, AssuranceClaimOutcome.Incomplete)]
    [InlineData("contradictory", ValidationCoverageStatus.Inconclusive, AssuranceClaimOutcome.Incomplete)]
    [InlineData("disabled", ValidationCoverageStatus.Skipped, AssuranceClaimOutcome.Incomplete)]
    [InlineData("not-executed", ValidationCoverageStatus.Unavailable, AssuranceClaimOutcome.Incomplete)]
    [InlineData("recovery-failed", ValidationCoverageStatus.Covered, AssuranceClaimOutcome.Violated)]
    public async Task TimeoutObligation_ShouldDetectMissingEvidenceDespitePassingCategory(
        string variant, ValidationCoverageStatus expectedCoverage, AssuranceClaimOutcome expectedClaim)
    {
        var client = new Mock<IMcpHttpClient>(MockBehavior.Strict);
        var probe = CreateResilienceProbeResult(ValidationConstants.ProbeIds.TimeoutHandling);
        if (variant == "not-executed") probe.Executed = false;
        if (variant == "recovery-failed") probe.GracefulRecovery = false;
        client.Setup(http => http.ProbeTimeoutRecoveryAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(probe);
        var configuration = new McpValidatorConfiguration();
        configuration.Server.Endpoint = "https://example.invalid/mcp";
        configuration.Server.Transport = "http";
        configuration.Validation.Categories.ErrorHandling = new ErrorHandlingConfig
        {
            TestTimeoutHandling = true, TestConnectionInterruption = false, TestInvalidMethods = false,
            TestMalformedJson = false, TestGracefulDegradation = false
        };
        var measured = await new ErrorHandlingValidator(Mock.Of<ILogger<ErrorHandlingValidator>>(), client.Object)
            .ValidateErrorHandlingAsync(configuration.Server, configuration.Validation.Categories.ErrorHandling);
        measured.Status.Should().Be(variant switch
        {
            "not-executed" => TestStatus.Skipped,
            "recovery-failed" => TestStatus.Failed,
            _ => TestStatus.Passed
        });
        if (variant == "missing") measured.RecoveryResults.Clear();
        if (variant == "unknown") measured.ErrorScenarioResults[0].ErrorType = "unknown";
        if (variant == "duplicate") measured.ErrorScenarioResults.Add(measured.ErrorScenarioResults[0]);
        if (variant == "contradictory") measured.RecoveryResults[0].RecoverySuccessful = false;
        if (variant == "disabled") configuration.Validation.Categories.ErrorHandling.TestTimeoutHandling = false;
        var result = new ValidationResult { ErrorHandling = measured };
        var scenario = new BuiltInObservedSurfaceScenarioPack().GetScenarios()
            .Single(candidate => candidate.Descriptor.Key.Value == ValidationConstants.ProbeIds.TimeoutHandling);
        var observed = await scenario.ExecuteAsync(new ValidationScenarioContext
        {
            ServerConfig = configuration.Server, ValidationConfiguration = configuration,
            ValidationResult = result, ApplicabilityContext = new ValidationApplicabilityContext
            {
                NegotiatedProtocolVersion = "2025-11-25", SchemaVersion = "2025-11-25",
                Transport = "http", AccessMode = "public"
            }
        }, CancellationToken.None);
        result.Evidence.Coverage.AddRange(observed.Coverage);
        result.Assurance = AssuranceAssessmentEngine.Evaluate(result, AssuranceProfileCatalog.Comprehensive);

        observed.Coverage.Should().ContainSingle(coverage => coverage.Status == expectedCoverage);
        result.Assurance.Claims.Should().ContainSingle(claim => claim.Subject == ValidationConstants.ProbeIds.TimeoutHandling &&
            claim.RequirementLevel == AssuranceRequirementLevel.OperationalObjective && claim.Outcome == expectedClaim);
        if (variant == "recovery-failed")
            result.Assurance.Assessments.OperationalReadiness.Status.Should().Be(OperationalReadinessStatus.NotReady);
        else if (variant != "complete")
            result.Assurance.Assessments.OperationalReadiness.Status.Should().Be(OperationalReadinessStatus.Incomplete);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ValidateErrorHandlingAsync_MixedUnavailableAndObservedResponse_ShouldPreserveBothMeanings(bool observedValid)
    {
        var client = new Mock<IMcpHttpClient>(MockBehavior.Strict);
        client.Setup(http => http.ValidateErrorCodesAsync(It.IsAny<string>(),
                JsonRpcErrorProbeSelection.ParseError | JsonRpcErrorProbeSelection.MethodNotFound, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcErrorValidationResult
            {
                Tests =
                [
                    new JsonRpcErrorTest { ExpectedErrorCode = -32700, ActualResponse = new JsonRpcResponse { StatusCode = -1 } },
                    CreateErrorTest("method", -32601, observedValid, 200, observedValid
                        ? """{"jsonrpc":"2.0","id":1,"error":{"code":-32601,"message":"Method not found"}}"""
                        : """{"jsonrpc":"2.0","id":1,"error":{"code":-32000,"message":"Server error"}}""")
                ]
            });
        var validator = new ErrorHandlingValidator(Mock.Of<ILogger<ErrorHandlingValidator>>(), client.Object);

        var result = await validator.ValidateErrorHandlingAsync(
            new McpServerConfig { Endpoint = "https://example.invalid/mcp", Transport = "http" },
            new ErrorHandlingConfig { TestInvalidMethods = true, TestMalformedJson = true, TestGracefulDegradation = false,
                TestTimeoutHandling = false, TestConnectionInterruption = false });

        result.Status.Should().Be(observedValid ? TestStatus.Inconclusive : TestStatus.Failed);
        result.ErrorScenariosTestCount.Should().Be(1);
        result.ErrorScenariosHandledCorrectly.Should().Be(observedValid ? 1 : 0);
        result.Findings.Count(finding => finding.EffectiveSource == ValidationRuleSource.Spec).Should().Be(observedValid ? 0 : 1);
        result.Findings.Should().Contain(finding => finding.RuleId == "MCP.GUIDELINE.ERROR_HANDLING.PROBE_NOT_EXECUTED");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ValidateErrorHandlingAsync_WithNoExecutableProbes_ShouldNotContactTarget(bool customScenario)
    {
        var httpClient = new Mock<IMcpHttpClient>(MockBehavior.Strict);
        var validator = new ErrorHandlingValidator(Mock.Of<ILogger<ErrorHandlingValidator>>(), httpClient.Object);
        var configuration = new ErrorHandlingConfig
        {
            TestInvalidMethods = false,
            TestMalformedJson = false,
            TestGracefulDegradation = false,
            TestTimeoutHandling = false,
            TestConnectionInterruption = false
        };
        if (customScenario) configuration.CustomErrorScenarios.Add(new());

        var result = await validator.ValidateErrorHandlingAsync(
            new McpServerConfig { Endpoint = "https://example.invalid/mcp", Transport = "http" }, configuration);

        httpClient.Invocations.Should().BeEmpty();
        result.Status.Should().Be(TestStatus.Skipped);
        result.ErrorScenariosTestCount.Should().Be(0);
        if (customScenario)
            result.Findings.Should().Contain(finding => finding.RuleId == "MCP.GUIDELINE.ERROR_HANDLING.CUSTOM_SCENARIOS_NOT_EXECUTED");
    }

    [Fact]
    public async Task ValidateErrorHandlingAsync_WithSupportedProbeResults_ShouldPassAllConfiguredScenarios()
    {
        var httpClient = new Mock<IMcpHttpClient>();
        httpClient
            .Setup(client => client.ValidateErrorCodesAsync(It.IsAny<string>(), It.IsAny<JsonRpcErrorProbeSelection>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcErrorValidationResult
            {
                Tests = new List<JsonRpcErrorTest>
                {
                    CreateErrorTest("Parse Error", -32700, true, 400, "{\"jsonrpc\":\"2.0\",\"error\":{\"code\":-32700,\"message\":\"Parse error\"},\"id\":null}"),
                    CreateErrorTest("Invalid Request", -32600, true, 400, "{\"jsonrpc\":\"2.0\",\"error\":{\"code\":-32600,\"message\":\"Invalid Request\"},\"id\":1}"),
                    CreateErrorTest("Method Not Found", -32601, true, 200, "{\"jsonrpc\":\"2.0\",\"error\":{\"code\":-32601,\"message\":\"Method not found\"},\"id\":1}")
                }
            });
        httpClient
            .Setup(client => client.ProbeTimeoutRecoveryAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateResilienceProbeResult("timeout-handling"));
        httpClient
            .Setup(client => client.ProbeConnectionInterruptionRecoveryAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateResilienceProbeResult("connection-interruption"));

        var validator = new ErrorHandlingValidator(new Mock<ILogger<ErrorHandlingValidator>>().Object, httpClient.Object);

        var result = await validator.ValidateErrorHandlingAsync(
            new McpServerConfig { Endpoint = "http://localhost:8080/mcp", Transport = "http" },
            new ErrorHandlingConfig(),
            CancellationToken.None);

        result.Status.Should().Be(TestStatus.Passed);
        result.Score.Should().Be(100);
        result.ErrorScenariosTestCount.Should().Be(5);
        result.ErrorScenariosHandledCorrectly.Should().Be(5);
        result.RecoveryResults.Should().HaveCount(2);
        result.Findings.Should().NotContain(finding => finding.RuleId == "MCP.GUIDELINE.ERROR_HANDLING.PROBE_NOT_EXECUTED");
        result.Message.Should().Be("Validated 5 error scenario(s); 5 handled correctly.");
        var configuration = new McpValidatorConfiguration();
        var validation = new ValidationResult { ErrorHandling = result };
        var context = new ValidationScenarioContext
        {
            ServerConfig = configuration.Server, ValidationConfiguration = configuration, ValidationResult = validation,
            ApplicabilityContext = new ValidationApplicabilityContext
            {
                NegotiatedProtocolVersion = "2025-11-25", SchemaVersion = "2025-11-25", Transport = "http", AccessMode = "public"
            }
        };
        var identities = new[] { ValidationConstants.ProbeIds.TimeoutHandling, ValidationConstants.ProbeIds.ConnectionInterruption,
            ValidationConstants.ProbeIds.InvalidMethod, ValidationConstants.ProbeIds.MalformedJson, ValidationConstants.ProbeIds.InvalidRequest };
        var scenarios = new BuiltInObservedSurfaceScenarioPack().GetScenarios()
            .Where(scenario => identities.Contains(scenario.Descriptor.Key.Value)).ToArray();
        scenarios.Should().HaveCount(5);
        foreach (var scenario in scenarios)
        {
            var complete = await scenario.ExecuteAsync(context, CancellationToken.None);
            complete.Coverage.Should().ContainSingle(coverage => coverage.ObservedOutcome == ValidationOutcome.Succeeded);
            var record = result.ErrorScenarioResults.Single(item => item.ErrorType == scenario.Descriptor.Key.Value);
            result.ErrorScenarioResults.Remove(record);
            var missing = await scenario.ExecuteAsync(context, CancellationToken.None);
            result.Status.Should().Be(TestStatus.Passed);
            missing.Coverage.Should().ContainSingle(coverage => coverage.Scope == scenario.Descriptor.Key.Value &&
                coverage.Status == ValidationCoverageStatus.Unavailable);
            validation.Evidence.Coverage.Clear();
            validation.Evidence.Coverage.AddRange(missing.Coverage);
            AssuranceAssessmentEngine.Evaluate(validation, AssuranceProfileCatalog.Comprehensive)
                .Claims.Should().Contain(claim => claim.Subject == scenario.Descriptor.Key.Value && claim.Outcome == AssuranceClaimOutcome.Incomplete);
            result.ErrorScenarioResults.Add(record);
        }
    }

    [Fact]
    public async Task ValidateErrorHandlingAsync_WithNonStandardErrorResponse_ShouldFailWithSpecFinding()
    {
        var httpClient = new Mock<IMcpHttpClient>();
        httpClient
            .Setup(client => client.ValidateErrorCodesAsync(It.IsAny<string>(), It.IsAny<JsonRpcErrorProbeSelection>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcErrorValidationResult
            {
                Tests = new List<JsonRpcErrorTest>
                {
                    CreateErrorTest("Method Not Found", -32601, false, 200, "{\"jsonrpc\":\"2.0\",\"error\":{\"code\":-32000,\"message\":\"Server error\"},\"id\":1}")
                }
            });

        var validator = new ErrorHandlingValidator(new Mock<ILogger<ErrorHandlingValidator>>().Object, httpClient.Object);

        var result = await validator.ValidateErrorHandlingAsync(
            new McpServerConfig { Endpoint = "http://localhost:8080/mcp", Transport = "http" },
            new ErrorHandlingConfig
            {
                TestInvalidMethods = true,
                TestMalformedJson = false,
                TestConnectionInterruption = false,
                TestTimeoutHandling = false,
                TestGracefulDegradation = false
            },
            CancellationToken.None);

        result.Status.Should().Be(TestStatus.Failed);
        result.Score.Should().Be(0);
        result.ErrorScenariosTestCount.Should().Be(1);
        result.ErrorScenariosHandledCorrectly.Should().Be(0);
        result.Findings.Should().Contain(finding =>
            finding.RuleId == "MCP.ERROR_HANDLING.NON_STANDARD_ERROR_RESPONSE" &&
            finding.Source == ValidationRuleSource.Spec &&
            finding.Recommendation == ComplianceRecommendations.GetRecommendation(ValidationConstants.CheckIds.ProtocolErrorHandling, "Error Code Violation"));
    }

    [Fact]
    public async Task ValidateErrorHandlingAsync_WithHttpFrontDoorRejection_ShouldClassifySurfaceInFindingMetadata()
    {
        var httpClient = new Mock<IMcpHttpClient>();
        httpClient
            .Setup(client => client.ValidateErrorCodesAsync(It.IsAny<string>(), It.IsAny<JsonRpcErrorProbeSelection>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcErrorValidationResult
            {
                Tests = new List<JsonRpcErrorTest>
                {
                    CreateErrorTest("Parse Error", -32700, false, 500, "Internal server error while parsing request")
                }
            });

        var validator = new ErrorHandlingValidator(new Mock<ILogger<ErrorHandlingValidator>>().Object, httpClient.Object);

        var result = await validator.ValidateErrorHandlingAsync(
            new McpServerConfig { Endpoint = "http://localhost:8080/mcp", Transport = "http" },
            new ErrorHandlingConfig
            {
                TestInvalidMethods = false,
                TestMalformedJson = true,
                TestConnectionInterruption = false,
                TestTimeoutHandling = false,
                TestGracefulDegradation = false
            },
            CancellationToken.None);

        result.Findings.Should().Contain(finding =>
            finding.RuleId == "MCP.ERROR_HANDLING.NON_STANDARD_ERROR_RESPONSE" &&
            finding.Metadata.ContainsKey("responseSurface") &&
            finding.Metadata["responseSurface"] == "http-front-door" &&
            finding.Summary.Contains("HTTP front door", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ValidateErrorHandlingAsync_WithHttp4xxFrontDoorOnParseError_ShouldRetainUnknownProtocolEvidence()
    {
        var httpClient = new Mock<IMcpHttpClient>();
        httpClient
            .Setup(client => client.ValidateErrorCodesAsync(It.IsAny<string>(), It.IsAny<JsonRpcErrorProbeSelection>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcErrorValidationResult
            {
                Tests = new List<JsonRpcErrorTest>
                {
                    CreateErrorTest("Parse Error", -32700, false, 400, "{\"error\":\"malformed payload\"}")
                }
            });

        var validator = new ErrorHandlingValidator(new Mock<ILogger<ErrorHandlingValidator>>().Object, httpClient.Object);

        var result = await validator.ValidateErrorHandlingAsync(
            new McpServerConfig { Endpoint = "http://localhost:8080/mcp", Transport = "http" },
            new ErrorHandlingConfig
            {
                TestInvalidMethods = false,
                TestMalformedJson = true,
                TestConnectionInterruption = false,
                TestTimeoutHandling = false,
                TestGracefulDegradation = false
            },
            CancellationToken.None);

        result.Status.Should().Be(TestStatus.Inconclusive);
        result.ErrorScenariosHandledCorrectly.Should().Be(0);
        result.ErrorScenarioResults.Should().BeEmpty();
        result.Findings.Should().Contain(finding =>
            finding.RuleId == "MCP.ERROR_HANDLING.NON_STANDARD_ERROR_RESPONSE" &&
            finding.Source == ValidationRuleSource.Guideline &&
            finding.Severity == ValidationFindingSeverity.Info &&
            finding.Summary.Contains("was not observed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ValidateErrorHandlingAsync_WithStdioRawProbeDrops_ShouldSkipMalformedEnvelopeScenarios()
    {
        var httpClient = new Mock<IMcpHttpClient>();
        httpClient
            .Setup(client => client.ValidateErrorCodesAsync(It.IsAny<string>(), It.IsAny<JsonRpcErrorProbeSelection>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcErrorValidationResult
            {
                Tests = new List<JsonRpcErrorTest>
                {
                    new()
                    {
                        Name = "Parse Error",
                        ExpectedErrorCode = -32700,
                        IsValid = false,
                        ActualResponse = new JsonRpcResponse
                        {
                            StatusCode = 500,
                            IsSuccess = false,
                            Error = "No response"
                        }
                    },
                    new()
                    {
                        Name = "Invalid Request",
                        ExpectedErrorCode = -32600,
                        IsValid = false,
                        ActualResponse = new JsonRpcResponse
                        {
                            StatusCode = 500,
                            IsSuccess = false,
                            Error = "No response"
                        }
                    },
                    CreateErrorTest("Method Not Found", -32601, true, 200, "{\"jsonrpc\":\"2.0\",\"error\":{\"code\":-32601,\"message\":\"Method not found\"},\"id\":1}")
                }
            });

        var validator = new ErrorHandlingValidator(new Mock<ILogger<ErrorHandlingValidator>>().Object, httpClient.Object);

        var result = await validator.ValidateErrorHandlingAsync(
            new McpServerConfig { Endpoint = "npx -y mcpval-localmcp", Transport = "stdio" },
            new ErrorHandlingConfig
            {
                TestInvalidMethods = true,
                TestMalformedJson = true,
                TestConnectionInterruption = false,
                TestTimeoutHandling = false,
                TestGracefulDegradation = true
            },
            CancellationToken.None);

        result.Status.Should().Be(TestStatus.Passed);
        result.ErrorScenariosTestCount.Should().Be(1);
        result.ErrorScenariosHandledCorrectly.Should().Be(1);
        result.Findings.Should().Contain(finding =>
            finding.RuleId == "MCP.GUIDELINE.ERROR_HANDLING.PROBE_NOT_EXECUTED" &&
            finding.Component == "malformed-json");
        result.Findings.Should().Contain(finding =>
            finding.RuleId == "MCP.GUIDELINE.ERROR_HANDLING.PROBE_NOT_EXECUTED" &&
            finding.Component == "invalid-request");
    }

    [Fact]
    public async Task ValidateErrorHandlingAsync_WithFailedResilienceProbe_ShouldRecordRecoveryFailure()
    {
        var httpClient = new Mock<IMcpHttpClient>();
        httpClient
            .Setup(client => client.ValidateErrorCodesAsync(It.IsAny<string>(), It.IsAny<JsonRpcErrorProbeSelection>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcErrorValidationResult());
        httpClient
            .Setup(client => client.ProbeTimeoutRecoveryAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransportResilienceProbeResult
            {
                ProbeId = "timeout-handling",
                DisplayName = "Timeout Handling Recovery",
                Executed = true,
                FailureObserved = true,
                GracefulRecovery = false,
                ActualOutcome = "Validator cancelled the request, but the follow-up MCP probe never recovered.",
                FailureElapsedMs = 25,
                RecoveryElapsedMs = 300,
                RecoveryResponse = new JsonRpcResponse { StatusCode = -1, IsSuccess = false, Error = "No recovery response" }
            });

        var validator = new ErrorHandlingValidator(new Mock<ILogger<ErrorHandlingValidator>>().Object, httpClient.Object);

        var result = await validator.ValidateErrorHandlingAsync(
            new McpServerConfig { Endpoint = "http://localhost:8080/mcp", Transport = "http" },
            new ErrorHandlingConfig
            {
                TestInvalidMethods = false,
                TestMalformedJson = false,
                TestConnectionInterruption = false,
                TestTimeoutHandling = true,
                TestGracefulDegradation = false
            },
            CancellationToken.None);

        result.Status.Should().Be(TestStatus.Failed);
        result.ErrorScenariosTestCount.Should().Be(1);
        result.RecoveryResults.Should().ContainSingle(recovery =>
            recovery.FailureType == "timeout-handling" &&
            !recovery.RecoverySuccessful);
        result.Findings.Should().Contain(finding =>
            finding.RuleId == "MCP.ERROR_HANDLING.RESILIENCE_PROBE_FAILED" &&
            finding.Component == "timeout-handling");
    }

    private static JsonRpcErrorTest CreateErrorTest(string name, int expectedErrorCode, bool isValid, int statusCode, string rawJson)
    {
        return new JsonRpcErrorTest
        {
            Name = name,
            ExpectedErrorCode = expectedErrorCode,
            IsValid = isValid,
            ActualResponse = new JsonRpcResponse
            {
                StatusCode = statusCode,
                IsSuccess = false,
                RawJson = rawJson,
                ElapsedMs = 12
            }
        };
    }

    private static TransportResilienceProbeResult CreateResilienceProbeResult(string probeId)
    {
        return new TransportResilienceProbeResult
        {
            ProbeId = probeId,
            DisplayName = probeId,
            Executed = true,
            FailureObserved = true,
            GracefulRecovery = true,
            ActualOutcome = $"{probeId} probe observed the expected transport failure and the follow-up MCP probe recovered cleanly.",
            FailureElapsedMs = 20,
            RecoveryElapsedMs = 15,
            RecoveryResponse = new JsonRpcResponse
            {
                StatusCode = 400,
                IsSuccess = false,
                RawJson = "{\"jsonrpc\":\"2.0\",\"error\":{\"code\":-32601,\"message\":\"Method not found\"},\"id\":1}"
            }
        };
    }
}