using System.Net;
using System.Text.Json;
using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Infrastructure.Http;
using Mcp.Benchmark.Core.Services;
using Mcp.Benchmark.Infrastructure.Services;
using Mcp.Benchmark.Infrastructure.Validators;
using Microsoft.Extensions.Logging;

namespace Mcp.Benchmark.Tests.Unit.Http;

public sealed class ModernRequestMetadataTests
{
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task SyntheticClientInputs_ShouldUseExactPrivateRoundsAndRequestLocalCapabilities(bool mismatch, bool admitted)
    {
        using var requestDocument = JsonDocument.Parse("""{"roots":{"method":"roots/list"}}""");
        using var responseDocument = JsonDocument.Parse("""{"roots":{"roots":[]}}""");
        var config = new ToolTestingConfig { CustomScenarios = [new ToolTestScenario
        {
            ToolName = "owned", Parameters = new() { ["value"] = "private-argument" },
            ClientInputRounds = [new ToolClientInputRound { ExpectedRequests = requestDocument.RootElement, Responses = responseDocument.RootElement }]
        }] };
        var captured = ToolScenarioInputs.CaptureScenarios(config)["owned"];
        var registry = new Mcp.Compliance.Spec.EmbeddedSchemaRegistry();
        var validator = new JsonSchemaValidator();
        var capabilities = SyntheticToolClientInputs.CreateCapabilities(captured, "2026-07-28", registry, validator);
        var handler = new SyntheticInputHandler(mismatch);
        using var http = new HttpClient(handler);
        using var client = new McpHttpClient(http, Mock.Of<ILogger<McpHttpClient>>(), Mock.Of<IMcpClient>());
        client.ConfigureExecutionPolicy(new ExecutionPolicy
        {
            Mode = admitted ? ExecutionMode.Standard : ExecutionMode.Safe,
            AllowedHosts = ["example.test"], AllowedOrigins = ["https://example.test"], AllowPrivateAddresses = true
        });
        client.SetProtocolVersion("2026-07-28");
        using var schema = JsonDocument.Parse("""{"type":"object","properties":{"value":{"type":"string"}}}""");
        var initial = new ToolCallParameters("owned", captured.Arguments, schema.RootElement) { ClientCapabilities = capabilities };
        var result = await SyntheticToolClientInputs.ExecuteAsync(initial, captured, "2026-07-28", registry, validator,
            (parameters, token) => client.CallAsync("https://example.test/mcp", "tools/call", parameters, token), CancellationToken.None);
        result.IsSuccess.Should().Be(admitted && !mismatch);
        if (!admitted)
        {
            handler.Bodies.Should().BeEmpty();
            return;
        }
        handler.Bodies.Should().HaveCount(mismatch ? 1 : 2);
        var first = handler.Bodies[0].GetProperty("params");
        first.TryGetProperty("inputResponses", out _).Should().BeFalse();
        first.TryGetProperty("requestState", out _).Should().BeFalse();
        first.GetProperty("_meta").GetProperty("io.modelcontextprotocol/clientCapabilities").EnumerateObject().Select(property => property.Name).Should().Equal("roots");
        if (!mismatch)
        {
            var retry = handler.Bodies[1].GetProperty("params");
            JsonElement.DeepEquals(first.GetProperty("arguments"), retry.GetProperty("arguments")).Should().BeTrue();
            retry.GetProperty("requestState").GetString().Should().Be("opaque-state-canary");
            JsonElement.DeepEquals(retry.GetProperty("inputResponses"), responseDocument.RootElement).Should().BeTrue();
            handler.Bodies[1].GetProperty("id").GetString().Should().NotBe(handler.Bodies[0].GetProperty("id").GetString());
        }
        await client.CallAsync("https://example.test/mcp", "tools/list");
        handler.Bodies.Last().GetProperty("params").GetProperty("_meta").GetProperty("io.modelcontextprotocol/clientCapabilities").EnumerateObject().Should().BeEmpty();
    }

    private sealed class SyntheticInputHandler(bool mismatch) : HttpMessageHandler
    {
        public List<JsonElement> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            Bodies.Add(body.RootElement.Clone());
            object result = Bodies.Count == 1
                ? new { resultType = "input_required", inputRequests = new Dictionary<string, object>
                    { [mismatch ? "unapproved" : "roots"] = new { method = "roots/list" } }, requestState = "opaque-state-canary" }
                : new { resultType = "complete", content = Array.Empty<object>() };
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new { jsonrpc = "2.0", id = body.RootElement.GetProperty("id"), result }))
            };
        }
    }

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
            using var body = JsonDocument.Parse(Body);
            ProtocolVersionHeader = request.Headers.TryGetValues("MCP-Protocol-Version", out var values)
                ? values.Single()
                : null;
            MethodHeader = request.Headers.TryGetValues("Mcp-Method", out var methodValues)
                ? methodValues.Single()
                : null;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new { jsonrpc = "2.0", id = body.RootElement.GetProperty("id"), result = new { resultType = "complete" } }))
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
                Content = new StringContent(JsonSerializer.Serialize(new { jsonrpc = "2.0", id = body.RootElement.GetProperty("id"), result = new { resultType = "complete", cacheScope = "public", ttlMs = 60000, tools = Array.Empty<object>() } }))
            };
        }
    }
}
