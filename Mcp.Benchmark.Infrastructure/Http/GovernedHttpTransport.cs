using System.Net;
using System.Net.Sockets;
using System.Net.Http.Headers;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Services;
using Mcp.Benchmark.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Mcp.Benchmark.Infrastructure.Http;

internal interface IHostAddressResolver
{
    Task<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken cancellationToken);
}

internal interface IEndpointConnector
{
    ValueTask<Stream> ConnectAsync(IPAddress address, int port, CancellationToken cancellationToken);
}

internal sealed class DnsHostAddressResolver : IHostAddressResolver
{
    public async Task<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken cancellationToken)
    {
        return await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false);
    }
}

internal sealed class SocketEndpointConnector : IEndpointConnector
{
    public async ValueTask<Stream> ConnectAsync(IPAddress address, int port, CancellationToken cancellationToken)
    {
        var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        try
        {
            await socket.ConnectAsync(new IPEndPoint(address, port), cancellationToken).ConfigureAwait(false);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}

internal sealed class RunNetworkPolicyContext
{
    private OperationPolicySnapshot? _snapshot;
    private int _requestCount;

    public OperationPolicySnapshot Snapshot =>
        Volatile.Read(ref _snapshot) ?? throw new InvalidOperationException("HTTP connection attempted before execution policy admission.");

    public void Configure(OperationPolicySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        Volatile.Write(ref _snapshot, snapshot);
        Interlocked.Exchange(ref _requestCount, 0);
    }

    public void AdmitRequest()
    {
        var policy = Snapshot;
        var requestNumber = Interlocked.Increment(ref _requestCount);
        if (requestNumber > policy.MaxRequests)
        {
            throw new InvalidOperationException($"Execution request budget exceeded ({policy.MaxRequests}).");
        }
    }
}

internal sealed class GovernedConnectionFactory(
    RunNetworkPolicyContext policyContext,
    IHostAddressResolver resolver,
    IEndpointConnector connector)
{
    internal RunNetworkPolicyContext PolicyContext { get; } = policyContext;

    public async ValueTask<Stream> ConnectAsync(string host, int port, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        if (port is < IPEndPoint.MinPort or > IPEndPoint.MaxPort)
        {
            throw new ArgumentOutOfRangeException(nameof(port));
        }

        var normalizedHost = NetworkTargetPolicy.NormalizeHost(host);
        var policy = PolicyContext.Snapshot;
        if (policy.AllowedHosts.Count > 0 && !policy.AllowedHosts.Contains(normalizedHost))
        {
            throw new InvalidOperationException($"Execution policy blocked outbound connection to host '{normalizedHost}'.");
        }

        var addresses = await resolver.ResolveAsync(normalizedHost, cancellationToken).ConfigureAwait(false);
        if (addresses.Count == 0)
        {
            throw new InvalidOperationException($"Execution policy blocked host '{normalizedHost}' because it did not resolve to an address.");
        }

        if (!policy.AllowPrivateAddresses && addresses.Any(NetworkAddressClassifier.IsRestricted))
        {
            throw new InvalidOperationException($"Execution policy blocked private, local, or reserved address resolution for host '{normalizedHost}'.");
        }

        Exception? lastError = null;
        foreach (var address in addresses.OrderBy(value => value.ToString(), StringComparer.Ordinal))
        {
            try
            {
                return await connector.ConnectAsync(address, port, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex is SocketException or IOException)
            {
                lastError = ex;
            }
        }

        throw new HttpRequestException($"Unable to connect to an approved address for host '{normalizedHost}'.", lastError);
    }
}

internal sealed class GovernedHttpClientProvider : IDisposable
{
    public GovernedHttpClientProvider(GovernedConnectionFactory connectionFactory)
    {
        var socketsHandler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseProxy = false,
            ConnectCallback = (context, cancellationToken) =>
                connectionFactory.ConnectAsync(context.DnsEndPoint.Host, context.DnsEndPoint.Port, cancellationToken)
        };
        var policyHandler = new GovernedRequestPolicyHandler(connectionFactory.PolicyContext)
        {
            InnerHandler = socketsHandler
        };
        var observabilityHandler = new GovernedRequestObservabilityHandler
        {
            InnerHandler = policyHandler
        };
        Client = new HttpClient(observabilityHandler, disposeHandler: true)
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        Client.DefaultRequestHeaders.UserAgent.ParseAdd("Visual-Studio-Code/1.96.0 mcp-compliance-validator/1.0.0");
    }

    public HttpClient Client { get; }

    public void Dispose()
    {
        Client.Dispose();
    }
}

internal sealed class GovernedRequestObservabilityHandler : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ValidationObservability.RecordRequestStarted(request.Method.Method);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            ValidationObservability.RecordRequestCompleted(
                request.Method.Method,
                stopwatch.Elapsed.TotalMilliseconds,
                response.IsSuccessStatusCode);
            return response;
        }
        catch
        {
            ValidationObservability.RecordRequestCompleted(
                request.Method.Method,
                stopwatch.Elapsed.TotalMilliseconds,
                success: false);
            throw;
        }
    }
}

