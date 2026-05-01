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
using McpServerCapabilities = ModelContextProtocol.Protocol.ServerCapabilities;
using ModelContextProtocol.Protocol;

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
            .Setup(x => x.InitializeAsync(
                It.Is<McpServerConfig>(cfg => cfg.Endpoint == endpoint),
                It.IsAny<AuthenticationConfig?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InitializeResult
            {
                ProtocolVersion = "2025-11-25",
                ServerInfo = new Implementation { Name = "test-server", Version = "1.0.0" },
                Capabilities = new McpServerCapabilities
                {
                    Tools = new ToolsCapability()
                }
            });

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
            .Setup(x => x.InitializeAsync(
                It.Is<McpServerConfig>(cfg => cfg.Endpoint == endpoint),
                It.IsAny<AuthenticationConfig?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InitializeResult
            {
                ProtocolVersion = "2025-11-25",
                ServerInfo = new Implementation { Name = "test-server", Version = "1.0.0" },
                Capabilities = new McpServerCapabilities
                {
                    Tools = new ToolsCapability(),
                    Resources = new ResourcesCapability(),
                    Prompts = new PromptsCapability()
                }
            });

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

    [Fact]
    public async Task ValidateCapabilitiesAsync_WhenToolsAreNotAdvertised_DoesNotProbeToolSurface()
    {
        const string endpoint = "http://localhost:8080/mcp";

        using var httpClient = new HttpClient(new RoutingHandler(_ =>
            throw new InvalidOperationException("No JSON-RPC probes should run when no capabilities are advertised.")))
        {
            Timeout = TimeSpan.FromSeconds(5)
        };

        var loggerMock = new Mock<ILogger<McpHttpClient>>();
        var mcpClientMock = new Mock<IMcpClient>();

        mcpClientMock
            .Setup(x => x.InitializeAsync(
                It.Is<McpServerConfig>(cfg => cfg.Endpoint == endpoint),
                It.IsAny<AuthenticationConfig?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InitializeResult
            {
                ProtocolVersion = "2025-11-25",
                ServerInfo = new Implementation { Name = "test-server", Version = "1.0.0" },
                Capabilities = new McpServerCapabilities()
            });

        var client = new McpHttpClient(httpClient, loggerMock.Object, mcpClientMock.Object);

        var result = await client.ValidateCapabilitiesAsync(endpoint, CancellationToken.None);

        result.IsSuccessful.Should().BeTrue();
        result.Payload.Should().NotBeNull();
        result.Payload!.CapabilityDeclarationsAvailable.Should().BeTrue();
        result.Payload.AdvertisedCapabilities.Should().BeEmpty();
        result.Payload.ToolListResponse.Should().BeNull();
        result.Payload.ToolListingSucceeded.Should().BeFalse();

        mcpClientMock.Verify(x => x.ListToolsAsync(
            It.IsAny<McpServerConfig>(),
            It.IsAny<AuthenticationConfig?>(),
            It.IsAny<string?>(),
            It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ValidateErrorCodesAsync_WithPlainText400MalformedRequests_ShouldNotTreatThemAsJsonRpcCompliant()
    {
        const string endpoint = "http://localhost:8080/mcp";

        using var httpClient = new HttpClient(new RoutingHandler(request =>
        {
            var payload = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();

            if (payload == "{ invalid json")
            {
                return new HttpResponseMessage(HttpStatusCode.BadRequest)
                {
                    Content = new StringContent("malformed payload")
                };
            }

            if (payload == "{\"method\":\"test\",\"id\":1,\"params\":{}}")
            {
                return new HttpResponseMessage(HttpStatusCode.BadRequest)
                {
                    Content = new StringContent("invalid message version tag")
                };
            }

            using var document = JsonDocument.Parse(payload);
            var method = document.RootElement.GetProperty("method").GetString();

            return method switch
            {
                "nonexistent_method_12345" => CreateJsonResponse("{\"jsonrpc\":\"2.0\",\"error\":{\"code\":-32601,\"message\":\"Method not found\"},\"id\":\"1\"}"),
                "tools/call" => CreateJsonResponse("{\"jsonrpc\":\"2.0\",\"error\":{\"code\":-32602,\"message\":\"Invalid params\"},\"id\":\"1\"}"),
                _ => throw new InvalidOperationException($"Unexpected payload '{payload}'.")
            };
        }))
        {
            Timeout = TimeSpan.FromSeconds(5)
        };

        var loggerMock = new Mock<ILogger<McpHttpClient>>();
        var mcpClientMock = new Mock<IMcpClient>();
        var client = new McpHttpClient(httpClient, loggerMock.Object, mcpClientMock.Object);

        var result = await client.ValidateErrorCodesAsync(endpoint, CancellationToken.None);

        result.IsCompliant.Should().BeFalse();
        result.Tests.Should().ContainSingle(test => test.ExpectedErrorCode == -32700 && !test.IsValid);
        result.Tests.Should().ContainSingle(test => test.ExpectedErrorCode == -32600 && !test.IsValid);
        result.Tests.Should().ContainSingle(test => test.ExpectedErrorCode == -32601 && test.IsValid);
        result.Tests.Should().ContainSingle(test => test.ExpectedErrorCode == -32602 && test.IsValid);
    }

    [Fact]
    public async Task CallAsync_WithMultilineSseEvent_ShouldPreserveFullJsonPayload()
    {
        const string endpoint = "http://localhost:8080/mcp";
        const string ssePayload = "event: message\n" +
                                  "data: {\"jsonrpc\":\"2.0\",\n" +
                                  "data: \"error\":{\"code\":-32601,\"message\":\"Method not found\"},\"id\":\"1\"}\n\n";

        using var httpClient = new HttpClient(new RoutingHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(ssePayload)
            };
            response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/event-stream");
            return response;
        }))
        {
            Timeout = TimeSpan.FromSeconds(5)
        };

        var loggerMock = new Mock<ILogger<McpHttpClient>>();
        var mcpClientMock = new Mock<IMcpClient>();
        var client = new McpHttpClient(httpClient, loggerMock.Object, mcpClientMock.Object);

        var response = await client.CallAsync(endpoint, "nonexistent_method_12345", null, CancellationToken.None);

        response.RawJson.Should().NotBeNullOrWhiteSpace();

        using var document = JsonDocument.Parse(response.RawJson!);
        document.RootElement.GetProperty("error").GetProperty("code").GetInt32().Should().Be(-32601);
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
