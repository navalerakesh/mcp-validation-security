using FluentAssertions;
using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Infrastructure.Http;
using Microsoft.Extensions.Logging;
using Moq;
using ProtocolModels = ModelContextProtocol.Protocol;

namespace Mcp.Benchmark.Tests.Unit.Http;

public class McpHttpClientInitializeTests
{
    [Fact]
    public async Task ValidateInitializeAsync_DelegatesToMcpClientAndWrapsResult()
    {
        // Arrange
        const string endpoint = "http://localhost:8080/mcp";

        using var httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(5)
        };

        var loggerMock = new Mock<ILogger<McpHttpClient>>();
        var mcpClientMock = new Mock<IMcpClient>();

        var initializeResult = new ProtocolModels.InitializeResult
        {
            ProtocolVersion = "2025-03-26",
            Capabilities = default!,
            ServerInfo = default!,
            Instructions = "Welcome"
        };

        mcpClientMock
            .Setup(x => x.InitializeAsync(
                It.Is<McpServerConfig>(cfg => cfg.Endpoint == endpoint),
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(initializeResult);

        var client = new McpHttpClient(httpClient, loggerMock.Object, mcpClientMock.Object);

        // Act
        var result = await client.ValidateInitializeAsync(endpoint, CancellationToken.None);

        // Assert
        result.IsSuccessful.Should().BeTrue();
        result.Payload.Should().BeSameAs(initializeResult);
        result.Transport.Should().NotBeNull();
        result.Transport!.Duration.Should().BeGreaterThan(TimeSpan.Zero);

        mcpClientMock.Verify(x => x.InitializeAsync(
            It.Is<McpServerConfig>(cfg => cfg.Endpoint == endpoint),
            null,
            null,
            It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
