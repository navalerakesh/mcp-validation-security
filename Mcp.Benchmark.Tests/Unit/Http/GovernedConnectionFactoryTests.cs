using System.Net;
using System.Net.Sockets;
using System.Text;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Services;
using Mcp.Benchmark.Infrastructure.Http;

namespace Mcp.Benchmark.Tests.Unit.Http;

public sealed class GovernedConnectionFactoryTests
{
    [Theory]
    [InlineData("host")]
    [InlineData("mode")]
    [InlineData("budget")]
    [InlineData("cancelled")]
    public async Task GovernedObserver_ShouldCountOnlyAdmittedOutboundAttempts(string rejectedBy)
    {
        var context = new RunNetworkPolicyContext();
        context.Configure(OperationPolicySnapshot.From(new ExecutionPolicy
        {
            Mode = rejectedBy == "mode" ? ExecutionMode.Safe : ExecutionMode.Standard,
            AllowedHosts = ["example.test"], MaxRequests = 1
        }));
        var receiver = new RecordingHttpHandler();
        using var policy = new GovernedRequestPolicyHandler(context) { InnerHandler = receiver };
        using var observer = new GovernedRequestObservabilityHandler { InnerHandler = policy };
        using var invoker = new HttpMessageInvoker(observer);
        using var telemetry = ValidationObservability.BeginRun("governed-admission", 1);
        if (rejectedBy == "budget")
        {
            using var first = new HttpRequestMessage(HttpMethod.Get, "https://example.test/first");
            using var response = await invoker.SendAsync(first, CancellationToken.None);
        }
        using var request = new HttpRequestMessage(rejectedBy == "mode" ? HttpMethod.Post : HttpMethod.Get,
            rejectedBy == "host" ? "https://outside.test/mcp" : "https://example.test/mcp");
        using var cancellation = new CancellationTokenSource();
        if (rejectedBy == "cancelled") cancellation.Cancel();
        var send = () => invoker.SendAsync(request, cancellation.Token);
        if (rejectedBy == "cancelled") await send.Should().ThrowAsync<OperationCanceledException>();
        else await send.Should().ThrowAsync<InvalidOperationException>();
        var metrics = telemetry.Complete(1);
        var expected = rejectedBy == "budget" ? 1 : 0;
        receiver.RequestCount.Should().Be(expected);
        metrics.RequestsStarted.Should().Be(expected);
        metrics.RequestsCompleted.Should().Be(expected);
        metrics.RequestsFailed.Should().Be(0);
        metrics.TargetLatencySampleCount.Should().Be(expected);
        if (rejectedBy != "budget")
        {
            using var valid = new HttpRequestMessage(HttpMethod.Get, "https://example.test/valid");
            using var response = await invoker.SendAsync(valid, CancellationToken.None);
            receiver.RequestCount.Should().Be(1);
        }
    }

