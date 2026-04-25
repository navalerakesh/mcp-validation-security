using FluentAssertions;
using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Infrastructure.Validators;
using Mcp.Compliance.Spec;
using Microsoft.Extensions.Logging;
using Moq;

namespace Mcp.Benchmark.Tests.Unit;

public class ResourceValidatorUnitTests
{
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
}