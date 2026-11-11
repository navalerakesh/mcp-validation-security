using System.Reflection;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Infrastructure.Http;
using ModelContextProtocol.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Mcp.Benchmark.Tests.Unit.Http;

public sealed class McpClientFactorySecurityTests
{
    [Fact]
    public async Task DisposeAsync_ShouldRetainInitializationCleanupFailureAfterGetterEviction()
    {
        var factory = new McpClientFactory(NullLogger<McpClientFactory>.Instance, NullLoggerFactory.Instance);
        SeedOwnedClient(factory, new Mock<McpClient>().Object, new TrackingDisposable(), initializationCleanupFailure: true);

        await FluentActions.Awaiting(() => factory.GetOrCreateClientAsync(new McpServerConfig
            { Endpoint = "https://example.test/mcp", Authentication = null }, null, "2025-11-25"))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("SDK transport cleanup did not complete.");
        await FluentActions.Awaiting(() => factory.DisposeAsync().AsTask())
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("SDK transport cleanup did not complete.");
    }

    [Fact]
    public async Task DisposeAsync_ShouldAwaitInvalidatedOwnedCleanup()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new Mock<McpClient>();
        client.Setup(value => value.DisposeAsync()).Returns(ValueTask.CompletedTask);
        var transport = new TrackingDisposable { Pending = release.Task };
        var factory = new McpClientFactory(NullLogger<McpClientFactory>.Instance, NullLoggerFactory.Instance);
        SeedOwnedClient(factory, client.Object, transport);
        var invalidation = factory.InvalidateAsync(new McpServerConfig
            { Endpoint = "https://example.test/mcp", Authentication = null }, null, "2025-11-25");
        Task? disposal = null;
        try
        {
            await transport.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            disposal = factory.DisposeAsync().AsTask();
            disposal.IsCompleted.Should().BeFalse("cache removal must not remove cleanup ownership");
        }
        finally
        {
            release.TrySetResult();
            await Task.WhenAll(invalidation, disposal ?? factory.DisposeAsync().AsTask()).WaitAsync(TimeSpan.FromSeconds(10));
        }
        transport.Calls.Should().Be(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisposeAsync_ShouldAttemptTransportCleanupAndSurfaceFailures(bool clientFails)
    {
        var client = new Mock<McpClient>();
        client.Setup(value => value.DisposeAsync()).Returns(clientFails
            ? ValueTask.FromException(new IOException("Synthetic client cleanup failure.")) : ValueTask.CompletedTask);
        var transport = new TrackingDisposable { Fail = !clientFails };
        var factory = new McpClientFactory(NullLogger<McpClientFactory>.Instance, NullLoggerFactory.Instance);
        SeedOwnedClient(factory, client.Object, transport);

        await FluentActions.Awaiting(() => factory.DisposeAsync().AsTask()).Should().ThrowAsync<Exception>();

        transport.Calls.Should().Be(1);
        client.Verify(value => value.DisposeAsync(), Times.Once);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task GetOrCreateClientAsync_ShouldIsolateWaiterCancellation(bool cancelCreator, bool disposeFactory)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var initializations = 0;
        using var server = WireMock.Server.WireMockServer.Start();
        server.Given(WireMock.RequestBuilders.Request.Create().WithPath("/mcp").UsingPost())
            .RespondWith(WireMock.ResponseBuilders.Response.Create().WithCallback(async request =>
            {
                var body = System.Text.Json.Nodes.JsonNode.Parse(request.Body!)!;
                if (body["method"]!.GetValue<string>() != "initialize")
                    return new WireMock.ResponseMessage { StatusCode = 202 };
                Interlocked.Increment(ref initializations);
                entered.TrySetResult();
                await release.Task;
                return new WireMock.ResponseMessage
                {
                    StatusCode = 200,
                    Headers = new Dictionary<string, WireMock.Types.WireMockList<string>>
                        { ["Content-Type"] = new("application/json") },
                    BodyData = new WireMock.Util.BodyData
                    {
                        DetectedBodyType = WireMock.Types.BodyType.String,
                        BodyAsString = System.Text.Json.JsonSerializer.Serialize(new
                        {
                            jsonrpc = "2.0", id = body["id"],
                            result = new { protocolVersion = "2025-11-25", capabilities = new { },
                                serverInfo = new { name = "owned-waiter-fixture", version = "1" } }
                        })
                    }
                };
            }));
        var context = new RunNetworkPolicyContext();
        context.Configure(OperationPolicySnapshot.From(new ExecutionPolicy { AllowPrivateAddresses = true }));
        using var provider = new GovernedHttpClientProvider(new GovernedConnectionFactory(context,
            new DnsHostAddressResolver(), new SocketEndpointConnector()));
        await using var factory = new McpClientFactory(NullLogger<McpClientFactory>.Instance, NullLoggerFactory.Instance, provider);
        var config = new McpServerConfig { Endpoint = server.Urls[0] + "/mcp", Transport = "http" };
        using var firstCancellation = new CancellationTokenSource();
        using var secondCancellation = new CancellationTokenSource();
        var first = factory.GetOrCreateClientAsync(config, null, "2025-11-25", firstCancellation.Token);
        Task<McpClient>? second = null;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            second = factory.GetOrCreateClientAsync(config, null, "2025-11-25", secondCancellation.Token);
            if (disposeFactory)
            {
                await factory.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
                await FluentActions.Awaiting(() => first).Should().ThrowAsync<OperationCanceledException>();
                await FluentActions.Awaiting(() => second).Should().ThrowAsync<OperationCanceledException>();
                await FluentActions.Awaiting(() => factory.GetOrCreateClientAsync(config, null, "2025-11-25"))
                    .Should().ThrowAsync<ObjectDisposedException>();
                initializations.Should().Be(1);
                return;
            }
            (cancelCreator ? firstCancellation : secondCancellation).Cancel();
            var cancelled = cancelCreator ? first : second;
            var survivor = cancelCreator ? second : first;
            await FluentActions.Awaiting(() => cancelled.WaitAsync(TimeSpan.FromSeconds(10)))
                .Should().ThrowAsync<OperationCanceledException>();
            release.TrySetResult();
            await survivor.WaitAsync(TimeSpan.FromSeconds(10));
            initializations.Should().Be(1);
            await FluentActions.Awaiting(() => factory.GetOrCreateClientAsync(config, null, "2025-11-25",
                new CancellationToken(canceled: true))).Should().ThrowAsync<OperationCanceledException>();
        }
        finally
        {
            release.TrySetResult();
            try { await Task.WhenAll(second is null ? [first] : [first, second]); }
            catch (OperationCanceledException) { }
        }
    }

