using FluentAssertions;
using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Infrastructure.Http;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using Moq;

namespace Mcp.Benchmark.Tests.Unit.Http;

public class McpHttpClientCapabilitiesTests
{
    [Fact]
    public async Task ValidateCapabilitiesAsync_UsesMcpClientForToolsAndBuildsSummary()
    {
        // Arrange
        const string endpoint = "http://localhost:8080/mcp";

        using var httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(1)
        };

        var loggerMock = new Mock<ILogger<McpHttpClient>>();
        var mcpClientMock = new Mock<IMcpClient>();

        mcpClientMock
            .Setup(x => x.ListToolsAsync(
                It.Is<McpServerConfig>(cfg => cfg.Endpoint == endpoint),
                It.IsAny<AuthenticationConfig?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<McpClientTool>());

        mcpClientMock
            .Setup(x => x.CallToolAsync(
                It.Is<McpServerConfig>(cfg => cfg.Endpoint == endpoint),
                It.IsAny<AuthenticationConfig?>(),
                It.IsAny<string?>(),
                "demo-tool",
                It.IsAny<IDictionary<string, object?>>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var client = new McpHttpClient(httpClient, loggerMock.Object, mcpClientMock.Object);

        // Act
        var result = await client.ValidateCapabilitiesAsync(endpoint, CancellationToken.None);

        // Assert
        result.IsSuccessful.Should().BeTrue();
        result.Payload.Should().NotBeNull();

        var summary = result.Payload!;
        summary.Tools.Should().NotBeNull();
        summary.Tools.Should().BeEmpty();
        summary.ToolListingSucceeded.Should().BeTrue();
        summary.ToolInvocationSucceeded.Should().BeFalse();
        summary.DiscoveredToolsCount.Should().Be(0);

        mcpClientMock.Verify(x => x.ListToolsAsync(
            It.Is<McpServerConfig>(cfg => cfg.Endpoint == endpoint),
            It.IsAny<AuthenticationConfig?>(),
            It.IsAny<string?>(),
            It.IsAny<CancellationToken>()),
            Times.Once);

        mcpClientMock.Verify(x => x.CallToolAsync(
            It.Is<McpServerConfig>(cfg => cfg.Endpoint == endpoint),
            It.IsAny<AuthenticationConfig?>(),
            It.IsAny<string?>(),
            It.IsAny<string>(),
            It.IsAny<IDictionary<string, object?>>(),
            It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
