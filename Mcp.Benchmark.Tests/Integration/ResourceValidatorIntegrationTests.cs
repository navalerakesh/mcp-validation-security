using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Infrastructure.Validators;
using Mcp.Compliance.Spec;
using Microsoft.Extensions.Logging;
using Moq;
using FluentAssertions;
using Xunit;

namespace Mcp.Benchmark.Tests.Integration;

public class ResourceValidatorIntegrationTests
{
    private readonly ResourceValidator _validator;
    private readonly Mock<IMcpHttpClient> _httpClientMock;

    public ResourceValidatorIntegrationTests()
    {
        var logger = new Mock<ILogger<ResourceValidator>>();
        _httpClientMock = new Mock<IMcpHttpClient>();
        var schemaValidator = new Mock<ISchemaValidator>();
        var schemaRegistry = new Mock<ISchemaRegistry>();
        var contentSafety = new Mock<IContentSafetyAnalyzer>();
        contentSafety.Setup(x => x.AnalyzeResource(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(new List<ContentSafetyFinding>());

        _validator = new ResourceValidator(logger.Object, _httpClientMock.Object, schemaValidator.Object, schemaRegistry.Object, contentSafety.Object);
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
    public async Task ValidateResourceDiscovery_WithServerError_ShouldFail()
    {
        var config = new McpServerConfig { Endpoint = "https://test.com/mcp", Transport = "http" };
        _httpClientMock.Setup(x => x.CallAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse { StatusCode = 500, IsSuccess = false, Error = "Internal Error" });

        var result = await _validator.ValidateResourceDiscoveryAsync(config, new ResourceTestingConfig(), CancellationToken.None);

        result.Status.Should().Be(TestStatus.Failed);
    }
}
