using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Infrastructure.Validators;
using Mcp.Compliance.Spec;
using Microsoft.Extensions.Logging;
using Moq;
using FluentAssertions;
using Xunit;

namespace Mcp.Benchmark.Tests.Integration;

public class PromptValidatorIntegrationTests
{
    private readonly PromptValidator _validator;
    private readonly Mock<IMcpHttpClient> _httpClient;

    public PromptValidatorIntegrationTests()
    {
        _httpClient = new Mock<IMcpHttpClient>();
        var contentSafety = new Mock<IContentSafetyAnalyzer>();
        contentSafety.Setup(x => x.AnalyzePrompt(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()))
            .Returns(new List<ContentSafetyFinding>());

        _validator = new PromptValidator(
            new Mock<ILogger<PromptValidator>>().Object,
            _httpClient.Object,
            new Mock<ISchemaValidator>().Object,
            new Mock<ISchemaRegistry>().Object,
            contentSafety.Object);
    }

    [Fact]
    public async Task ValidatePromptDiscovery_WithAuthRequired_ShouldSkip()
    {
        var config = new McpServerConfig { Endpoint = "https://test.com/mcp", Transport = "http" };
        _httpClient.Setup(x => x.CallAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse { StatusCode = 401, IsSuccess = false, Headers = new Dictionary<string, string> { { "WWW-Authenticate", "Bearer" } } });

        var result = await _validator.ValidatePromptDiscoveryAsync(config, new PromptTestingConfig(), CancellationToken.None);

        result.Status.Should().Be(TestStatus.Skipped);
    }

