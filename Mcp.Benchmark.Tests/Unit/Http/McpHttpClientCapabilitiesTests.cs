using FluentAssertions;
using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Infrastructure.Http;
using Mcp.Benchmark.Infrastructure.Validators;
using Mcp.Benchmark.Infrastructure.Services;
using Mcp.Benchmark.Infrastructure.Registries;
using Mcp.Compliance.Spec;
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
    public async Task ErrorHandling_LocalDispatchRejection_ShouldNotBlameTarget()
    {
        var requests = 0;
        using var http = new HttpClient(new RoutingHandler(_ => { requests++; return CreateJsonResponse("{}"); }));
        http.DefaultRequestHeaders.Add("MCP-Protocol-Version", "2025-11-25");
        using var client = new McpHttpClient(http, Mock.Of<ILogger<McpHttpClient>>(), Mock.Of<IMcpClient>());
        var validator = new ErrorHandlingValidator(Mock.Of<ILogger<ErrorHandlingValidator>>(), client);

        var result = await validator.ValidateErrorHandlingAsync(
            new McpServerConfig { Endpoint = "https://example.test/mcp", Transport = "http" },
            new ErrorHandlingConfig { TestInvalidMethods = true, TestMalformedJson = false, TestGracefulDegradation = false,
                TestTimeoutHandling = false, TestConnectionInterruption = false });

        requests.Should().Be(0);
        result.Status.Should().Be(TestStatus.Inconclusive);
        result.Findings.Should().NotContain(finding => finding.EffectiveSource == ValidationRuleSource.Spec);
        result.ErrorScenariosHandledCorrectly.Should().Be(0);
    }

    [Theory]
    [InlineData(200)]
    [InlineData(401)]
    public async Task RawJson_ShouldDescribeActualAuthorization(int statusCode)
    {
        using var http = new HttpClient(new RoutingHandler(request =>
        {
            request.Headers.Authorization!.Scheme.Should().Be("Bearer");
            return new HttpResponseMessage((HttpStatusCode)statusCode) { Content = new StringContent("{}") };
        }));
        using var client = new McpHttpClient(http, Mock.Of<ILogger<McpHttpClient>>(), Mock.Of<IMcpClient>());
        client.SetAuthentication(new AuthenticationConfig { CustomHeaders = new() { ["Authorization"] = "Bearer synthetic-canary" } });

        var response = await client.SendRawJsonAsync("https://example.test/mcp", "{}", CancellationToken.None);

        response.ProbeContext!.AuthApplied.Should().BeTrue();
        response.ProbeContext.AuthStatus.Should().Be(statusCode == 401 ? ProbeAuthStatus.InvalidOrExpired : ProbeAuthStatus.Applied);
        JsonSerializer.Serialize(response.ProbeContext).Should().NotContain("synthetic-canary");
    }

    [Fact]
    public async Task PostConstructionFailure_ShouldDisposeOwnedContentWithoutContact()
    {
        var requests = 0;
        using var http = new HttpClient(new RoutingHandler(_ => { requests++; return CreateJsonResponse("{}"); }));
        using var client = new McpHttpClient(http, Mock.Of<ILogger<McpHttpClient>>(), Mock.Of<IMcpClient>());
        client.SetAuthentication(new AuthenticationConfig { CustomHeaders = new() { ["Invalid Header"] = "synthetic-canary" } });
        using var content = new DisposalTrackingContent();

        var send = () => client.SendAsync("https://example.test/mcp", content);

        await send.Should().ThrowAsync<InvalidOperationException>();
        requests.Should().Be(0);
        content.WasDisposed.Should().BeTrue();
    }

    private sealed class DisposalTrackingContent() : StringContent("{}")
    {
        public bool WasDisposed { get; private set; }
        protected override void Dispose(bool disposing)
        {
            WasDisposed = true;
            base.Dispose(disposing);
        }
    }

    [Theory]
    [InlineData(JsonRpcErrorProbeSelection.None, 0)]
    [InlineData(JsonRpcErrorProbeSelection.ParseError, 1)]
    [InlineData(JsonRpcErrorProbeSelection.InvalidRequest, 1)]
    [InlineData(JsonRpcErrorProbeSelection.MethodNotFound, 1)]
    [InlineData(JsonRpcErrorProbeSelection.InvalidParams, 1)]
    [InlineData(JsonRpcErrorProbeSelection.All, 4)]
    public async Task SelectiveHttpErrorProbes_ShouldNotDispatchUnselectedOperations(JsonRpcErrorProbeSelection selection, int expectedAttempts)
    {
        var requests = 0;
        using var http = new HttpClient(new RoutingHandler(_ =>
        {
            requests++;
            return CreateJsonResponse("""{"jsonrpc":"2.0","id":1,"error":{"code":-32601,"message":"Method not found"}}""");
        }));
        using var client = new McpHttpClient(http, Mock.Of<ILogger<McpHttpClient>>(), Mock.Of<IMcpClient>());

        var result = await client.ValidateErrorCodesAsync("https://example.test/mcp", selection);

        requests.Should().Be(expectedAttempts);
        result.Tests.Should().HaveCount(expectedAttempts);
        if (selection == JsonRpcErrorProbeSelection.None) result.IsCompliant.Should().BeFalse();
        var invalid = () => client.ValidateErrorCodesAsync("https://example.test/mcp", (JsonRpcErrorProbeSelection)16);
        await invalid.Should().ThrowAsync<ArgumentOutOfRangeException>();
        requests.Should().Be(expectedAttempts);
    }

    [Fact]
    public async Task ErrorHandling_InvalidMethodOnly_ShouldDispatchOnlyTheAdmittedProbe()
    {
        var payloads = new List<string>();
        using var http = new HttpClient(new RoutingHandler(request =>
        {
            payloads.Add(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            return CreateJsonResponse("""{"jsonrpc":"2.0","id":1,"error":{"code":-32601,"message":"Method not found"}}""");
        }));
        using var client = new McpHttpClient(http, Mock.Of<ILogger<McpHttpClient>>(), Mock.Of<IMcpClient>());
        var validator = new ErrorHandlingValidator(Mock.Of<ILogger<ErrorHandlingValidator>>(), client);

        var result = await validator.ValidateErrorHandlingAsync(
            new McpServerConfig { Endpoint = "https://example.test/mcp", Transport = "http" },
            new ErrorHandlingConfig
            {
                TestInvalidMethods = true, TestMalformedJson = false, TestGracefulDegradation = false,
                TestTimeoutHandling = false, TestConnectionInterruption = false
            });

        payloads.Should().ContainSingle();
        using var requestBody = JsonDocument.Parse(payloads[0]);
        requestBody.RootElement.GetProperty("jsonrpc").GetString().Should().Be("2.0");
        requestBody.RootElement.GetProperty("method").GetString().Should().NotBe("tools/call");
        result.Status.Should().Be(TestStatus.Passed);
        result.ErrorScenariosTestCount.Should().Be(1);
    }

    [Fact]
    public async Task TransportProbe_SessionComparison_ShouldUseRequestSnapshotAcrossConcurrentSessionChange()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var initializeCount = 0;
        using var http = new HttpClient(new AsyncRoutingHandler(async (request, token) =>
        {
            var payload = await request.Content!.ReadAsStringAsync(token);
            if (payload.Contains("\"method\":\"initialize\"", StringComparison.Ordinal))
            {
                var response = CreateJsonResponse("""{"jsonrpc":"2.0","id":1,"result":{"protocolVersion":"2025-11-25","capabilities":{}}}""");
                response.Headers.Add("MCP-Session-Id", $"session-{Interlocked.Increment(ref initializeCount)}");
                return response;
            }
            request.Headers.GetValues("MCP-Session-Id").Should().Equal("session-1");
            entered.SetResult();
            await release.Task.WaitAsync(token);
            return new HttpResponseMessage(HttpStatusCode.Accepted) { Content = new StringContent(string.Empty) };
        }));
        using var client = new McpHttpClient(http, Mock.Of<ILogger<McpHttpClient>>(), Mock.Of<IMcpClient>());
        await client.CallAsync("https://example.test/mcp", "initialize", new { });
        var probe = client.SendHttpTransportProbeAsync(new HttpTransportProbeRequest
        {
            Endpoint = "https://example.test/mcp", Method = "POST",
            Body = """{"jsonrpc":"2.0","method":"notifications/initialized"}"""
        });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try { await client.CallAsync("https://example.test/mcp", "initialize", new { }); }
        finally { release.TrySetResult(); }

        var result = await probe.WaitAsync(TimeSpan.FromSeconds(10));

        result.RequestSessionIdMatchesSession.Should().BeTrue();
    }

    private sealed class AsyncRoutingHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> factory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => factory(request, cancellationToken);
    }

    [Theory]
    [InlineData("tools/call", true)]
    [InlineData("tools/call", false)]
    [InlineData("custom/write", true)]
    [InlineData("custom/write", false)]
    [InlineData("initialize", true)]
    [InlineData("initialize", false)]
    public async Task RawRpc_ShouldNotReplayUncertainOperations(string method, bool timeout)
    {
        var acceptedOperations = 0;
        using var http = new HttpClient(new RoutingHandler(_ =>
        {
            acceptedOperations++;
            if (acceptedOperations > 1) return CreateJsonResponse("""{"jsonrpc":"2.0","id":1,"result":{}}""");
            if (timeout) throw new TaskCanceledException("synthetic timeout after acceptance");
            var failure = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("unavailable after acceptance") };
            failure.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.Zero);
            return failure;
        }));
        using var client = new McpHttpClient(http, Mock.Of<ILogger<McpHttpClient>>(), Mock.Of<IMcpClient>());
        client.SetAuthentication(new AuthenticationConfig { Type = "bearer", Token = "synthetic-token" });

        var result = await client.CallAsync("https://example.test/mcp", method, new { });

        acceptedOperations.Should().Be(1);
        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task RawRpc_DiscoveryRetry_ShouldPreserveRequestCorrelation()
    {
        var ids = new List<string>();
        using var http = new HttpClient(new RoutingHandler(request =>
        {
            using var body = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            ids.Add(body.RootElement.GetProperty("id").GetString()!);
            if (ids.Count > 1) return CreateJsonResponse("""{"jsonrpc":"2.0","id":1,"result":{"tools":[]}}""");
            var failure = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("temporary discovery failure") };
            failure.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.Zero);
            return failure;
        }));
        using var client = new McpHttpClient(http, Mock.Of<ILogger<McpHttpClient>>(), Mock.Of<IMcpClient>());

        var result = await client.CallAsync("https://example.test/mcp", "tools/list");

        result.IsSuccess.Should().BeTrue();
        ids.Should().HaveCount(2);
        ids.Distinct(StringComparer.Ordinal).Should().ContainSingle();
    }

    [Theory]
    [InlineData("matched", true)]
    [InlineData("missing", false)]
    [InlineData("wrong", false)]
    public async Task ProtocolValidation_ShouldVerifySessionPropagationWithoutPersistingSessionCanary(string sessionBehavior, bool expectedMatch)
    {
        const string canary = "synthetic-session-response-canary";
        using var http = new HttpClient(new RoutingHandler(request =>
        {
            var payload = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? string.Empty;
            if (sessionBehavior != "matched" && payload.Contains("\"notifications/initialized\"", StringComparison.Ordinal))
            {
                request.Headers.Remove("MCP-Session-Id");
                if (sessionBehavior == "wrong") request.Headers.Add("MCP-Session-Id", "wrong-session");
            }
            var response = CreateJsonResponse("""{"jsonrpc":"2.0","id":1,"result":{"protocolVersion":"2025-11-25","capabilities":{}}}""");
            response.Headers.Add("MCP-Session-Id", canary);
            return response;
        }));
        var sdk = new Mock<IMcpClient>();
        sdk.Setup(client => client.InitializeAsync(It.IsAny<McpServerConfig>(), It.IsAny<AuthenticationConfig?>(),
                It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InitializeResult
            {
                ProtocolVersion = "2025-11-25", ServerInfo = new Implementation { Name = "fixture", Version = "1" }, Capabilities = new McpServerCapabilities()
            });
        using var client = new McpHttpClient(http, Mock.Of<ILogger<McpHttpClient>>(), sdk.Object);
        client.SetProtocolVersion("2025-11-25");
        var schemas = new EmbeddedSchemaRegistry();
        var validator = new ProtocolComplianceValidator(Mock.Of<ILogger<ProtocolComplianceValidator>>(), client,
            new ProtocolRuleRegistry(), new ValidationApplicabilityResolver(schemas),
            new ProtocolFeatureResolver(new ValidationPackRegistry<IProtocolFeaturePack>([new BuiltInProtocolFeaturePack(schemas)])));

        var result = await validator.ValidateJsonRpcComplianceAsync(
            new McpServerConfig { Endpoint = "https://example.test/mcp", Transport = "http", ProtocolVersion = "2025-11-25" },
            new ProtocolComplianceConfig { ProtocolVersion = "2025-11-25" });

        result.StreamableHttpTransport.Should().NotBeNull();
        result.StreamableHttpTransport!.Probes.Should().Contain(probe => probe.ProbeId == "session-id-propagated" && probe.Passed == expectedMatch);
        var canonical = new ValidationResult { ProtocolCompliance = result }.CloneWithoutSecrets();
        JsonSerializer.Serialize(canonical).Should().NotContain(canary);
    }

    [Theory]
    [InlineData("MCP-Protocol-Version", "2024-11-05")]
    [InlineData("MCP-Session-Id", "ambient-session")]
    [InlineData("Mcp-Method", "tools/call")]
    [InlineData("Authorization", "Bearer ambient-canary")]
    public async Task TransportProbe_ShouldRejectAmbientRuntimeHeadersBeforeContact(string name, string value)
    {
        var contacted = false;
        using var http = new HttpClient(new RoutingHandler(_ => { contacted = true; return CreateJsonResponse("{}"); }));
        http.DefaultRequestHeaders.Add(name, value);
        using var client = new McpHttpClient(http, Mock.Of<ILogger<McpHttpClient>>(), Mock.Of<IMcpClient>());

        var result = await client.SendHttpTransportProbeAsync(new HttpTransportProbeRequest
        {
            Endpoint = "https://example.test/mcp", Method = "POST", Body = "{}",
            IncludeDefaultAuthentication = false, IncludeProtocolVersionHeader = false, IncludeSessionIdHeader = false
        });

        contacted.Should().BeFalse();
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().NotContain(value);
        http.DefaultRequestHeaders.GetValues(name).Should().Equal(value);
    }

    [Theory]
    [InlineData("2025-11-25")]
    [InlineData("2026-07-28")]
    public async Task RawRpc_ShouldPreserveRuntimeIdentityAgainstCustomHeaders(string version)
    {
        var received = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var http = new HttpClient(new RoutingHandler(request =>
        {
            foreach (var header in request.Headers) received[header.Key] = string.Join(",", header.Value);
            return CreateJsonResponse("""{"jsonrpc":"2.0","id":1,"result":{}}""");
        }));
        using var client = new McpHttpClient(http, Mock.Of<ILogger<McpHttpClient>>(), Mock.Of<IMcpClient>());
        client.SetProtocolVersion(version);
        var authentication = new AuthenticationConfig
        {
            CustomHeaders = new()
            {
                ["MCP-Protocol-Version"] = "2024-11-05",
                ["MCP-Session-Id"] = "unapproved-session",
                ["Mcp-Method"] = "tools/call"
            }
        };

        var response = await client.CallAsync("https://example.test/mcp", "tools/list", null, authentication);

        received["MCP-Protocol-Version"].Should().Be(version);
        received.Should().NotContainKey("MCP-Session-Id");
        if (version == "2026-07-28") received["Mcp-Method"].Should().Be("tools/list");
        else received.Should().NotContainKey("Mcp-Method");
        response.ProbeContext!.ProtocolVersion.Should().Be(version);
    }

    [Fact]
    public async Task TransportProbe_ShouldOmitRuntimeHeadersAndRouteContentHeaders()
    {
        var received = false;
        using var http = new HttpClient(new RoutingHandler(request =>
        {
            received = true;
            request.Headers.Contains("MCP-Protocol-Version").Should().BeFalse();
            request.Headers.Contains("MCP-Session-Id").Should().BeFalse();
            request.Content!.Headers.ContentLanguage.Should().Equal("en");
            return CreateJsonResponse("{}");
        }));
        using var client = new McpHttpClient(http, Mock.Of<ILogger<McpHttpClient>>(), Mock.Of<IMcpClient>());
        client.SetProtocolVersion("2025-11-25");
        client.SetAuthentication(new AuthenticationConfig
        {
            CustomHeaders = new() { ["MCP-Protocol-Version"] = "2024-11-05", ["MCP-Session-Id"] = "unapproved-session" }
        });

        var response = await client.SendHttpTransportProbeAsync(new HttpTransportProbeRequest
        {
            Endpoint = "https://example.test/mcp", Method = "POST", Body = "{}",
            Headers = new() { ["Content-Language"] = "en" },
            IncludeProtocolVersionHeader = false, IncludeSessionIdHeader = false
        });

        received.Should().BeTrue();
        response.IsSuccess.Should().BeTrue();
    }

    [Theory]
    [InlineData(200)]
    [InlineData(401)]
    public async Task RawRpc_ShouldDeriveAuthEvidenceFromFinalAuthorizationHeader(int statusCode)
    {
        using var http = new HttpClient(new RoutingHandler(request =>
        {
            request.Headers.Authorization!.Scheme.Should().Be("Bearer");
            return new HttpResponseMessage((HttpStatusCode)statusCode)
            {
                Content = new StringContent("""{"jsonrpc":"2.0","id":1,"result":{}}""")
            };
        }));
        using var client = new McpHttpClient(http, Mock.Of<ILogger<McpHttpClient>>(), Mock.Of<IMcpClient>());

        var response = await client.CallAsync("https://example.test/mcp", "tools/list", null,
            new AuthenticationConfig { CustomHeaders = new() { ["Authorization"] = "Bearer synthetic-canary" } });

        response.ProbeContext!.AuthApplied.Should().BeTrue();
        response.ProbeContext.AuthStatus.Should().Be(statusCode == 401 ? ProbeAuthStatus.InvalidOrExpired : ProbeAuthStatus.Applied);
    }

    [Fact]
    public async Task TransportProbe_ShouldRedactSessionCanaryFromEvidenceAndConfiguration()
    {
        const string canary = "synthetic-session-canary";
        using var http = new HttpClient(new RoutingHandler(request =>
        {
            request.Headers.GetValues("MCP-Session-Id").Should().Equal(canary);
            return CreateJsonResponse("{}");
        }));
        using var client = new McpHttpClient(http, Mock.Of<ILogger<McpHttpClient>>(), Mock.Of<IMcpClient>());
        var response = await client.SendHttpTransportProbeAsync(new HttpTransportProbeRequest
        {
            Endpoint = "https://example.test/mcp", Method = "POST", Body = "{}",
            Headers = new() { ["MCP-Session-Id"] = canary }, IncludeSessionIdHeader = false
        });
        var configuration = new McpServerConfig
        {
            Headers = new() { ["MCP-Session-Id"] = canary },
            Authentication = new AuthenticationConfig { CustomHeaders = new() { ["MCP-Session-Id"] = canary } }
        };

        response.IsSuccess.Should().BeTrue();
        JsonSerializer.Serialize(response.RequestHeaders).Should().NotContain(canary);
        JsonSerializer.Serialize(configuration.CloneWithoutSecrets()).Should().NotContain(canary);
    }

    [Theory]
    [InlineData("rpc", false)]
    [InlineData("rpc", true)]
    [InlineData("post", false)]
    [InlineData("probe", false)]
    [InlineData("probe", true)]
    public async Task RawRequests_ShouldApplySelectedCustomHeadersWithoutMutatingDefaults(string path, bool explicitAuthentication)
    {
        string? receivedRoute = null;
        using var http = new HttpClient(new RoutingHandler(request =>
        {
            receivedRoute = string.Join(",", request.Headers.GetValues("X-Route"));
            return CreateJsonResponse("""{"jsonrpc":"2.0","id":1,"result":{}}""");
        }));
        http.DefaultRequestHeaders.Add("X-Route", "client-default");
        using var client = new McpHttpClient(http, Mock.Of<ILogger<McpHttpClient>>(), Mock.Of<IMcpClient>());
        client.SetAuthentication(new AuthenticationConfig { CustomHeaders = new() { ["X-Route"] = "run-default" } });
        var authentication = explicitAuthentication
            ? new AuthenticationConfig { CustomHeaders = new() { ["x-route"] = "request-specific" } }
            : null;

        if (path == "rpc")
            await client.CallAsync("https://example.test/mcp", "tools/list", null, authentication);
        else if (path == "post")
        {
            using var response = await client.SendAsync("https://example.test/mcp", new StringContent("{}"));
        }
        else
            await client.SendHttpTransportProbeAsync(new HttpTransportProbeRequest
            {
                Endpoint = "https://example.test/mcp", Method = "POST", Body = "{}",
                Authentication = authentication, IncludeDefaultAuthentication = true
            });

        receivedRoute.Should().Be(explicitAuthentication ? "request-specific" : "run-default");
        http.DefaultRequestHeaders.GetValues("X-Route").Should().Equal("client-default");
    }

    [Fact]
    public async Task TransportProbe_ShouldTransmitButRedactCredentialHeaders()
    {
        using var http = new HttpClient(new RoutingHandler(request =>
        {
            request.Headers.GetValues("Cookie").Should().Equal("synthetic-session=value");
            request.Headers.GetValues("X-Api-Key").Should().Equal("synthetic-api-value");
            return CreateJsonResponse("{}");
        }));
        using var client = new McpHttpClient(http, Mock.Of<ILogger<McpHttpClient>>(), Mock.Of<IMcpClient>());
        client.SetAuthentication(new AuthenticationConfig
        {
            CustomHeaders = new() { ["Cookie"] = "synthetic-session=value", ["X-Api-Key"] = "synthetic-api-value", ["X-Route"] = "route" }
        });

        var response = await client.SendHttpTransportProbeAsync(new HttpTransportProbeRequest
        {
            Endpoint = "https://example.test/mcp", Method = "POST", Body = "{}"
        });

        response.IsSuccess.Should().BeTrue();
        response.RequestHeaders["cookie"].Should().NotContain("synthetic-session");
        response.RequestHeaders["X-Api-Key"].Should().NotContain("synthetic-api-value");
        response.RequestHeaders["X-Route"].Should().Be("route");
    }

    [Theory]
    [InlineData("Invalid Header", "synthetic-secret")]
    [InlineData("X-Api-Key", "synthetic-secret\r\nInjected: value")]
    public async Task TransportProbe_WithInvalidCustomHeader_ShouldFailWithoutContactOrSecretEcho(string name, string value)
    {
        var contacted = false;
        using var http = new HttpClient(new RoutingHandler(_ =>
        {
            contacted = true;
            return CreateJsonResponse("{}");
        }));
        using var client = new McpHttpClient(http, Mock.Of<ILogger<McpHttpClient>>(), Mock.Of<IMcpClient>());
        client.SetAuthentication(new AuthenticationConfig { CustomHeaders = new() { [name] = value } });

        var response = await client.SendHttpTransportProbeAsync(new HttpTransportProbeRequest
        {
            Endpoint = "https://example.test/mcp", Method = "POST", Body = "{}"
        });

        contacted.Should().BeFalse();
        response.IsSuccess.Should().BeFalse();
        response.Error.Should().NotContain("synthetic-secret");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExplicitNoAuthentication_ShouldNotRestoreDefaultCredentials(bool useRpc)
    {
        var received = false;
        using var http = new HttpClient(new RoutingHandler(request =>
        {
            received = true;
            request.Headers.Authorization.Should().BeNull();
            request.Headers.Contains("X-Api-Key").Should().BeFalse();
            request.Headers.GetValues("X-Route").Should().Equal("stable-route");
            return CreateJsonResponse("""{"jsonrpc":"2.0","id":1,"result":{}}""");
        }));
        http.DefaultRequestHeaders.Add("X-Route", "stable-route");
        using var client = new McpHttpClient(http, Mock.Of<ILogger<McpHttpClient>>(), Mock.Of<IMcpClient>());
        client.SetAuthentication(new AuthenticationConfig
        {
            Type = "bearer", Token = "synthetic-token", CustomHeaders = new() { ["X-Api-Key"] = "synthetic-key" }
        });

        if (useRpc)
            await client.CallAsync("https://example.test/mcp", "tools/list", null, new AuthenticationConfig { Token = string.Empty });
        else
            await client.SendHttpTransportProbeAsync(new HttpTransportProbeRequest
            {
                Endpoint = "https://example.test/mcp", Method = "POST", Body = "{}", IncludeDefaultAuthentication = false
            });

        received.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateInitializeAsync_CallerCancellation_ShouldPropagateWithoutRawFallback()
    {
        var mcpClient = new Mock<IMcpClient>();
        mcpClient
            .Setup(client => client.InitializeAsync(
                It.IsAny<McpServerConfig>(),
                It.IsAny<AuthenticationConfig?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());
        using var httpClient = new HttpClient(new RoutingHandler(_ => throw new InvalidOperationException("Raw fallback must not execute after caller cancellation.")));
        var client = new McpHttpClient(httpClient, Mock.Of<ILogger<McpHttpClient>>(), mcpClient.Object);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var action = () => client.ValidateInitializeAsync("https://example.test/mcp", cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
    }
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
