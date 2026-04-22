using FluentAssertions;
using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Infrastructure.Http;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using Moq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;

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

    [Fact]
    public async Task ValidateCapabilitiesAsync_WhenSdkDiscoveryFallsBackToRawJson_UsesRawToolCount()
    {
        const string endpoint = "http://localhost:8080/mcp";

        using var httpClient = new HttpClient(new RoutingHandler(request =>
        {
            var payload = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            using var document = JsonDocument.Parse(payload);
            var method = document.RootElement.GetProperty("method").GetString();

            return method switch
            {
                "tools/list" => CreateJsonResponse("{\"jsonrpc\":\"2.0\",\"result\":{\"tools\":[{\"name\":\"demo-tool\"},{\"name\":\"secondary-tool\"}]},\"id\":\"tools\"}"),
                "resources/list" => CreateJsonResponse("{\"jsonrpc\":\"2.0\",\"result\":{\"resources\":[]},\"id\":\"resources\"}"),
                "prompts/list" => CreateJsonResponse("{\"jsonrpc\":\"2.0\",\"result\":{\"prompts\":[]},\"id\":\"prompts\"}"),
                "tools/call" => new HttpResponseMessage(HttpStatusCode.BadRequest)
                {
                    Content = new StringContent("{\"jsonrpc\":\"2.0\",\"error\":{\"code\":-32602,\"message\":\"Invalid params\"},\"id\":\"call\"}")
                },
                _ => throw new InvalidOperationException($"Unexpected method '{method}'.")
            };
        }))
        {
            Timeout = TimeSpan.FromSeconds(5)
        };

        var loggerMock = new Mock<ILogger<McpHttpClient>>();
        var mcpClientMock = new Mock<IMcpClient>();

        mcpClientMock
            .Setup(x => x.ListToolsAsync(
                It.Is<McpServerConfig>(cfg => cfg.Endpoint == endpoint),
                It.IsAny<AuthenticationConfig?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("SDK tool discovery failed"));

        var client = new McpHttpClient(httpClient, loggerMock.Object, mcpClientMock.Object);

        var result = await client.ValidateCapabilitiesAsync(endpoint, CancellationToken.None);

        result.IsSuccessful.Should().BeTrue();
        result.Payload.Should().NotBeNull();
        result.Payload!.DiscoveredToolsCount.Should().Be(2);
        result.Payload.FirstToolName.Should().Be("demo-tool");
        result.Payload.ToolListingSucceeded.Should().BeTrue();
    }

    private static HttpResponseMessage CreateJsonResponse(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json)
        };
    }

    private sealed class RoutingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responseFactory;

        public RoutingHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        {
            _responseFactory = responseFactory;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_responseFactory(request));
        }
    }
}