internal sealed class GovernedRequestPolicyHandler(RunNetworkPolicyContext policyContext) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uri = request.RequestUri ?? throw new InvalidOperationException("Outbound HTTP request is missing an absolute URI.");
        if (!uri.IsAbsoluteUri || uri.Scheme is not ("http" or "https"))
        {
            throw new InvalidOperationException("Execution policy allows only absolute HTTP or HTTPS request URIs.");
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new InvalidOperationException("Execution policy blocked a request URI containing user-info.");
        }

        var policy = policyContext.Snapshot;
        var normalizedHost = NetworkTargetPolicy.NormalizeHost(uri.IdnHost);
        if (policy.AllowedHosts.Count > 0 && !policy.AllowedHosts.Contains(normalizedHost))
        {
            throw new InvalidOperationException($"Execution policy blocked outbound request to host '{normalizedHost}'.");
        }

        var origin = NetworkTargetPolicy.NormalizeOrigin(uri);
        if (policy.AllowedOrigins.Count > 0 && !policy.AllowedOrigins.Contains(origin))
        {
            throw new InvalidOperationException($"Execution policy blocked outbound request to origin '{origin}'.");
        }

        policyContext.AdmitRequest();
        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var maxResponseBytes = policy.MaxResponseBytes;
        if (response.Content.Headers.ContentLength is { } declaredLength && declaredLength > maxResponseBytes)
        {
            ValidationObservability.RecordResponseRejected(responseWasInitiallySuccessful: false);
            response.Dispose();
            throw new InvalidDataException(
            $"Response body exceeds configured limit of {maxResponseBytes} bytes (Content-Length: {declaredLength}).");
        }

        response.Content = new BoundedHttpContent(response.Content, maxResponseBytes, response.IsSuccessStatusCode);
        return response;
    }
}

internal sealed class BoundedHttpContent : HttpContent
{
    private readonly HttpContent _inner;
    private readonly int _maxBytes;
    private readonly bool _responseWasInitiallySuccessful;

    public BoundedHttpContent(HttpContent inner, int maxBytes, bool responseWasInitiallySuccessful)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _maxBytes = maxBytes > 0 ? maxBytes : throw new ArgumentOutOfRangeException(nameof(maxBytes));
        _responseWasInitiallySuccessful = responseWasInitiallySuccessful;
        foreach (var header in inner.Headers)
        {
            Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
    }

    protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
    {
        await using var source = await CreateContentReadStreamAsync().ConfigureAwait(false);
        await source.CopyToAsync(stream).ConfigureAwait(false);
    }

    protected override bool TryComputeLength(out long length)
    {
        if (_inner.Headers.ContentLength is { } contentLength && contentLength <= _maxBytes)
        {
            length = contentLength;
            return true;
        }

        length = 0;
        return false;
    }

    protected override async Task<Stream> CreateContentReadStreamAsync()
    {
        var source = await _inner.ReadAsStreamAsync().ConfigureAwait(false);
        return new BoundedReadStream(source, _maxBytes, _responseWasInitiallySuccessful);
    }

    protected override async Task<Stream> CreateContentReadStreamAsync(CancellationToken cancellationToken)
    {
        var source = await _inner.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return new BoundedReadStream(source, _maxBytes, _responseWasInitiallySuccessful);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _inner.Dispose();
        }

        base.Dispose(disposing);
    }
}

internal sealed class BoundedReadStream(Stream inner, int maxBytes, bool responseWasInitiallySuccessful) : Stream
{
    private long _bytesRead;
    private int _rejectionRecorded;

    public override bool CanRead => inner.CanRead;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => _bytesRead; set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override int Read(byte[] buffer, int offset, int count) =>
        ReadCore(buffer.AsSpan(offset, count));
    public override int Read(Span<byte> buffer) => ReadCore(buffer);

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var remaining = maxBytes - _bytesRead;
        var read = await inner.ReadAsync(buffer[..Math.Min(buffer.Length, checked((int)Math.Min(remaining + 1, int.MaxValue)))], cancellationToken).ConfigureAwait(false);
        return ValidateRead(read);
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    private int ReadCore(Span<byte> buffer)
    {
        var remaining = maxBytes - _bytesRead;
        var read = inner.Read(buffer[..Math.Min(buffer.Length, checked((int)Math.Min(remaining + 1, int.MaxValue)))]);
        return ValidateRead(read);
    }

    private int ValidateRead(int read)
    {
        _bytesRead += read;
        if (_bytesRead > maxBytes)
        {
            if (Interlocked.Exchange(ref _rejectionRecorded, 1) == 0)
            {
                ValidationObservability.RecordResponseRejected(responseWasInitiallySuccessful);
            }
            throw new InvalidDataException($"Response body exceeds configured limit of {maxBytes} bytes.");
        }

        return read;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            inner.Dispose();
        }

        base.Dispose(disposing);
    }

    public override ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return inner.DisposeAsync();
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

public static class GovernedMcpHttpTransportServiceCollectionExtensions
{
    public static IServiceCollection AddGovernedMcpHttpTransport(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IHostAddressResolver, DnsHostAddressResolver>();
        services.AddSingleton<IEndpointConnector, SocketEndpointConnector>();
        services.AddScoped<RunNetworkPolicyContext>();
        services.AddScoped<GovernedConnectionFactory>();
        services.AddScoped<GovernedHttpClientProvider>();
        services.AddScoped<IMcpClientFactory>(provider => new McpClientFactory(
            provider.GetRequiredService<ILogger<McpClientFactory>>(),
            provider.GetRequiredService<ILoggerFactory>(),
            provider.GetRequiredService<GovernedHttpClientProvider>()));
        services.AddScoped<IMcpClient, SdkMcpClient>();
        services.AddScoped(provider => new McpHttpClient(
            provider.GetRequiredService<GovernedHttpClientProvider>().Client,
            provider.GetRequiredService<ILogger<McpHttpClient>>(),
            provider.GetRequiredService<IMcpClient>(),
            provider.GetRequiredService<RunNetworkPolicyContext>()));

        return services;
    }
}