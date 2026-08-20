using System.Net;
using System.Text.Json;
using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Infrastructure.Http;
using Microsoft.Extensions.Logging;

namespace Mcp.Benchmark.Tests.Unit.Http;

public sealed class ModernRequestMetadataTests
{
    [Fact]
    public async Task CallAsync_ModernProtocol_ShouldEmitRequiredPerRequestMetadataAndMatchingHeader()
    {
        var handler = new CapturingHandler();
        using var httpClient = new HttpClient(handler);
        var client = new McpHttpClient(httpClient, Mock.Of<ILogger<McpHttpClient>>(), Mock.Of<IMcpClient>());
        client.ConfigureExecutionPolicy(new ExecutionPolicy
        {
            AllowedHosts = ["example.test"],
            AllowedOrigins = ["https://example.test"],
            AllowPrivateAddresses = true
        });
        client.SetProtocolVersion("2026-07-28");

        var response = await client.CallAsync("https://example.test/mcp", "tools/list", new { cursor = "next" });

        response.IsSuccess.Should().BeTrue();
        handler.ProtocolVersionHeader.Should().Be("2026-07-28");
        handler.MethodHeader.Should().Be("tools/list");
        using var request = JsonDocument.Parse(handler.Body!);
        var parameters = request.RootElement.GetProperty("params");
        parameters.GetProperty("cursor").GetString().Should().Be("next");
        var metadata = parameters.GetProperty("_meta");
        metadata.GetProperty("io.modelcontextprotocol/protocolVersion").GetString().Should().Be("2026-07-28");
        metadata.GetProperty("io.modelcontextprotocol/clientCapabilities").EnumerateObject().Should().BeEmpty();
        metadata.GetProperty("io.modelcontextprotocol/clientInfo").GetProperty("name").GetString().Should().Be("mcpval");
        metadata.GetProperty("io.modelcontextprotocol/clientInfo").GetProperty("version").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task CallAsync_LegacyProtocol_ShouldNotInjectModernMetadata()
    {
        var handler = new CapturingHandler();
        using var httpClient = new HttpClient(handler);
        var client = new McpHttpClient(httpClient, Mock.Of<ILogger<McpHttpClient>>(), Mock.Of<IMcpClient>());
        client.ConfigureExecutionPolicy(new ExecutionPolicy
        {
            AllowedHosts = ["example.test"],
            AllowedOrigins = ["https://example.test"],
            AllowPrivateAddresses = true
        });
        client.SetProtocolVersion("2025-11-25");

        await client.CallAsync("https://example.test/mcp", "tools/list", new { cursor = "next" });

        using var request = JsonDocument.Parse(handler.Body!);
        request.RootElement.GetProperty("params").TryGetProperty("_meta", out _).Should().BeFalse();
    }

    [Fact]
    public async Task ValidateCapabilitiesAsync_ModernDiscovery_ShouldNotInvokeLegacyInitializeOrSdkClient()
    {
        var handler = new ModernCapabilityHandler();
        using var httpClient = new HttpClient(handler);
        var sdkClient = new Mock<IMcpClient>(MockBehavior.Strict);
        var client = new McpHttpClient(httpClient, Mock.Of<ILogger<McpHttpClient>>(), sdkClient.Object);
        client.ConfigureExecutionPolicy(new ExecutionPolicy
        {
            AllowedHosts = ["example.test"],
            AllowedOrigins = ["https://example.test"],
            AllowPrivateAddresses = true
        });
        client.SetProtocolVersion("2026-07-28");
        client.SetModernDiscovery(new ModernDiscoveryEvidence
        {
            IsValid = true,
            SupportedVersions = ["2026-07-28"],
            CapabilityNames = ["tools"],
            ResultType = "complete",
            CacheScope = "public",
            TtlMs = 60000
        });

        var result = await client.ValidateCapabilitiesAsync("https://example.test/mcp", CancellationToken.None);

        result.IsSuccessful.Should().BeTrue();
        result.Payload!.CapabilityDeclarationsAvailable.Should().BeTrue();
        result.Payload.AdvertisedCapabilities.Should().Equal("tools");
        result.Payload.ToolListingSucceeded.Should().BeTrue();
        handler.Methods.Should().Equal("tools/list");
        sdkClient.VerifyNoOtherCalls();
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public string? Body { get; private set; }
        public string? ProtocolVersionHeader { get; private set; }
        public string? MethodHeader { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            ProtocolVersionHeader = request.Headers.TryGetValues("MCP-Protocol-Version", out var values)
                ? values.Single()
                : null;
            MethodHeader = request.Headers.TryGetValues("Mcp-Method", out var methodValues)
                ? methodValues.Single()
                : null;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"jsonrpc\":\"2.0\",\"id\":\"test\",\"result\":{\"resultType\":\"complete\"}}")
            };
        }
    }

    private sealed class ModernCapabilityHandler : HttpMessageHandler
    {
        public List<string> Methods { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            Methods.Add(body.RootElement.GetProperty("method").GetString()!);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"jsonrpc\":\"2.0\",\"id\":\"test\",\"result\":{\"resultType\":\"complete\",\"cacheScope\":\"public\",\"ttlMs\":60000,\"tools\":[]}}")
            };
        }
    }
}
