using FluentAssertions;
using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Constants;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Infrastructure.Validators;
using Microsoft.Extensions.Logging;
using Moq;

namespace Mcp.Benchmark.Tests.Unit;

public class ErrorHandlingValidatorUnitTests
{
    [Fact]
    public async Task ValidateErrorHandlingAsync_WithSupportedProbeResults_ShouldPassAllConfiguredScenarios()
    {
        var httpClient = new Mock<IMcpHttpClient>();
        httpClient
            .Setup(client => client.ValidateErrorCodesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
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
    }

    [Fact]
    public async Task ValidateErrorHandlingAsync_WithNonStandardErrorResponse_ShouldFailWithSpecFinding()
    {
        var httpClient = new Mock<IMcpHttpClient>();
        httpClient
            .Setup(client => client.ValidateErrorCodesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
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
    public async Task ValidateErrorHandlingAsync_WithFailedResilienceProbe_ShouldRecordRecoveryFailure()
    {
        var httpClient = new Mock<IMcpHttpClient>();
        httpClient
            .Setup(client => client.ValidateErrorCodesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
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