    [Fact]
    public async Task GetOrCreateClientAsync_ShouldUseOneCredentialSnapshotForKeyAndTransport()
    {
        using var server = WireMock.Server.WireMockServer.Start();
        server.Given(WireMock.RequestBuilders.Request.Create().WithPath("/mcp").UsingPost())
            .RespondWith(WireMock.ResponseBuilders.Response.Create().WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"protocolVersion\":\"2025-11-25\",\"capabilities\":{},\"serverInfo\":{\"name\":\"owned-cache-fixture\",\"version\":\"1\"}}}"));
        var authentication = new AuthenticationConfig { Type = "bearer", Token = "first-canary" };
        var headers = new CallbackHeaders(() => authentication.Token = "second-canary") { ["X-Route"] = "selected" };
        var context = new RunNetworkPolicyContext();
        context.Configure(OperationPolicySnapshot.From(new ExecutionPolicy { AllowPrivateAddresses = true }));
        using var provider = new GovernedHttpClientProvider(new GovernedConnectionFactory(context,
            new DnsHostAddressResolver(), new SocketEndpointConnector()));
        await using var factory = new McpClientFactory(NullLogger<McpClientFactory>.Instance, NullLoggerFactory.Instance, provider);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        await factory.GetOrCreateClientAsync(new McpServerConfig
            { Endpoint = server.Urls[0] + "/mcp", Transport = "http", Headers = headers, Authentication = authentication },
            null, "2025-11-25", deadline.Token);

