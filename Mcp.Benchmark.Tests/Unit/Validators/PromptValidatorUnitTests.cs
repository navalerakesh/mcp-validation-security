using FluentAssertions;
using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Infrastructure.Validators;
using Mcp.Compliance.Spec;
using Microsoft.Extensions.Logging;
using Moq;

namespace Mcp.Benchmark.Tests.Unit;

public class PromptValidatorUnitTests
{
    [Fact]
    public async Task ValidatePromptDiscoveryAsync_WithPassingPrompts_ShouldPopulatePassCounters()
    {
        var httpClient = new Mock<IMcpHttpClient>();
        httpClient
            .Setup(client => client.CallAsync(
                It.IsAny<string>(),
                "prompts/list",
                It.IsAny<object?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse
            {
                StatusCode = 200,
                IsSuccess = true,
                RawJson = "{\"jsonrpc\":\"2.0\",\"result\":{\"prompts\":[{\"name\":\"demo-prompt\",\"description\":\"Demonstration prompt\"}]},\"id\":1}"
            });

        var contentSafety = new Mock<IContentSafetyAnalyzer>();
        contentSafety
            .Setup(analyzer => analyzer.AnalyzePrompt(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int>()))
            .Returns(new List<ContentSafetyFinding>());

        var validator = new PromptValidator(
            new Mock<ILogger<PromptValidator>>().Object,
            httpClient.Object,
            new Mock<ISchemaValidator>().Object,
            new Mock<ISchemaRegistry>().Object,
            contentSafety.Object);

        var result = await validator.ValidatePromptDiscoveryAsync(
            new McpServerConfig { Endpoint = "http://localhost:8080/mcp", Transport = "http" },
            new PromptTestingConfig { TestPromptExecution = false },
            CancellationToken.None);

        result.Status.Should().Be(TestStatus.Passed);
        result.PromptsDiscovered.Should().Be(1);
        result.PromptsTestPassed.Should().Be(1);
        result.PromptsTestFailed.Should().Be(0);
        result.Score.Should().Be(100);
    }

    [Fact]
    public async Task ValidatePromptDiscoveryAsync_WithNoPrompts_ShouldReportExplicitNoPromptCoverageMessage()
    {
        var httpClient = new Mock<IMcpHttpClient>();
        httpClient
            .Setup(client => client.CallAsync(
                It.IsAny<string>(),
                "prompts/list",
                It.IsAny<object?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse
            {
                StatusCode = 200,
                IsSuccess = true,
                RawJson = "{\"jsonrpc\":\"2.0\",\"result\":{\"prompts\":[]},\"id\":1}"
            });

        var validator = new PromptValidator(
            new Mock<ILogger<PromptValidator>>().Object,
            httpClient.Object,
            new Mock<ISchemaValidator>().Object,
            new Mock<ISchemaRegistry>().Object,
            new Mock<IContentSafetyAnalyzer>().Object);

        var result = await validator.ValidatePromptDiscoveryAsync(
            new McpServerConfig { Endpoint = "http://localhost:8080/mcp", Transport = "http" },
            new PromptTestingConfig { TestPromptExecution = false },
            CancellationToken.None);

        result.Status.Should().Be(TestStatus.Passed);
        result.PromptsDiscovered.Should().Be(0);
        result.PromptsTestPassed.Should().Be(0);
        result.PromptsTestFailed.Should().Be(0);
        result.Score.Should().Be(100);
        result.Issues.Should().Contain("✅ COMPLIANT: No prompts were advertised; no prompt executions were required");
    }

    [Fact]
    public async Task ValidatePromptDiscoveryAsync_WithMethodNotFound_ShouldTreatPromptSurfaceAsNotAdvertised()
    {
        var httpClient = new Mock<IMcpHttpClient>();
        httpClient
            .Setup(client => client.CallAsync(
                It.IsAny<string>(),
                "prompts/list",
                It.IsAny<object?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse
            {
                StatusCode = 400,
                IsSuccess = false,
                Error = "Method not found",
                RawJson = "{\"jsonrpc\":\"2.0\",\"error\":{\"code\":-32601,\"message\":\"Method not found\"},\"id\":1}"
            });

        var validator = new PromptValidator(
            new Mock<ILogger<PromptValidator>>().Object,
            httpClient.Object,
            new Mock<ISchemaValidator>().Object,
            new Mock<ISchemaRegistry>().Object,
            new Mock<IContentSafetyAnalyzer>().Object);

        var result = await validator.ValidatePromptDiscoveryAsync(
            new McpServerConfig { Endpoint = "stdio-server", Transport = "stdio" },
            new PromptTestingConfig { TestPromptExecution = false },
            CancellationToken.None);

        result.Status.Should().Be(TestStatus.Passed);
        result.PromptsDiscovered.Should().Be(0);
        result.Score.Should().Be(100);
        result.Issues.Should().Contain("✅ COMPLIANT: No prompts were advertised; no prompt executions were required");
    }
}