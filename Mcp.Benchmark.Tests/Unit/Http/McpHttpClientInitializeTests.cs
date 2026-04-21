using FluentAssertions;
using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Infrastructure.Http;
using Microsoft.Extensions.Logging;
using Moq;
using System.Net;
using System.Net.Http;
using System.Threading;
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

    [Fact]
    public async Task CallAsync_ShouldSendStrictApplicationJsonWithoutCharset()
    {
        HttpRequestMessage? capturedRequest = null;
        using var httpClient = new HttpClient(new CapturingHandler(request =>
        {
            capturedRequest = request;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"jsonrpc\":\"2.0\",\"result\":{},\"id\":\"1\"}")
            };
        }));

        var loggerMock = new Mock<ILogger<McpHttpClient>>();
        var mcpClientMock = new Mock<IMcpClient>();
        var client = new McpHttpClient(httpClient, loggerMock.Object, mcpClientMock.Object);

        var response = await client.CallAsync("https://example.test/mcp", "initialize", new { }, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        capturedRequest.Should().NotBeNull();
        capturedRequest!.Content.Should().NotBeNull();
        capturedRequest.Content!.Headers.ContentType.Should().NotBeNull();
        capturedRequest.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
        capturedRequest.Content.Headers.ContentType.CharSet.Should().BeNull();
    }

    [Fact]
    public async Task CallAsync_ShouldRetryTransient503AndSucceed()
    {
        var attempts = 0;
        using var httpClient = new HttpClient(new CapturingHandler(_ =>
        {
            attempts++;
            if (attempts == 1)
            {
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                {
                    Content = new StringContent("temporarily unavailable")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"jsonrpc\":\"2.0\",\"result\":{},\"id\":\"1\"}")
            };
        }));

        var loggerMock = new Mock<ILogger<McpHttpClient>>();
        var mcpClientMock = new Mock<IMcpClient>();
        var client = new McpHttpClient(httpClient, loggerMock.Object, mcpClientMock.Object);

        var response = await client.CallAsync("https://example.test/mcp", "initialize", new { }, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        attempts.Should().Be(2);
    }

    [Fact]
    public async Task CallAsync_ShouldRetryTransientCancellationAndSucceed()
    {
        var attempts = 0;
        using var httpClient = new HttpClient(new CapturingHandler(_ =>
        {
            attempts++;
            if (attempts == 1)
            {
                throw new TaskCanceledException("request timed out");
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"jsonrpc\":\"2.0\",\"result\":{},\"id\":\"1\"}")
            };
        }));

        var loggerMock = new Mock<ILogger<McpHttpClient>>();
        var mcpClientMock = new Mock<IMcpClient>();
        var client = new McpHttpClient(httpClient, loggerMock.Object, mcpClientMock.Object);

        var response = await client.CallAsync("https://example.test/mcp", "initialize", new { }, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        attempts.Should().Be(2);
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responseFactory;

        public CapturingHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        {
            _responseFactory = responseFactory;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_responseFactory(request));
        }
    }
}