        authentication.Token.Should().Be("second-canary");
        var initialize = server.LogEntries.First(entry => entry.RequestMessage?.Body?.Contains("initialize", StringComparison.Ordinal) == true);
        initialize.RequestMessage!.Headers!["Authorization"].Should().Equal("Bearer first-canary");
    }

    [Fact]
    public async Task GetOrCreateClientAsync_ShouldRejectUriCredentialsWithoutLoggingCanaries()
    {
        const string endpoint = "https://synthetic-user:synthetic-password@example.test/mcp?token=synthetic-query";
        var context = new RunNetworkPolicyContext();
        context.Configure(OperationPolicySnapshot.From(new ExecutionPolicy { AllowedHosts = ["example.test"] }));
        var resolver = new RejectingResolver();
        var connector = new RejectingConnector();
        using var provider = new GovernedHttpClientProvider(new GovernedConnectionFactory(context, resolver, connector));
        var logger = new Mock<ILogger<McpClientFactory>>();
        await using var factory = new McpClientFactory(logger.Object, NullLoggerFactory.Instance, provider);

        await FluentActions.Awaiting(() => factory.GetOrCreateClientAsync(
            new McpServerConfig { Endpoint = endpoint, Transport = "http" }, null, "2025-11-25"))
            .Should().ThrowAsync<Exception>();

        resolver.Calls.Should().Be(0);
        connector.Calls.Should().Be(0);
        var logs = string.Join(" ", logger.Invocations.SelectMany(invocation => invocation.Arguments).Select(value => value?.ToString()));
        logs.Should().NotContain("synthetic-user").And.NotContain("synthetic-password").And.NotContain("synthetic-query");
    }

    [Theory]
    [InlineData("Bearer selected-canary", null, "Bearer selected-canary")]
    [InlineData("Bearer selected-canary", "typed-canary", "Bearer typed-canary")]
    [InlineData(null, null, null)]
    public void ApplyAuthentication_ShouldPreserveOnlySelectedAuthorization(string? custom, string? token, string? expected)
    {
        var authentication = new AuthenticationConfig
        {
            Type = "bearer", Token = token,
            CustomHeaders = custom is null ? [] : new Dictionary<string, string> { ["authorization"] = custom }
        };
        var build = typeof(McpClientFactory).GetMethod("BuildHeaders", BindingFlags.NonPublic | BindingFlags.Static)!;
        var apply = typeof(McpClientFactory).GetMethod("ApplyAuthentication", BindingFlags.NonPublic | BindingFlags.Static)!;
        var headers = (IDictionary<string, string>)build.Invoke(null,
            [new Dictionary<string, string> { ["Authorization"] = "Bearer ambient-canary" }, authentication.CustomHeaders])!;

        apply.Invoke(null, [headers, authentication]);

        if (expected is null) headers.Should().NotContainKey("Authorization");
        else headers.Should().Contain("Authorization", expected);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CreateCacheKey_ShouldDistinguishEffectiveRoutingAndDelimiterValues(bool delimiterCollision)
    {
        var method = typeof(McpClientFactory).GetMethod("CreateCacheKey", BindingFlags.NonPublic | BindingFlags.Static)!;
        var firstDefaults = delimiterCollision
            ? new Dictionary<string, string> { ["a"] = "foo|b=bar" }
            : new Dictionary<string, string> { ["X-Tenant-Route"] = "first" };
        var secondDefaults = delimiterCollision
            ? new Dictionary<string, string> { ["a"] = "foo", ["b"] = "bar" }
            : new Dictionary<string, string> { ["X-Tenant-Route"] = "second" };
        IDictionary<string, string>? firstAuthenticationHeaders = delimiterCollision
            ? null : new Dictionary<string, string> { ["X-Tenant-Route"] = "second" };
        IDictionary<string, string>? secondAuthenticationHeaders = delimiterCollision
            ? null : new Dictionary<string, string> { ["X-Tenant-Route"] = "first" };

        var first = method.Invoke(null, ["https://example.test/mcp", "2025-11-25", null, firstDefaults, firstAuthenticationHeaders]);
        var second = method.Invoke(null, ["https://example.test/mcp", "2025-11-25", null, secondDefaults, secondAuthenticationHeaders]);

        first.Should().NotBe(second);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BuildHeaders_ShouldKeepRoutingButExcludeConfiguredProtocolIdentity(bool authenticationHeaders)
    {
        var configured = new Dictionary<string, string>
        {
            ["mcp-session-id"] = "synthetic-unowned-session",
            ["MCP-Method"] = "tools/call",
            ["mcp-protocol-version"] = "2099-01-01",
            ["X-Tenant-Route"] = "selected-route"
        };
        var method = typeof(McpClientFactory).GetMethod("BuildHeaders", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();
        var headers = (IDictionary<string, string>)method!.Invoke(null,
            authenticationHeaders ? [null, configured] : [configured, null])!;

        headers.Should().Contain("X-Tenant-Route", "selected-route");
        headers.Should().NotContainKey("MCP-Session-Id");
        headers.Should().NotContainKey("MCP-Method");
        headers.Should().NotContainKey("MCP-Protocol-Version");
    }

    [Fact]
    public void CreateCacheKey_ShouldDistinguishCredentialFieldBoundaries()
    {
        var method = typeof(McpClientFactory).GetMethod("CreateCacheKey", BindingFlags.NonPublic | BindingFlags.Static)!;
        var first = new AuthenticationConfig { Type = "bearer", Token = "first|scope", Username = "user", Password = "pass" };
        var second = new AuthenticationConfig { Type = "bearer", Token = "first", Username = "scope|user", Password = "pass" };

        method.Invoke(null, ["https://example.test/mcp", "2025-11-25", first, null, null]).Should().NotBe(
            method.Invoke(null, ["https://example.test/mcp", "2025-11-25", second, null, null]));
    }

    [Fact]
    public void CreateCacheKey_DoesNotRetainRawCredentials()
    {
        const string token = "token-canary-not-for-storage";
        const string password = "password-canary-not-for-storage";
        const string sensitiveHeader = "header-canary-not-for-storage";
        var authentication = new AuthenticationConfig
        {
            Type = "bearer",
            Token = token,
            Username = "test-user",
            Password = password
        };
        var headers = new Dictionary<string, string>
        {
            ["X-Sensitive-Test"] = sensitiveHeader
        };
        var method = typeof(McpClientFactory).GetMethod("CreateCacheKey", BindingFlags.NonPublic | BindingFlags.Static);

        method.Should().NotBeNull();
        var key = method!.Invoke(null, ["https://example.test/mcp", "2025-11-25", authentication, headers, null]);
        var storedRepresentation = key?.ToString();

        storedRepresentation.Should().NotBeNull();
        storedRepresentation.Should().NotContain(token);
        storedRepresentation.Should().NotContain(password);
        storedRepresentation.Should().NotContain(sensitiveHeader);
    }

    [Theory]
    [InlineData(McpProtocolEraSelection.Auto, "2026-07-28", HttpTransportMode.AutoDetect)]
    [InlineData(McpProtocolEraSelection.Modern, "2026-07-28", HttpTransportMode.StreamableHttp)]
    [InlineData(McpProtocolEraSelection.Legacy, "2025-11-25", HttpTransportMode.AutoDetect)]
    [InlineData(McpProtocolEraSelection.Legacy, "2024-11-05", HttpTransportMode.Sse)]
    public void ResolveHttpTransportMode_ShouldRespectEraAndProtocolProfile(
        McpProtocolEraSelection selection,
        string protocolVersion,
        HttpTransportMode expected)
    {
        var server = new McpServerConfig
        {
            ProtocolEra = selection,
            ProtocolVersion = protocolVersion
        };

        McpClientFactory.ResolveHttpTransportMode(server).Should().Be(expected);
    }

    [Fact]
    public void ResolveToken_EnvironmentReference_ShouldResolveTransientlyWithDirectTokenPrecedence()
    {
        const string variableName = "MCPVAL_TEST_SECRET_REF";
        var previous = Environment.GetEnvironmentVariable(variableName);
        Environment.SetEnvironmentVariable(variableName, "environment-secret");
        try
        {
            var authentication = new AuthenticationConfig
            {
                Type = "bearer",
                TokenRef = new SecretRef { Provider = SecretRefProviders.Environment, Name = variableName }
            };

            McpAuthenticationHelper.ResolveToken(authentication).Should().Be("environment-secret");
            authentication.Token = "direct-secret";
            McpAuthenticationHelper.ResolveToken(authentication).Should().Be("direct-secret");
        }
        finally
        {
            Environment.SetEnvironmentVariable(variableName, previous);
        }
    }

    private static void SeedOwnedClient(McpClientFactory factory, McpClient client, IAsyncDisposable transport, bool initializationCleanupFailure = false)
    {
        var entryType = typeof(McpClientFactory).GetNestedType("CachedClient", BindingFlags.NonPublic)!;
        var entry = Activator.CreateInstance(entryType, [client, transport])!;
        var taskType = typeof(Task<>).MakeGenericType(entryType);
        var cleanupErrorType = typeof(McpClientFactory).GetNestedType("SdkClientCleanupException", BindingFlags.NonPublic)!;
        var completion = initializationCleanupFailure ? Activator.CreateInstance(cleanupErrorType, ["transport"])! : entry;
        var task = typeof(Task).GetMethods().Single(method => method.IsGenericMethodDefinition &&
            method.Name == (initializationCleanupFailure ? nameof(Task.FromException) : nameof(Task.FromResult)))
            .MakeGenericMethod(entryType).Invoke(null, [completion])!;
        var create = System.Linq.Expressions.Expression.Lambda(typeof(Func<>).MakeGenericType(taskType),
            System.Linq.Expressions.Expression.Constant(task, taskType)).Compile();
        var lazyType = typeof(Lazy<>).MakeGenericType(taskType);
        var lazy = Activator.CreateInstance(lazyType, [create])!;
        _ = lazyType.GetProperty("Value")!.GetValue(lazy);
        var key = typeof(McpClientFactory).GetMethod("CreateCacheKey", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, ["https://example.test/mcp", "2025-11-25", null, null, null]);
        var cache = typeof(McpClientFactory).GetField("_clients", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(factory)!;
        ((bool)cache.GetType().GetMethod("TryAdd")!.Invoke(cache, [key, lazy])!).Should().BeTrue();
    }

    private sealed class TrackingDisposable : IAsyncDisposable
    {
        public bool Fail { get; init; }
        public Task? Pending { get; init; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Calls { get; private set; }
        public ValueTask DisposeAsync()
        {
            Calls++;
            Entered.TrySetResult();
            if (Pending != null) return new ValueTask(Pending);
            return Fail ? ValueTask.FromException(new IOException("Synthetic transport cleanup failure.")) : ValueTask.CompletedTask;
        }
    }

    private sealed class CallbackHeaders(Action callback) : Dictionary<string, string>, IEnumerable<KeyValuePair<string, string>>
    {
        private Action? _callback = callback;
        IEnumerator<KeyValuePair<string, string>> IEnumerable<KeyValuePair<string, string>>.GetEnumerator()
        {
            Interlocked.Exchange(ref _callback, null)?.Invoke();
            return base.GetEnumerator();
        }
    }

    private sealed class RejectingResolver : IHostAddressResolver
    {
        public int Calls { get; private set; }
        public Task<IReadOnlyList<System.Net.IPAddress>> ResolveAsync(string host, CancellationToken cancellationToken)
        {
            Calls++;
            throw new InvalidOperationException("Resolver must not execute.");
        }
    }

    private sealed class RejectingConnector : IEndpointConnector
    {
        public int Calls { get; private set; }
        public ValueTask<Stream> ConnectAsync(System.Net.IPAddress address, int port, CancellationToken cancellationToken)
        {
            Calls++;
            throw new InvalidOperationException("Connector must not execute.");
        }
    }
}