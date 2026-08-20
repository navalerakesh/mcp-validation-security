using System.Net;
using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Services;
using Mcp.Benchmark.Infrastructure.Http;
using Microsoft.Extensions.Logging;

namespace Mcp.Benchmark.Tests.Unit.Http;

public sealed class McpHttpClientConcurrencyTests
{
    [Fact]
    public async Task GetStringAsync_PrivateResolution_DoesNotReachHttpHandler()
    {
        var handler = new RecordingHandler();
        using var httpClient = new HttpClient(handler);
        var client = new McpHttpClient(httpClient, Mock.Of<ILogger<McpHttpClient>>(), Mock.Of<IMcpClient>());
        client.ConfigureExecutionPolicy(new ExecutionPolicy
        {
            AllowedHosts = ["localhost"],
            AllowPrivateAddresses = false
        });

        var action = () => client.GetStringAsync("http://localhost/oauth-metadata");

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*private, local, or reserved*");
        handler.RequestCount.Should().Be(0);
    }

    [Fact]
    public async Task CallAsync_CancelledWaiter_DoesNotReleaseAnotherRequestsPermit()
    {
        using var telemetry = ValidationObservability.BeginRun("cancelled-waiter", 10);
        var handler = new BlockingHandler();
        using var httpClient = new HttpClient(handler);
        var client = new McpHttpClient(httpClient, Mock.Of<ILogger<McpHttpClient>>(), Mock.Of<IMcpClient>());
        client.SetConcurrencyLimit(1);

        var first = client.CallAsync("https://example.test/mcp", "first", null, CancellationToken.None);
        await handler.FirstRequestEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));

        using var cancelledWaiter = new CancellationTokenSource();
        var second = client.CallAsync("https://example.test/mcp", "second", null, cancelledWaiter.Token);
        cancelledWaiter.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second);

        var third = client.CallAsync("https://example.test/mcp", "third", null, CancellationToken.None);
        await Task.Delay(TimeSpan.FromMilliseconds(100));

        handler.MaximumConcurrentRequests.Should().Be(1);
        handler.ReleaseFirstRequest.TrySetResult();

        await Task.WhenAll(first, third).WaitAsync(TimeSpan.FromSeconds(2));
        handler.MaximumConcurrentRequests.Should().Be(1);
        telemetry.Complete(100).QueueTimeP95Ms.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ConfigureExecutionPolicy_CapturesImmutableNormalizedSnapshot()
    {
        var handler = new RecordingHandler();
        using var httpClient = new HttpClient(handler);
        var client = new McpHttpClient(httpClient, Mock.Of<ILogger<McpHttpClient>>(), Mock.Of<IMcpClient>());
        var policy = new ExecutionPolicy
        {
            AllowedHosts = ["example.test"],
            AllowPrivateAddresses = true,
            MaxRequests = 1
        };

        client.ConfigureExecutionPolicy(policy);
        policy.MaxRequests = 10;
        policy.AllowedHosts.Clear();

        await client.GetStringAsync("https://example.test/first");
        var secondRequest = () => client.GetStringAsync("https://example.test/second");

        await secondRequest.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*request budget exceeded (1)*");
        handler.RequestCount.Should().Be(1);
    }

    [Fact]
    public async Task CallAsync_ResponseExceedsConfiguredLimit_ReturnsExplicitFailureEvidence()
    {
        using var telemetry = ValidationObservability.BeginRun("oversized-response", 10);
        var handler = new OversizedResponseHandler();
        using var httpClient = new HttpClient(handler);
        var client = new McpHttpClient(httpClient, Mock.Of<ILogger<McpHttpClient>>(), Mock.Of<IMcpClient>());
        client.ConfigureExecutionPolicy(new ExecutionPolicy
        {
            AllowedHosts = ["example.test"],
            AllowPrivateAddresses = true,
            MaxResponseBytes = 32
        });

        var response = await client.CallAsync("https://example.test/mcp", "tools/list");

        response.IsSuccess.Should().BeFalse();
        response.RawJson.Should().BeNull();
        response.Error.Should().Contain("configured limit of 32 bytes");
        var metrics = telemetry.Complete(100);
        metrics.TruncatedResponseCount.Should().Be(1);
        metrics.RequestsFailed.Should().Be(1);
        metrics.ErrorRate.Should().Be(1);
    }

    [Fact]
    public async Task CallAsync_SlowResponseStream_ShouldHonorCallerCancellation()
    {
        using var httpClient = new HttpClient(new SlowResponseHandler());
        var client = new McpHttpClient(httpClient, Mock.Of<ILogger<McpHttpClient>>(), Mock.Of<IMcpClient>());
        client.ConfigureExecutionPolicy(new ExecutionPolicy
        {
            AllowedHosts = ["example.test"],
            AllowPrivateAddresses = true
        });
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        var action = () => client.CallAsync("https://example.test/mcp", "tools/list", null, cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task GetStringAsync_ResponseExceedsConfiguredLimit_ThrowsExplicitPolicyFailure()
    {
        var handler = new OversizedResponseHandler();
        using var httpClient = new HttpClient(handler);
        var client = new McpHttpClient(httpClient, Mock.Of<ILogger<McpHttpClient>>(), Mock.Of<IMcpClient>());
        client.ConfigureExecutionPolicy(new ExecutionPolicy
        {
            AllowedHosts = ["example.test"],
            AllowPrivateAddresses = true,
            MaxResponseBytes = 32
        });

        var action = () => client.GetStringAsync("https://example.test/oauth-metadata");

        await action.Should().ThrowAsync<InvalidDataException>()
            .WithMessage("*configured limit of 32 bytes*");
    }

    [Fact]
    public async Task SetAuthentication_MetadataGetShouldRemainUnauthenticatedWhileMcpCallUsesToken()
    {
        var handler = new AuthenticationCaptureHandler();
        using var httpClient = new HttpClient(handler);
        var client = new McpHttpClient(httpClient, Mock.Of<ILogger<McpHttpClient>>(), Mock.Of<IMcpClient>());
        client.ConfigureExecutionPolicy(new ExecutionPolicy
        {
            AllowedHosts = ["example.test"],
            AllowedOrigins = ["https://example.test"],
            AllowPrivateAddresses = true
        });
        client.SetAuthentication(new AuthenticationConfig { Type = "bearer", Token = "token-canary" });

        await client.GetStringAsync("https://example.test/.well-known/oauth-authorization-server");
        await client.CallAsync("https://example.test/mcp", "tools/list", null, CancellationToken.None);

        handler.AuthorizationSchemes.Should().Equal(null, "Bearer");
        handler.AuthorizationParameters.Should().Equal(null, "token-canary");
    }

    [Theory]
    [InlineData(HttpStatusCode.NoContent, "application/json")]
    [InlineData(HttpStatusCode.OK, "text/plain")]
    public async Task GetStringAsync_NonconformantMetadataResponse_ShouldFail(
        HttpStatusCode statusCode,
        string mediaType)
    {
        using var httpClient = new HttpClient(new MetadataResponseHandler(statusCode, mediaType));
        var client = new McpHttpClient(httpClient, Mock.Of<ILogger<McpHttpClient>>(), Mock.Of<IMcpClient>());
        client.ConfigureExecutionPolicy(new ExecutionPolicy
        {
            AllowedHosts = ["example.test"],
            AllowPrivateAddresses = true
        });

        var action = () => client.GetStringAsync("https://example.test/.well-known/oauth-protected-resource");

        await action.Should().ThrowAsync<Exception>();
    }

    private sealed class BlockingHandler : HttpMessageHandler
    {
        private int _activeRequests;
        private int _maximumConcurrentRequests;
        private int _requestNumber;

        public TaskCompletionSource FirstRequestEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseFirstRequest { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int MaximumConcurrentRequests => Volatile.Read(ref _maximumConcurrentRequests);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var active = Interlocked.Increment(ref _activeRequests);
            UpdateMaximum(active);
            var requestNumber = Interlocked.Increment(ref _requestNumber);

            try
            {
                if (requestNumber == 1)
                {
                    FirstRequestEntered.TrySetResult();
                    await ReleaseFirstRequest.Task.WaitAsync(cancellationToken);
                }

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"jsonrpc\":\"2.0\",\"result\":{},\"id\":\"test\"}")
                };
            }
            finally
            {
                Interlocked.Decrement(ref _activeRequests);
            }
        }

        private void UpdateMaximum(int candidate)
        {
            var observed = Volatile.Read(ref _maximumConcurrentRequests);
            while (candidate > observed)
            {
                var previous = Interlocked.CompareExchange(ref _maximumConcurrentRequests, candidate, observed);
                if (previous == observed)
                {
                    return;
                }

                observed = previous;
            }
        }
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private int _requestCount;
        public int RequestCount => Volatile.Read(ref _requestCount);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _requestCount);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    private sealed class OversizedResponseHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var content = new StreamContent(new MemoryStream(new byte[64]));
            content.Headers.ContentLength = null;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }

    private sealed class SlowResponseHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(new SlowReadStream())
            });
        }
    }

    private sealed class SlowReadStream : MemoryStream
    {
        public SlowReadStream() : base("{\"jsonrpc\":\"2.0\",\"result\":{}}"u8.ToArray()) { }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            return await base.ReadAsync(buffer, cancellationToken);
        }
    }

    private sealed class AuthenticationCaptureHandler : HttpMessageHandler
    {
        public List<string?> AuthorizationSchemes { get; } = new();
        public List<string?> AuthorizationParameters { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            AuthorizationSchemes.Add(request.Headers.Authorization?.Scheme);
            AuthorizationParameters.Add(request.Headers.Authorization?.Parameter);
            var body = request.Method == HttpMethod.Get
                ? "{}"
                : "{\"jsonrpc\":\"2.0\",\"id\":\"test\",\"result\":{}}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class MetadataResponseHandler(HttpStatusCode statusCode, string mediaType) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent("{}", System.Text.Encoding.UTF8, mediaType)
            });
        }
    }
}