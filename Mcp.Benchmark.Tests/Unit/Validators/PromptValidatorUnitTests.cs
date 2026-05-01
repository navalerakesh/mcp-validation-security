using FluentAssertions;
using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Constants;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Infrastructure.Validators;
using Mcp.Compliance.Spec;
using Microsoft.Extensions.Logging;
using Moq;

namespace Mcp.Benchmark.Tests.Unit;

public class PromptValidatorUnitTests
{
    [Fact]
    public async Task ValidatePromptDiscoveryAsync_WhenPromptsCapabilityIsNotAdvertised_ShouldSkipPromptProbes()
    {
        var httpClient = new Mock<IMcpHttpClient>();
        var validator = new PromptValidator(
            new Mock<ILogger<PromptValidator>>().Object,
            httpClient.Object,
            new Mock<ISchemaValidator>().Object,
            new Mock<ISchemaRegistry>().Object,
            new Mock<IContentSafetyAnalyzer>().Object);

        var result = await validator.ValidatePromptDiscoveryAsync(
            new McpServerConfig { Endpoint = "http://localhost:8080/mcp", Transport = "http" },
            new PromptTestingConfig
            {
                CapabilitySnapshot = new TransportResult<CapabilitySummary>
                {
                    IsSuccessful = true,
                    Payload = new CapabilitySummary
                    {
                        CapabilityDeclarationsAvailable = true,
                        AdvertisedCapabilities = Array.Empty<string>()
                    }
                }
            },
            CancellationToken.None);

        result.Status.Should().Be(TestStatus.Skipped);
        result.Score.Should().Be(100.0);
        result.Issues.Should().Contain("Prompts capability was not advertised during initialize; prompts/list and prompts/get probes were skipped.");

        httpClient.Verify(client => client.CallAsync(
            It.IsAny<string>(),
            "prompts/list",
            It.IsAny<object?>(),
            It.IsAny<CancellationToken>()),
            Times.Never);
    }

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

    [Fact]
    public async Task ValidatePromptDiscoveryAsync_WithInjectionProneArguments_ShouldEmitAiSafetyAdvisories()
    {
        var httpClient = new Mock<IMcpHttpClient>();
        httpClient
            .Setup(client => client.CallAsync(
                It.IsAny<string>(),
                ValidationConstants.Methods.PromptsList,
                It.IsAny<object?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse
            {
                StatusCode = 200,
                IsSuccess = true,
                RawJson = "{\"jsonrpc\":\"2.0\",\"result\":{\"prompts\":[{\"name\":\"review_code\",\"description\":\"Summarize submitted code\",\"arguments\":[{\"name\":\"code\",\"description\":\"Source code to process\",\"required\":true}]}]},\"id\":1}"
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
            new McpServerConfig { Endpoint = "https://example.test/mcp", Transport = "http" },
            new PromptTestingConfig { TestPromptExecution = false },
            CancellationToken.None);

        result.Findings.Should().Contain(finding => finding.RuleId == ValidationFindingRuleIds.PromptInjectionGuidanceMissing);
        result.Findings.Should().Contain(finding => finding.RuleId == ValidationFindingRuleIds.PromptInputOutputValidationAdvisory);
    }

    [Fact]
    public async Task ValidatePromptDiscoveryAsync_WithUnsafePromptContent_ShouldEmitStructuredFindings()
    {
        var httpClient = new Mock<IMcpHttpClient>();
        const string unsafePromptResponseJson = "{\"jsonrpc\":\"2.0\",\"result\":{\"messages\":[{\"role\":\"user\",\"content\":[{\"type\":\"image\",\"data\":\"not-base64\",\"mimeType\":\"image/png\",\"annotations\":{\"audience\":[\"model\"],\"priority\":2}},{\"type\":\"resource\",\"resource\":{\"uri\":\"file:///etc/secret.txt\",\"mimeType\":\"text/plain\",\"blob\":\"not-base64\"}}]}]},\"id\":2}";

        httpClient
            .Setup(client => client.CallAsync(
                It.IsAny<string>(),
                ValidationConstants.Methods.PromptsList,
                It.IsAny<object?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse
            {
                StatusCode = 200,
                IsSuccess = true,
                RawJson = "{\"jsonrpc\":\"2.0\",\"result\":{\"prompts\":[{\"name\":\"sensitive_context\",\"description\":\"Loads sensitive context for review\"}]},\"id\":1}"
            });

        httpClient
            .Setup(client => client.CallAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<object?>(),
                It.IsAny<AuthenticationConfig?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, string method, object? _, AuthenticationConfig? _, CancellationToken _) => method == ValidationConstants.Methods.PromptsGet
                ? new JsonRpcResponse
                {
                    StatusCode = 200,
                    IsSuccess = true,
                    RawJson = unsafePromptResponseJson
                }
                : new JsonRpcResponse
                {
                    StatusCode = 400,
                    IsSuccess = false,
                    Error = "Unexpected method"
                });

        httpClient
            .Setup(client => client.CallAsync(
                It.IsAny<string>(),
                ValidationConstants.Methods.PromptsGet,
                It.IsAny<object?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse
            {
                StatusCode = 200,
                IsSuccess = true,
                RawJson = unsafePromptResponseJson
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
            new McpServerConfig { Endpoint = "https://example.test/mcp", Transport = "http" },
            new PromptTestingConfig { TestPromptExecution = true },
            CancellationToken.None);

        result.Findings.Should().Contain(finding => finding.RuleId == ValidationFindingRuleIds.PromptAnnotationInvalid);
        result.Findings.Should().Contain(finding => finding.RuleId == ValidationFindingRuleIds.PromptContentBlobInvalidBase64);
        result.Findings.Should().Contain(finding => finding.RuleId == ValidationFindingRuleIds.PromptEmbeddedResourceSafetyAdvisory);

        var promptResult = result.PromptResults.Single(prompt => prompt.PromptName == "sensitive_context");
        promptResult.Findings.Should().Contain(finding => finding.RuleId == ValidationFindingRuleIds.PromptContentBlobInvalidBase64);
    }
}