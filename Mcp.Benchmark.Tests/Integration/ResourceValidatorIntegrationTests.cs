using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Constants;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Infrastructure.Validators;
using Mcp.Compliance.Spec;
using Microsoft.Extensions.Logging;
using Moq;
using FluentAssertions;
using System.Text;
using Xunit;

namespace Mcp.Benchmark.Tests.Integration;

public class ResourceValidatorIntegrationTests
{
    private readonly ResourceValidator _validator;
    private readonly Mock<IMcpHttpClient> _httpClientMock;
    private readonly Mock<ISchemaValidator> _schemaValidatorMock;
    private readonly Mock<ISchemaRegistry> _schemaRegistryMock;

    public ResourceValidatorIntegrationTests()
    {
        var logger = new Mock<ILogger<ResourceValidator>>();
        _httpClientMock = new Mock<IMcpHttpClient>();
        _schemaValidatorMock = new Mock<ISchemaValidator>();
        _schemaRegistryMock = new Mock<ISchemaRegistry>();
        var contentSafety = new Mock<IContentSafetyAnalyzer>();
        contentSafety.Setup(x => x.AnalyzeResource(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(new List<ContentSafetyFinding>());

        _schemaRegistryMock
            .Setup(registry => registry.GetSchema(It.IsAny<ProtocolVersion>(), It.IsAny<string>(), It.IsAny<string>()))
            .Throws(new FileNotFoundException("Schema not found"));

        _validator = new ResourceValidator(logger.Object, _httpClientMock.Object, _schemaValidatorMock.Object, _schemaRegistryMock.Object, contentSafety.Object);
    }

    [Fact]
    public async Task ValidateResourceDiscovery_WithAuthRequired_ShouldSkip()
    {
        var config = new McpServerConfig { Endpoint = "https://test.com/mcp", Transport = "http" };
        _httpClientMock.Setup(x => x.CallAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse { StatusCode = 401, IsSuccess = false, Headers = new Dictionary<string, string> { { "WWW-Authenticate", "Bearer" } } });

        var result = await _validator.ValidateResourceDiscoveryAsync(config, new ResourceTestingConfig(), CancellationToken.None);

        result.Status.Should().Be(TestStatus.Skipped);
    }

    [Fact]
    public async Task ValidateResourceDiscovery_WithResources_ShouldParse()
    {
        var config = new McpServerConfig { Endpoint = "https://test.com/mcp", Transport = "http" };
        _httpClientMock.Setup(x => x.CallAsync(It.IsAny<string>(), "resources/list", It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse
            {
                StatusCode = 200,
                IsSuccess = true,
                RawJson = "{\"jsonrpc\":\"2.0\",\"result\":{\"resources\":[{\"uri\":\"file:///test.txt\",\"name\":\"test.txt\",\"mimeType\":\"text/plain\"}]},\"id\":1}"
            });

        var result = await _validator.ValidateResourceDiscoveryAsync(config, new ResourceTestingConfig { TestResourceReading = false }, CancellationToken.None);

        result.ResourcesDiscovered.Should().Be(1);
        result.ResourceResults.Should().HaveCount(1);
        result.ResourceResults[0].ResourceUri.Should().Be("file:///test.txt");
        result.ResourceResults[0].MetadataValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateResourceDiscovery_WithMissingUri_ShouldFlagInvalid()
    {
        var config = new McpServerConfig { Endpoint = "https://test.com/mcp", Transport = "http" };
        _httpClientMock.Setup(x => x.CallAsync(It.IsAny<string>(), "resources/list", It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse
            {
                StatusCode = 200,
                IsSuccess = true,
                RawJson = "{\"jsonrpc\":\"2.0\",\"result\":{\"resources\":[{\"name\":\"test.txt\"}]},\"id\":1}"
            });

        var result = await _validator.ValidateResourceDiscoveryAsync(config, new ResourceTestingConfig { TestResourceReading = false }, CancellationToken.None);

        result.ResourceResults[0].MetadataValid.Should().BeFalse();
        result.ResourceResults[0].Issues.Should().Contain(i => i.Contains("uri"));
        result.ResourceResults[0].Findings.Should().Contain(f => f.RuleId == ValidationFindingRuleIds.ResourceMissingUri);
    }

    [Fact]
    public async Task ValidateResourceDiscovery_WithoutMimeType_ShouldEmitGuidelineFinding()
    {
        var config = new McpServerConfig { Endpoint = "https://test.com/mcp", Transport = "http" };
        _httpClientMock.Setup(x => x.CallAsync(It.IsAny<string>(), "resources/list", It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse
            {
                StatusCode = 200,
                IsSuccess = true,
                RawJson = "{\"jsonrpc\":\"2.0\",\"result\":{\"resources\":[{\"uri\":\"file:///test.txt\",\"name\":\"test.txt\"}]},\"id\":1}"
            });

        var result = await _validator.ValidateResourceDiscoveryAsync(config, new ResourceTestingConfig { TestResourceReading = false }, CancellationToken.None);

        result.Findings.Should().Contain(f => f.RuleId == ValidationFindingRuleIds.ResourceGuidelineMimeTypeMissing);
    }

    [Fact]
    public async Task ValidateResourceDiscovery_WithNonAbsoluteUriScheme_ShouldEmitHeuristicFinding()
    {
        var config = new McpServerConfig { Endpoint = "https://test.com/mcp", Transport = "http" };
        _httpClientMock.Setup(x => x.CallAsync(It.IsAny<string>(), "resources/list", It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse
            {
                StatusCode = 200,
                IsSuccess = true,
                RawJson = "{\"jsonrpc\":\"2.0\",\"result\":{\"resources\":[{\"uri\":\"docs/README.md\",\"name\":\"README.md\",\"mimeType\":\"text/markdown\"}]},\"id\":1}"
            });

        var result = await _validator.ValidateResourceDiscoveryAsync(config, new ResourceTestingConfig { TestResourceReading = false }, CancellationToken.None);

        result.Findings.Should().Contain(f => f.RuleId == ValidationFindingRuleIds.ResourceUriSchemeUnclear);
    }

    [Fact]
    public async Task ValidateResourceDiscovery_WithResourceRead_ShouldValidateContents()
    {
        var config = new McpServerConfig { Endpoint = "https://test.com/mcp", Transport = "http" };
        _httpClientMock.Setup(x => x.CallAsync(It.IsAny<string>(), "resources/list", It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse
            {
                StatusCode = 200, IsSuccess = true,
                RawJson = "{\"jsonrpc\":\"2.0\",\"result\":{\"resources\":[{\"uri\":\"file:///test.txt\",\"name\":\"test\"}]},\"id\":1}"
            });
        _httpClientMock.Setup(x => x.CallAsync(It.IsAny<string>(), "resources/read", It.IsAny<object>(), It.IsAny<AuthenticationConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse
            {
                StatusCode = 200, IsSuccess = true,
                RawJson = "{\"jsonrpc\":\"2.0\",\"result\":{\"contents\":[{\"uri\":\"file:///test.txt\",\"mimeType\":\"text/plain\",\"text\":\"hello world\"}]},\"id\":2}"
            });

        var result = await _validator.ValidateResourceDiscoveryAsync(config, new ResourceTestingConfig { TestResourceReading = true }, CancellationToken.None);

        result.ResourceResults[0].AccessSuccessful.Should().BeTrue();
        result.ResourceResults[0].MimeType.Should().Be("text/plain");
        result.ResourceResults[0].ContentSize.Should().Be(11);
    }

    [Fact]
    public async Task ValidateResourceDiscovery_WithMissingTextAndBlob_ShouldFlagNonCompliant()
    {
        var config = new McpServerConfig { Endpoint = "https://test.com/mcp", Transport = "http" };
        _httpClientMock.Setup(x => x.CallAsync(It.IsAny<string>(), "resources/list", It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse
            {
                StatusCode = 200, IsSuccess = true,
                RawJson = "{\"jsonrpc\":\"2.0\",\"result\":{\"resources\":[{\"uri\":\"file:///test.txt\",\"name\":\"test\"}]},\"id\":1}"
            });
        _httpClientMock.Setup(x => x.CallAsync(It.IsAny<string>(), "resources/read", It.IsAny<object>(), It.IsAny<AuthenticationConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse
            {
                StatusCode = 200, IsSuccess = true,
                RawJson = "{\"jsonrpc\":\"2.0\",\"result\":{\"contents\":[{\"uri\":\"file:///test.txt\",\"mimeType\":\"text/plain\"}]},\"id\":2}"
            });

        var result = await _validator.ValidateResourceDiscoveryAsync(config, new ResourceTestingConfig { TestResourceReading = true }, CancellationToken.None);

        result.ResourceResults[0].Issues.Should().Contain(i => i.Contains("missing both 'text' and 'blob'"));
        result.ResourceResults[0].Findings.Should().Contain(f => f.RuleId == ValidationFindingRuleIds.ResourceReadMissingTextOrBlob);
    }

    [Fact]
    public async Task ValidateResourceDiscovery_WithEmptyResources_ShouldPass()
    {
        var config = new McpServerConfig { Endpoint = "https://test.com/mcp", Transport = "http" };
        _httpClientMock.Setup(x => x.CallAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse
            {
                StatusCode = 200, IsSuccess = true,
                RawJson = "{\"jsonrpc\":\"2.0\",\"result\":{\"resources\":[]},\"id\":1}"
            });

        var result = await _validator.ValidateResourceDiscoveryAsync(config, new ResourceTestingConfig(), CancellationToken.None);

        result.ResourcesDiscovered.Should().Be(0);
    }

    [Fact]
    public async Task ValidateResourceDiscovery_WithTemplateIssues_ShouldEmitStructuredTemplateFindings()
    {
        var config = new McpServerConfig { Endpoint = "https://test.com/mcp", Transport = "http" };
        _httpClientMock.Setup(x => x.CallAsync(It.IsAny<string>(), "resources/list", It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse
            {
                StatusCode = 200,
                IsSuccess = true,
                RawJson = "{\"jsonrpc\":\"2.0\",\"result\":{\"resources\":[]},\"id\":1}"
            });
        _httpClientMock.Setup(x => x.CallAsync(It.IsAny<string>(), ValidationConstants.Methods.ResourcesTemplatesList, It.IsAny<object>(), It.IsAny<AuthenticationConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse
            {
                StatusCode = 200,
                IsSuccess = true,
                RawJson = "{\"jsonrpc\":\"2.0\",\"result\":{\"resourceTemplates\":[{\"name\":\"broken-template\"},{\"name\":\"repo-file\",\"uriTemplate\":\"repo://{owner}/{repo}/{path}\"}]},\"id\":2}"
            });

        var result = await _validator.ValidateResourceDiscoveryAsync(config, new ResourceTestingConfig { TestResourceReading = false }, CancellationToken.None);

        result.Status.Should().Be(TestStatus.Failed);
        result.Findings.Should().Contain(f => f.RuleId == ValidationFindingRuleIds.ResourceTemplateMissingUriTemplate);
        result.Findings.Should().Contain(f => f.RuleId == ValidationFindingRuleIds.ResourceTemplateDescriptionMissing);
    }

    [Fact]
    public async Task ValidateResourceDiscovery_WithServerError_ShouldFail()
    {
        var config = new McpServerConfig { Endpoint = "https://test.com/mcp", Transport = "http" };
        _httpClientMock.Setup(x => x.CallAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse { StatusCode = 500, IsSuccess = false, Error = "Internal Error" });

        var result = await _validator.ValidateResourceDiscoveryAsync(config, new ResourceTestingConfig(), CancellationToken.None);

        result.Status.Should().Be(TestStatus.Failed);
    }

    [Fact]
    public async Task ValidateResourceDiscovery_WithSchemaProcessingError_ShouldWarnWithoutFailingCategory()
    {
        var config = new McpServerConfig { Endpoint = "https://test.com/mcp", Transport = "http" };
        _httpClientMock.Setup(x => x.CallAsync(It.IsAny<string>(), "resources/list", It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse
            {
                StatusCode = 200,
                IsSuccess = true,
                RawJson = "{\"jsonrpc\":\"2.0\",\"result\":{\"resources\":[{\"uri\":\"repo://demo/readme.md\",\"name\":\"README\",\"mimeType\":\"text/markdown\"}]},\"id\":1}"
            });

        _schemaRegistryMock
            .Setup(registry => registry.GetSchema(It.IsAny<ProtocolVersion>(), "protocol", "schema"))
            .Returns(() => new MemoryStream(Encoding.UTF8.GetBytes("{\"$defs\":{\"ListResourcesResult\":{\"type\":\"object\"}}}")));

        _schemaValidatorMock
            .Setup(validator => validator.Validate(It.IsAny<System.Text.Json.Nodes.JsonNode>(), It.IsAny<System.Text.Json.Nodes.JsonNode>()))
            .Returns(new SchemaValidationResult
            {
                IsValid = false,
                Errors = new List<string> { "Schema processing error: Could not resolve 'https://example.test/$defs/Resource'" }
            });

        var result = await _validator.ValidateResourceDiscoveryAsync(config, new ResourceTestingConfig { TestResourceReading = false }, CancellationToken.None);

        result.Status.Should().Be(TestStatus.Passed);
        result.Issues.Should().Contain(issue => issue.Contains("Schema validation warning: resources/list schema could not be fully processed"));
        result.Issues.Should().NotContain(issue => issue.Contains("NON-COMPLIANT", StringComparison.OrdinalIgnoreCase));
    }
}