    [Fact]
    public async Task GovernedBudget_ShouldRemainSharedAcrossConcurrentClientInstances()
    {
        const int limit = 4;
        var context = new RunNetworkPolicyContext();
        context.Configure(OperationPolicySnapshot.From(new ExecutionPolicy
        { Mode = ExecutionMode.Standard, AllowedHosts = ["example.test"], MaxRequests = limit }));
        var receivers = new[] { new RecordingHttpHandler(), new RecordingHttpHandler() };
        using var firstPolicy = new GovernedRequestPolicyHandler(context) { InnerHandler = receivers[0] };
        using var secondPolicy = new GovernedRequestPolicyHandler(context) { InnerHandler = receivers[1] };
        using var firstObserver = new GovernedRequestObservabilityHandler { InnerHandler = firstPolicy };
        using var secondObserver = new GovernedRequestObservabilityHandler { InnerHandler = secondPolicy };
        using var first = new HttpMessageInvoker(firstObserver);
        using var second = new HttpMessageInvoker(secondObserver);
        using var telemetry = ValidationObservability.BeginRun("shared-budget", limit);
        var rejected = 0;
        await Task.WhenAll(Enumerable.Range(0, 32).Select(index => Task.Run(async () =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.test/mcp");
            try
            {
                using var response = await (index % 2 == 0 ? first : second).SendAsync(request, CancellationToken.None);
            }
            catch (Mcp.Benchmark.Core.Exceptions.OperationAdmissionException) { Interlocked.Increment(ref rejected); }
        })));
        receivers.Sum(receiver => receiver.RequestCount).Should().Be(limit);
        rejected.Should().Be(32 - limit);
        var metrics = telemetry.Complete(1);
        metrics.RequestsStarted.Should().Be(limit);
        metrics.RequestsCompleted.Should().Be(limit);
        metrics.RequestsFailed.Should().Be(0);
    }

    [Fact]
    public async Task ConnectAsync_RestrictedResolution_ShouldRejectWithoutOpeningSocket()
    {
        var resolver = new SequenceResolver([IPAddress.Loopback]);
        var connector = new RecordingConnector();
        var factory = CreateFactory(resolver, connector);

        var action = () => factory.ConnectAsync("example.test", 443, CancellationToken.None).AsTask();

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*private, local, or reserved*");
        connector.Addresses.Should().BeEmpty();
    }

    [Fact]
    public async Task ConnectAsync_MixedPublicAndRestrictedResolution_ShouldFailClosed()
    {
        var resolver = new SequenceResolver([IPAddress.Parse("8.8.8.8"), IPAddress.Loopback]);
        var connector = new RecordingConnector();
        var factory = CreateFactory(resolver, connector);

        var action = () => factory.ConnectAsync("example.test", 443, CancellationToken.None).AsTask();

        await action.Should().ThrowAsync<InvalidOperationException>();
        connector.Addresses.Should().BeEmpty();
    }

    [Fact]
    public async Task ConnectAsync_EachConnection_ShouldReResolveAndBlockRebinding()
    {
        var resolver = new SequenceResolver(
            [IPAddress.Parse("8.8.8.8")],
            [IPAddress.Loopback]);
        var connector = new RecordingConnector();
        var factory = CreateFactory(resolver, connector);

        await using var first = await factory.ConnectAsync("example.test", 443, CancellationToken.None);
        var rebound = () => factory.ConnectAsync("example.test", 443, CancellationToken.None).AsTask();

        await rebound.Should().ThrowAsync<InvalidOperationException>();
        resolver.CallCount.Should().Be(2);
        connector.Addresses.Should().Equal(IPAddress.Parse("8.8.8.8"));
    }

    [Fact]
    public async Task ConnectAsync_HostOutsideAllowlist_ShouldRejectBeforeResolution()
    {
        var resolver = new SequenceResolver([IPAddress.Parse("8.8.8.8")]);
        var connector = new RecordingConnector();
        var factory = CreateFactory(resolver, connector);

        var action = () => factory.ConnectAsync("other.test", 443, CancellationToken.None).AsTask();

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*blocked outbound connection*");
        resolver.CallCount.Should().Be(0);
        connector.Addresses.Should().BeEmpty();
    }

    [Fact]
    public async Task GovernedHttpClient_RedirectResponse_ShouldNotContactDestination()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var requestCount = 0;
        var serverTask = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            Interlocked.Increment(ref requestCount);
            await using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
            while (!string.IsNullOrEmpty(await reader.ReadLineAsync()))
            {
            }

            var response = Encoding.ASCII.GetBytes(
                $"HTTP/1.1 302 Found\r\nLocation: http://127.0.0.1:{port}/redirected\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(response);
        });
        var context = new RunNetworkPolicyContext();
        context.Configure(OperationPolicySnapshot.From(new ExecutionPolicy
        {
            AllowedHosts = ["127.0.0.1"],
            AllowPrivateAddresses = true
        }));
        var connectionFactory = new GovernedConnectionFactory(
            context,
            new DnsHostAddressResolver(),
            new SocketEndpointConnector());
        using var provider = new GovernedHttpClientProvider(connectionFactory);
        provider.Client.Timeout = TimeSpan.FromSeconds(2);

        using var responseMessage = await provider.Client.GetAsync($"http://127.0.0.1:{port}/start");
        await serverTask;

        responseMessage.StatusCode.Should().Be(HttpStatusCode.Found);
        requestCount.Should().Be(1);
    }

    [Theory]
    [InlineData("http://example.test/mcp")]
    [InlineData("https://example.test:8443/mcp")]
    [InlineData("https://user:password@example.test/mcp")]
    public async Task RequestPolicy_OriginOrUserInfoMismatch_ShouldRejectBeforeInnerHandler(string requestUri)
    {
        var context = new RunNetworkPolicyContext();
        context.Configure(OperationPolicySnapshot.From(new ExecutionPolicy
        {
            AllowedHosts = ["example.test"],
            AllowedOrigins = ["https://example.test"]
        }));
        var innerHandler = new RecordingHttpHandler();
        using var policyHandler = new GovernedRequestPolicyHandler(context) { InnerHandler = innerHandler };
        using var invoker = new HttpMessageInvoker(policyHandler);
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);

        var action = () => invoker.SendAsync(request, CancellationToken.None);

        await action.Should().ThrowAsync<InvalidOperationException>();
        innerHandler.RequestCount.Should().Be(0);
    }

    [Fact]
    public async Task RequestPolicy_ExactOrigin_ShouldReachInnerHandler()
    {
        var context = new RunNetworkPolicyContext();
        context.Configure(OperationPolicySnapshot.From(new ExecutionPolicy
        {
            AllowedHosts = ["example.test"],
            AllowedOrigins = ["https://example.test"]
        }));
        var innerHandler = new RecordingHttpHandler();
        using var policyHandler = new GovernedRequestPolicyHandler(context) { InnerHandler = innerHandler };
        using var invoker = new HttpMessageInvoker(policyHandler);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.test/mcp");

        using var response = await invoker.SendAsync(request, CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        innerHandler.RequestCount.Should().Be(1);
    }

    [Fact]
    public async Task GovernedObserver_AdmittedPost_ShouldRecordOneAttempt()
    {
        var context = new RunNetworkPolicyContext();
        context.Configure(OperationPolicySnapshot.From(new ExecutionPolicy
        {
            Mode = ExecutionMode.Standard,
            AllowedHosts = ["example.test"],
            AllowedOrigins = ["https://example.test"]
        }));
        var innerHandler = new RecordingHttpHandler();
        using var policyHandler = new GovernedRequestPolicyHandler(context) { InnerHandler = innerHandler };
        using var observer = new GovernedRequestObservabilityHandler { InnerHandler = policyHandler };
        using var invoker = new HttpMessageInvoker(observer);
        using var telemetry = ValidationObservability.BeginRun("governed-sdk", 1);
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://example.test/mcp");

        using var response = await invoker.SendAsync(request, CancellationToken.None);
        var metrics = telemetry.Complete(100);

        metrics.RequestsStarted.Should().Be(1);
        metrics.RequestsCompleted.Should().Be(1);
        metrics.RequestsFailed.Should().Be(0);
        metrics.TargetLatencySampleCount.Should().Be(1);
    }

    [Fact]
    public async Task GovernedObserver_HttpJsonRpcErrorEnvelope_ShouldCompleteTransport()
    {
        var context = new RunNetworkPolicyContext();
        context.Configure(OperationPolicySnapshot.From(new ExecutionPolicy
        {
            Mode = ExecutionMode.Standard,
            AllowedHosts = ["example.test"],
            AllowedOrigins = ["https://example.test"]
        }));
        var innerHandler = new JsonRpcErrorHttpHandler();
        using var policyHandler = new GovernedRequestPolicyHandler(context) { InnerHandler = innerHandler };
        using var observer = new GovernedRequestObservabilityHandler { InnerHandler = policyHandler };
        using var invoker = new HttpMessageInvoker(observer);
        using var telemetry = ValidationObservability.BeginRun("governed-json-rpc-error", 1);
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://example.test/mcp");

        using var response = await invoker.SendAsync(request, CancellationToken.None);
        response.Content.Should().NotBeNull();
        (await response.Content.ReadAsStringAsync()).Should().Contain("\"error\"");
        var metrics = telemetry.Complete(100);

        metrics.RequestsCompleted.Should().Be(1);
        metrics.RequestsFailed.Should().Be(0);
        metrics.ErrorRate.Should().Be(0);
    }

    [Fact]
    public async Task RequestPolicy_RawAndSdkRequests_ShouldShareOneRunBudget()
    {
        var context = new RunNetworkPolicyContext();
        context.Configure(OperationPolicySnapshot.From(new ExecutionPolicy
        {
            AllowedHosts = ["example.test"],
            AllowedOrigins = ["https://example.test"],
            MaxRequests = 1
        }));
        var innerHandler = new RecordingHttpHandler();
        using var policyHandler = new GovernedRequestPolicyHandler(context) { InnerHandler = innerHandler };
        using var invoker = new HttpMessageInvoker(policyHandler);
        using var firstRequest = new HttpRequestMessage(HttpMethod.Get, "https://example.test/first");
        using var secondRequest = new HttpRequestMessage(HttpMethod.Get, "https://example.test/second");

        using var firstResponse = await invoker.SendAsync(firstRequest, CancellationToken.None);
        var second = () => invoker.SendAsync(secondRequest, CancellationToken.None);

        await second.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*request budget exceeded (1)*");
        innerHandler.RequestCount.Should().Be(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RequestPolicy_OversizedSdkResponse_ShouldFailForKnownAndUnknownLength(bool declareLength)
    {
        var context = new RunNetworkPolicyContext();
        context.Configure(OperationPolicySnapshot.From(new ExecutionPolicy
        {
            AllowedHosts = ["example.test"],
            AllowedOrigins = ["https://example.test"],
            MaxResponseBytes = 32
        }));
        var innerHandler = new OversizedHttpHandler(declareLength);
        using var policyHandler = new GovernedRequestPolicyHandler(context) { InnerHandler = innerHandler };
        using var observer = new GovernedRequestObservabilityHandler { InnerHandler = policyHandler };
        using var invoker = new HttpMessageInvoker(observer);
        using var telemetry = ValidationObservability.BeginRun("governed-size-limit", 1);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.test/mcp");

        var action = async () =>
        {
            using var response = await invoker.SendAsync(request, CancellationToken.None);
            _ = await response.Content.ReadAsByteArrayAsync();
        };

        await action.Should().ThrowAsync<InvalidDataException>()
            .WithMessage("*configured limit of 32 bytes*");
        var metrics = telemetry.Complete(100);
        metrics.RequestsStarted.Should().Be(1);
        metrics.RequestsCompleted.Should().Be(1);
        metrics.RequestsFailed.Should().Be(1);
        metrics.TruncatedResponseCount.Should().Be(1);
        metrics.ErrorRate.Should().Be(1);
    }

    private static GovernedConnectionFactory CreateFactory(IHostAddressResolver resolver, IEndpointConnector connector)
    {
        var context = new RunNetworkPolicyContext();
        context.Configure(OperationPolicySnapshot.From(new ExecutionPolicy
        {
            AllowedHosts = ["example.test"],
            AllowPrivateAddresses = false
        }));
        return new GovernedConnectionFactory(context, resolver, connector);
    }

    private sealed class SequenceResolver(params IPAddress[][] answers) : IHostAddressResolver
    {
        private int _callCount;
        public int CallCount => Volatile.Read(ref _callCount);

        public Task<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken cancellationToken)
        {
            var index = Interlocked.Increment(ref _callCount) - 1;
            return Task.FromResult<IReadOnlyList<IPAddress>>(answers[Math.Min(index, answers.Length - 1)]);
        }
    }

    private sealed class RecordingConnector : IEndpointConnector
    {
        public List<IPAddress> Addresses { get; } = new();

        public ValueTask<Stream> ConnectAsync(IPAddress address, int port, CancellationToken cancellationToken)
        {
            Addresses.Add(address);
            return ValueTask.FromResult<Stream>(new MemoryStream());
        }
    }

    private sealed class RecordingHttpHandler : HttpMessageHandler
    {
        private int _requestCount;
        public int RequestCount => Volatile.Read(ref _requestCount);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _requestCount);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    private sealed class OversizedHttpHandler(bool declareLength) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var content = new StreamContent(new MemoryStream(new byte[64]));
            content.Headers.ContentLength = declareLength ? 64 : null;

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }

    private sealed class JsonRpcErrorHttpHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"jsonrpc\":\"2.0\",\"id\":1,\"error\":{\"code\":-32601,\"message\":\"Method not found\"}}")
            });
    }
}
