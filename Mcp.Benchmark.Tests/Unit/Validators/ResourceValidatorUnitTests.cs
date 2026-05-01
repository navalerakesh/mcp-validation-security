using FluentAssertions;
using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Constants;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Infrastructure.Validators;
using Mcp.Compliance.Spec;
using Microsoft.Extensions.Logging;
using Moq;

namespace Mcp.Benchmark.Tests.Unit;

public class ResourceValidatorUnitTests
{
    [Fact]
    public async Task ValidateResourceDiscoveryAsync_WhenResourcesCapabilityIsNotAdvertised_ShouldSkipResourceProbes()
    {
        var httpClient = new Mock<IMcpHttpClient>();
        var validator = new ResourceValidator(
            new Mock<ILogger<ResourceValidator>>().Object,
            httpClient.Object,
            new Mock<ISchemaValidator>().Object,
            new Mock<ISchemaRegistry>().Object,
            new Mock<IContentSafetyAnalyzer>().Object);

        var result = await validator.ValidateResourceDiscoveryAsync(
            new McpServerConfig { Endpoint = "http://localhost:8080/mcp", Transport = "http" },
            new ResourceTestingConfig
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
        result.Issues.Should().Contain("Resources capability was not advertised during initialize; resources/list, resources/read, templates, and subscription probes were skipped.");

        httpClient.Verify(client => client.CallAsync(
            It.IsAny<string>(),
            "resources/list",
            It.IsAny<object?>(),
            It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ValidateResourceDiscoveryAsync_WithMethodNotFound_ShouldTreatResourceSurfaceAsNotAdvertised()
    {
        var httpClient = new Mock<IMcpHttpClient>();
        httpClient
            .Setup(client => client.CallAsync(
                It.IsAny<string>(),
                "resources/list",
                It.IsAny<object?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse
            {
                StatusCode = 400,
                IsSuccess = false,
                Error = "Method not found",
                RawJson = "{\"jsonrpc\":\"2.0\",\"error\":{\"code\":-32601,\"message\":\"Method not found\"},\"id\":1}"
            });

        var validator = new ResourceValidator(
            new Mock<ILogger<ResourceValidator>>().Object,
            httpClient.Object,
            new Mock<ISchemaValidator>().Object,
            new Mock<ISchemaRegistry>().Object,
            new Mock<IContentSafetyAnalyzer>().Object);

        var result = await validator.ValidateResourceDiscoveryAsync(
            new McpServerConfig { Endpoint = "stdio-server", Transport = "stdio" },
            new ResourceTestingConfig { TestResourceReading = false },
            CancellationToken.None);

        result.Status.Should().Be(TestStatus.Passed);
        result.ResourcesDiscovered.Should().Be(0);
        result.Score.Should().Be(100);
        result.Issues.Should().Contain("✅ COMPLIANT: No resources were advertised; no resource reads were required");
    }

    [Fact]
    public async Task ValidateResourceDiscoveryAsync_WithSensitivePublicResource_ShouldEmitAccessControlAndContentFindings()
    {
        var httpClient = new Mock<IMcpHttpClient>();
        httpClient
            .Setup(client => client.CallAsync(
                It.IsAny<string>(),
                ValidationConstants.Methods.ResourcesList,
                It.IsAny<object?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse
            {
                StatusCode = 200,
                IsSuccess = true,
                RawJson = "{\"jsonrpc\":\"2.0\",\"result\":{\"resources\":[{\"uri\":\"file:///etc/ssh/private_key\",\"name\":\"private key\",\"mimeType\":\"application/octet-stream\",\"annotations\":{\"audience\":[\"assistant\"],\"priority\":0.9}}]},\"id\":1}"
            });

        httpClient
            .Setup(client => client.CallAsync(
                It.IsAny<string>(),
                ValidationConstants.Methods.ResourcesRead,
                It.IsAny<object?>(),
                It.IsAny<AuthenticationConfig?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse
            {
                StatusCode = 200,
                IsSuccess = true,
                RawJson = "{\"jsonrpc\":\"2.0\",\"result\":{\"contents\":[{\"uri\":\"file:///etc/ssh/other_key\",\"mimeType\":\"application/octet-stream\",\"blob\":\"not-base64\",\"annotations\":{\"audience\":[\"model\"],\"priority\":1.2}}]},\"id\":2}"
            });

        var contentSafety = new Mock<IContentSafetyAnalyzer>();
        contentSafety
            .Setup(analyzer => analyzer.AnalyzeResource(It.IsAny<string?>(), It.IsAny<string>()))
            .Returns(new List<ContentSafetyFinding>());

        var validator = new ResourceValidator(
            new Mock<ILogger<ResourceValidator>>().Object,
            httpClient.Object,
            new Mock<ISchemaValidator>().Object,
            new Mock<ISchemaRegistry>().Object,
            contentSafety.Object);

        var result = await validator.ValidateResourceDiscoveryAsync(
            new McpServerConfig
            {
                Endpoint = "https://example.test/mcp",
                Transport = "http",
                Profile = McpServerProfile.Public
            },
            new ResourceTestingConfig { TestResourceReading = true },
            CancellationToken.None);

        result.Findings.Should().Contain(finding => finding.RuleId == ValidationFindingRuleIds.ResourceAccessControlAdvisory);
        result.Findings.Should().Contain(finding => finding.RuleId == ValidationFindingRuleIds.ResourceReadContentUriMismatch);
        result.Findings.Should().Contain(finding => finding.RuleId == ValidationFindingRuleIds.ResourceReadBlobInvalidBase64);
        result.Findings.Should().Contain(finding => finding.RuleId == ValidationFindingRuleIds.ResourceAnnotationInvalid);

        var resourceResult = result.ResourceResults.Single(resource => resource.ResourceName == "private key");
        resourceResult.Findings.Should().Contain(finding => finding.RuleId == ValidationFindingRuleIds.ResourceAccessControlAdvisory);
    }

    [Fact]
    public async Task ValidateResourceDiscoveryAsync_WithBroadTemplateWithoutBoundaryGuidance_ShouldEmitAiSafetyFinding()
    {
        var httpClient = new Mock<IMcpHttpClient>();
        httpClient
            .Setup(client => client.CallAsync(
                It.IsAny<string>(),
                ValidationConstants.Methods.ResourcesList,
                It.IsAny<object?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse
            {
                StatusCode = 200,
                IsSuccess = true,
                RawJson = "{\"jsonrpc\":\"2.0\",\"result\":{\"resources\":[]},\"id\":1}"
            });

        httpClient
            .Setup(client => client.CallAsync(
                It.IsAny<string>(),
                ValidationConstants.Methods.ResourcesTemplatesList,
                It.IsAny<object?>(),
                It.IsAny<AuthenticationConfig?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse
            {
                StatusCode = 200,
                IsSuccess = true,
                RawJson = "{\"jsonrpc\":\"2.0\",\"result\":{\"resourceTemplates\":[{\"uriTemplate\":\"file:///{path}\",\"name\":\"Project Files\",\"description\":\"Read files by path\"}]},\"id\":2}"
            });

        var validator = new ResourceValidator(
            new Mock<ILogger<ResourceValidator>>().Object,
            httpClient.Object,
            new Mock<ISchemaValidator>().Object,
            new Mock<ISchemaRegistry>().Object,
            new Mock<IContentSafetyAnalyzer>().Object);

        var result = await validator.ValidateResourceDiscoveryAsync(
            new McpServerConfig { Endpoint = "https://example.test/mcp", Transport = "http" },
            new ResourceTestingConfig { TestResourceReading = false },
            CancellationToken.None);

        result.Findings.Should().Contain(finding => finding.RuleId == ValidationFindingRuleIds.ResourceTemplateBoundaryGuidanceMissing);
    }
}