    [Fact]
    public async Task ValidatePromptDiscovery_WithPrompts_ShouldParseCorrectly()
    {
        var config = new McpServerConfig { Endpoint = "https://test.com/mcp", Transport = "http" };
        _httpClient.Setup(x => x.CallAsync(It.IsAny<string>(), "prompts/list", It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse
            {
                StatusCode = 200, IsSuccess = true,
                RawJson = "{\"jsonrpc\":\"2.0\",\"result\":{\"prompts\":[{\"name\":\"code_review\",\"description\":\"Review code\",\"arguments\":[{\"name\":\"code\",\"required\":true}]}]},\"id\":1}"
            });

        var result = await _validator.ValidatePromptDiscoveryAsync(config, new PromptTestingConfig { TestPromptExecution = false }, CancellationToken.None);

        result.PromptsDiscovered.Should().Be(1);
        result.PromptResults[0].PromptName.Should().Be("code_review");
        result.PromptResults[0].MetadataValid.Should().BeTrue();
        result.PromptResults[0].ArgumentsCount.Should().Be(1);
    }

    [Fact]
    public async Task ValidatePromptDiscovery_WithMissingName_ShouldFlagInvalid()
    {
        var config = new McpServerConfig { Endpoint = "https://test.com/mcp", Transport = "http" };
        _httpClient.Setup(x => x.CallAsync(It.IsAny<string>(), "prompts/list", It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse
            {
                StatusCode = 200, IsSuccess = true,
                RawJson = "{\"jsonrpc\":\"2.0\",\"result\":{\"prompts\":[{\"description\":\"no name prompt\"}]},\"id\":1}"
            });

        var result = await _validator.ValidatePromptDiscoveryAsync(config, new PromptTestingConfig { TestPromptExecution = false }, CancellationToken.None);

        result.PromptResults[0].MetadataValid.Should().BeFalse();
        result.PromptResults[0].Issues.Should().Contain(i => i.Contains("name"));
        result.PromptResults[0].Findings.Should().Contain(f => f.RuleId == ValidationFindingRuleIds.PromptMissingName);
    }

    [Fact]
    public async Task ValidatePromptDiscovery_WithMissingDescription_ShouldEmitGuidelineFinding()
    {
        var config = new McpServerConfig { Endpoint = "https://test.com/mcp", Transport = "http" };
        _httpClient.Setup(x => x.CallAsync(It.IsAny<string>(), "prompts/list", It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse
            {
                StatusCode = 200, IsSuccess = true,
                RawJson = "{\"jsonrpc\":\"2.0\",\"result\":{\"prompts\":[{\"name\":\"summarize_repo\"}]},\"id\":1}"
            });

        var result = await _validator.ValidatePromptDiscoveryAsync(config, new PromptTestingConfig { TestPromptExecution = false }, CancellationToken.None);

        result.PromptResults[0].MetadataValid.Should().BeTrue();
        result.PromptResults[0].Findings.Should().Contain(f => f.RuleId == ValidationFindingRuleIds.PromptGuidelineDescriptionMissing);
    }

    [Fact]
    public async Task ValidatePromptDiscovery_WithUndescribedArguments_ShouldEmitGuidelineFinding()
    {
        var config = new McpServerConfig { Endpoint = "https://test.com/mcp", Transport = "http" };
        _httpClient.Setup(x => x.CallAsync(It.IsAny<string>(), "prompts/list", It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse
            {
                StatusCode = 200, IsSuccess = true,
                RawJson = "{\"jsonrpc\":\"2.0\",\"result\":{\"prompts\":[{\"name\":\"search_docs\",\"description\":\"Search docs\",\"arguments\":[{\"name\":\"query\",\"required\":true}]}]},\"id\":1}"
            });

        var result = await _validator.ValidatePromptDiscoveryAsync(config, new PromptTestingConfig { TestPromptExecution = false }, CancellationToken.None);

        result.PromptResults[0].Findings.Should().Contain(f => f.RuleId == ValidationFindingRuleIds.PromptGuidelineArgumentDescriptionMissing);
    }

    [Fact]
    public async Task ValidatePromptDiscovery_WithManyRequiredArgumentsAndNoGuidance_ShouldEmitComplexityFinding()
    {
        var config = new McpServerConfig { Endpoint = "https://test.com/mcp", Transport = "http" };
        _httpClient.Setup(x => x.CallAsync(It.IsAny<string>(), "prompts/list", It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse
            {
                StatusCode = 200, IsSuccess = true,
                RawJson = "{\"jsonrpc\":\"2.0\",\"result\":{\"prompts\":[{\"name\":\"batch_operation\",\"description\":\"Run the batch operation.\",\"arguments\":[{\"name\":\"targets\",\"required\":true,\"description\":\"Targets\"},{\"name\":\"mode\",\"required\":true,\"description\":\"Mode\"},{\"name\":\"owner\",\"required\":true,\"description\":\"Owner\"}]}]},\"id\":1}"
            });

        var result = await _validator.ValidatePromptDiscoveryAsync(config, new PromptTestingConfig { TestPromptExecution = false }, CancellationToken.None);

        result.PromptResults[0].Findings.Should().Contain(f => f.RuleId == ValidationFindingRuleIds.PromptArgumentComplexityGuidanceMissing);
    }

    [Fact]
    public async Task ValidatePromptDiscovery_WithSafetySensitivePromptAndNoWarning_ShouldEmitSafetyFinding()
    {
        var config = new McpServerConfig { Endpoint = "https://test.com/mcp", Transport = "http" };
        _httpClient.Setup(x => x.CallAsync(It.IsAny<string>(), "prompts/list", It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse
            {
                StatusCode = 200, IsSuccess = true,
                RawJson = "{\"jsonrpc\":\"2.0\",\"result\":{\"prompts\":[{\"name\":\"delete_user_data\",\"description\":\"Delete the selected user data.\"}]},\"id\":1}"
            });

        var result = await _validator.ValidatePromptDiscoveryAsync(config, new PromptTestingConfig { TestPromptExecution = false }, CancellationToken.None);

        result.PromptResults[0].Findings.Should().Contain(f => f.RuleId == ValidationFindingRuleIds.PromptSafetyGuidanceMissing);
    }

    [Fact]
    public async Task ValidatePromptDiscovery_WithSafetySensitivePromptAndConfirmationGuidance_ShouldNotEmitSafetyFinding()
    {
        var config = new McpServerConfig { Endpoint = "https://test.com/mcp", Transport = "http" };
        _httpClient.Setup(x => x.CallAsync(It.IsAny<string>(), "prompts/list", It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse
            {
                StatusCode = 200, IsSuccess = true,
                RawJson = "{\"jsonrpc\":\"2.0\",\"result\":{\"prompts\":[{\"name\":\"delete_user_data\",\"description\":\"Delete the selected user data only after human approval and confirmation.\"}]},\"id\":1}"
            });

        var result = await _validator.ValidatePromptDiscoveryAsync(config, new PromptTestingConfig { TestPromptExecution = false }, CancellationToken.None);

        result.PromptResults[0].Findings.Should().NotContain(f => f.RuleId == ValidationFindingRuleIds.PromptSafetyGuidanceMissing);
    }

    [Fact]
    public async Task ValidatePromptDiscovery_WithPromptsGet_ShouldValidateMessages()
    {
        var config = new McpServerConfig { Endpoint = "https://test.com/mcp", Transport = "http" };
        _httpClient.Setup(x => x.CallAsync(It.IsAny<string>(), "prompts/list", It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse
            {
                StatusCode = 200, IsSuccess = true,
                RawJson = "{\"jsonrpc\":\"2.0\",\"result\":{\"prompts\":[{\"name\":\"test_prompt\"}]},\"id\":1}"
            });
        _httpClient.Setup(x => x.CallAsync(It.IsAny<string>(), "prompts/get", It.IsAny<object>(), It.IsAny<AuthenticationConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse
            {
                StatusCode = 200, IsSuccess = true,
                RawJson = "{\"jsonrpc\":\"2.0\",\"result\":{\"messages\":[{\"role\":\"user\",\"content\":{\"type\":\"text\",\"text\":\"Hello\"}}]},\"id\":2}"
            });

        var result = await _validator.ValidatePromptDiscoveryAsync(config, new PromptTestingConfig { TestPromptExecution = true }, CancellationToken.None);

        result.PromptResults[0].ExecutionSuccessful.Should().BeTrue();
        result.PromptResults[0].Issues.Should().Contain(i => i.Contains("messages[] array present"));
    }

    [Fact]
    public async Task ValidatePromptDiscovery_WithMissingRole_ShouldFlagNonCompliant()
    {
        var config = new McpServerConfig { Endpoint = "https://test.com/mcp", Transport = "http" };
        _httpClient.Setup(x => x.CallAsync(It.IsAny<string>(), "prompts/list", It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse
            {
                StatusCode = 200, IsSuccess = true,
                RawJson = "{\"jsonrpc\":\"2.0\",\"result\":{\"prompts\":[{\"name\":\"bad_prompt\"}]},\"id\":1}"
            });
        _httpClient.Setup(x => x.CallAsync(It.IsAny<string>(), "prompts/get", It.IsAny<object>(), It.IsAny<AuthenticationConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse
            {
                StatusCode = 200, IsSuccess = true,
                RawJson = "{\"jsonrpc\":\"2.0\",\"result\":{\"messages\":[{\"content\":{\"type\":\"text\",\"text\":\"No role\"}}]},\"id\":2}"
            });

        var result = await _validator.ValidatePromptDiscoveryAsync(config, new PromptTestingConfig { TestPromptExecution = true }, CancellationToken.None);

        result.PromptResults[0].Issues.Should().Contain(i => i.Contains("missing 'role'"));
        result.PromptResults[0].Findings.Should().Contain(f => f.RuleId == ValidationFindingRuleIds.PromptMessageMissingRole);
    }

    [Fact]
    public async Task ValidatePromptDiscovery_WithServerError_ShouldFail()
    {
        var config = new McpServerConfig { Endpoint = "https://test.com/mcp", Transport = "http" };
        _httpClient.Setup(x => x.CallAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse { StatusCode = 500, IsSuccess = false, Error = "Server Error" });

        var result = await _validator.ValidatePromptDiscoveryAsync(config, new PromptTestingConfig(), CancellationToken.None);

        result.Status.Should().Be(TestStatus.Failed);
    }

    [Fact]
    public async Task ValidatePromptDiscovery_WithEmptyPrompts_ShouldPass()
    {
        var config = new McpServerConfig { Endpoint = "https://test.com/mcp", Transport = "http" };
        _httpClient.Setup(x => x.CallAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse
            {
                StatusCode = 200, IsSuccess = true,
                RawJson = "{\"jsonrpc\":\"2.0\",\"result\":{\"prompts\":[]},\"id\":1}"
            });

        var result = await _validator.ValidatePromptDiscoveryAsync(config, new PromptTestingConfig(), CancellationToken.None);

        result.PromptsDiscovered.Should().Be(0);
    }
}
