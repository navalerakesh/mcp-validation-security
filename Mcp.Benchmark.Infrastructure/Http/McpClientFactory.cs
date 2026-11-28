using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Constants;
using Mcp.Benchmark.Core.Exceptions;
using Mcp.Compliance.Spec;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Mcp.Benchmark.Infrastructure.Http;

public interface IMcpClientFactory : IAsyncDisposable
{
    Task<McpClient> GetOrCreateClientAsync(
        McpServerConfig serverConfig,
        AuthenticationConfig? perRequestAuthentication,
        string? protocolVersion,
        CancellationToken cancellationToken = default);

    Task InvalidateAsync(
        McpServerConfig serverConfig,
        AuthenticationConfig? perRequestAuthentication,
        string? protocolVersion,
        CancellationToken cancellationToken = default);
}

public sealed class McpClientFactory : IMcpClientFactory
{
    private const string DefaultClientName = "MCP-Compliance-Validator";

    private readonly ConcurrentDictionary<McpClientCacheKey, Lazy<Task<CachedClient>>> _clients = new();
    private readonly object _lifecycleLock = new();
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly List<Task> _retiredCleanup = new();
    private Task? _disposeTask;
    private bool _disposed;
    private readonly ILogger<McpClientFactory> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly GovernedHttpClientProvider _httpClientProvider;
    private readonly bool _ownsHttpClientProvider;
    private readonly string _clientVersion;

    public McpClientFactory(ILogger<McpClientFactory> logger, ILoggerFactory loggerFactory)
        : this(logger, loggerFactory, CreateCompatibilityHttpClientProvider(), ownsHttpClientProvider: true)
    {
    }

    internal McpClientFactory(
        ILogger<McpClientFactory> logger,
        ILoggerFactory loggerFactory,
        GovernedHttpClientProvider httpClientProvider,
        bool ownsHttpClientProvider = false)
    {
        _logger = logger;
        _loggerFactory = loggerFactory;
        _httpClientProvider = httpClientProvider;
        _ownsHttpClientProvider = ownsHttpClientProvider;
        _clientVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0";
    }

    public async Task<McpClient> GetOrCreateClientAsync(
        McpServerConfig serverConfig,
        AuthenticationConfig? perRequestAuthentication,
        string? protocolVersion,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(serverConfig.Endpoint))
        {
            throw new ArgumentException("Server endpoint is required to create an MCP client.", nameof(serverConfig));
        }
        var normalizedProtocol = protocolVersion ?? serverConfig.ProtocolVersion;
        var snapshot = CaptureClientConfiguration(serverConfig, perRequestAuthentication, normalizedProtocol);
        var effectiveAuth = snapshot.Authentication;
        var key = CreateCacheKey(snapshot.Endpoint!, normalizedProtocol, effectiveAuth, snapshot.Headers, effectiveAuth?.CustomHeaders);

        Lazy<Task<CachedClient>> lazyClient;
        Task<CachedClient> initialization;
        lock (_lifecycleLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            lazyClient = _clients.GetOrAdd(key, _ =>
                new Lazy<Task<CachedClient>>(() => CreateClientAsync(snapshot, normalizedProtocol, effectiveAuth, _lifetimeCancellation.Token)));
            initialization = lazyClient.Value;
        }

        try
        {
            var cachedClient = await initialization.WaitAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return cachedClient.Client;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (SdkClientCleanupException)
        {
            throw;
        }
        catch
        {
            lock (_lifecycleLock)
                if (_clients.TryGetValue(key, out var current) && ReferenceEquals(current, lazyClient))
                    _clients.TryRemove(key, out _);
            throw;
        }
    }

    public async Task InvalidateAsync(
        McpServerConfig serverConfig,
        AuthenticationConfig? perRequestAuthentication,
        string? protocolVersion,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serverConfig.Endpoint))
        {
            return;
        }

        var normalizedProtocol = protocolVersion ?? serverConfig.ProtocolVersion;
        var snapshot = CaptureClientConfiguration(serverConfig, perRequestAuthentication, normalizedProtocol);
        var effectiveAuth = snapshot.Authentication;
        var key = CreateCacheKey(snapshot.Endpoint!, normalizedProtocol, effectiveAuth, snapshot.Headers, effectiveAuth?.CustomHeaders);

        Task? cleanup = null;
        lock (_lifecycleLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _retiredCleanup.RemoveAll(task => task.IsCompletedSuccessfully);
            if (_clients.TryRemove(key, out var lazyClient) && lazyClient.IsValueCreated)
            {
                cleanup = DisposeEntryAsync(lazyClient);
                _retiredCleanup.Add(cleanup);
            }
        }
        if (cleanup != null) await cleanup.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public ValueTask DisposeAsync()
    {
        lock (_lifecycleLock)
        {
            _disposed = true;
            return new ValueTask(_disposeTask ??= DisposeCoreAsync());
        }
    }

    private async Task DisposeCoreAsync()
    {
        var entries = _clients.Values.ToArray();
        _clients.Clear();
        try
        {
            var cleanup = new List<Task> { _lifetimeCancellation.CancelAsync() };
            cleanup.AddRange(_retiredCleanup);
            cleanup.AddRange(entries.Where(entry => entry.IsValueCreated).Select(DisposeEntryAsync));
            await Task.WhenAll(cleanup).ConfigureAwait(false);
        }
        finally
        {
            if (_ownsHttpClientProvider) _httpClientProvider.Dispose();
            _lifetimeCancellation.Dispose();
        }
    }

    private async Task DisposeEntryAsync(Lazy<Task<CachedClient>> lazyClient)
    {
        CachedClient cachedClient;
        try
        {
            cachedClient = await lazyClient.Value.ConfigureAwait(false);
        }
        catch (SdkClientCleanupException) { throw; }
        catch (Exception ex)
        {
            _logger.LogDebug("MCP client initialization did not complete ({ErrorType}).", ex.GetType().Name);
            return;
        }
        await cachedClient.DisposeAsync().ConfigureAwait(false);
    }

    private static McpServerConfig CaptureClientConfiguration(
        McpServerConfig serverConfig, AuthenticationConfig? perRequestAuthentication, string? protocolVersion)
    {
        if (!Uri.TryCreate(serverConfig.Endpoint, UriKind.Absolute, out var endpoint) || endpoint.Scheme is not ("http" or "https"))
            throw new OperationAdmissionException("Execution policy requires an absolute HTTP or HTTPS endpoint.");
        if (!string.IsNullOrEmpty(endpoint.UserInfo))
            throw new OperationAdmissionException("Execution policy blocked a request URI containing user-info.");
        var snapshot = new McpServerConfig
        {
            Endpoint = endpoint.AbsoluteUri,
            ProtocolVersion = protocolVersion,
            ProtocolEra = serverConfig.ProtocolEra,
            Transport = serverConfig.Transport,
            TimeoutMs = serverConfig.TimeoutMs
        };
        var selectedAuthentication = perRequestAuthentication ?? serverConfig.Authentication;
        snapshot.Authentication = selectedAuthentication is null ? null : new AuthenticationConfig
        {
            Type = selectedAuthentication.Type,
            Token = McpAuthenticationHelper.ResolveToken(selectedAuthentication),
            Username = selectedAuthentication.Username,
            Password = selectedAuthentication.Password,
            AllowInteractive = selectedAuthentication.AllowInteractive,
            CustomHeaders = new Dictionary<string, string>(selectedAuthentication.CustomHeaders, StringComparer.OrdinalIgnoreCase)
        };
        snapshot.Headers = new Dictionary<string, string>(serverConfig.Headers, StringComparer.OrdinalIgnoreCase);
        return snapshot;
    }

    private async Task<CachedClient> CreateClientAsync(
        McpServerConfig serverConfig,
        string? protocolVersion,
        AuthenticationConfig? authentication,
        CancellationToken cancellationToken)
    {
        var endpoint = new Uri(serverConfig.Endpoint!, UriKind.Absolute);
        var headers = BuildHeaders(serverConfig.Headers, authentication?.CustomHeaders);
        ApplyAuthentication(headers, authentication);
        ApplyProtocolVersion(headers, protocolVersion);

        var transportOptions = new HttpClientTransportOptions
        {
            Endpoint = endpoint,
            TransportMode = ResolveHttpTransportMode(serverConfig),
            AdditionalHeaders = headers,
            ConnectionTimeout = TimeSpan.FromMilliseconds(serverConfig.TimeoutMs > 0 ? serverConfig.TimeoutMs : 30000),
            Name = $"http-{endpoint.Host}"
        };

        var transport = new HttpClientTransport(transportOptions, _httpClientProvider.Client, _loggerFactory, ownsHttpClient: false);
        var clientOptions = new McpClientOptions
        {
            ProtocolVersion = protocolVersion,
            Capabilities = new ClientCapabilities(),
            InitializationTimeout = TimeSpan.FromMilliseconds(serverConfig.TimeoutMs > 0 ? serverConfig.TimeoutMs : 60000)
        };

        _logger.LogDebug("Creating MCP client for host {Host} (protocol {ProtocolVersion})", endpoint.Host, protocolVersion ?? "auto");
        try
        {
            var client = await McpClient.CreateAsync(transport, clientOptions, _loggerFactory, cancellationToken).ConfigureAwait(false);
            return new CachedClient(client, transport);
        }
        catch
        {
            try { await transport.DisposeAsync().ConfigureAwait(false); }
            catch { throw new SdkClientCleanupException("transport"); }
            throw;
        }
    }

    private static GovernedHttpClientProvider CreateCompatibilityHttpClientProvider()
    {
        var policyContext = new RunNetworkPolicyContext();
        policyContext.Configure(OperationPolicySnapshot.From(new ExecutionPolicy()));
        return new GovernedHttpClientProvider(new GovernedConnectionFactory(
            policyContext,
            new DnsHostAddressResolver(),
            new SocketEndpointConnector()));
    }

    internal static HttpTransportMode ResolveHttpTransportMode(McpServerConfig serverConfig)
    {
        ArgumentNullException.ThrowIfNull(serverConfig);
        return serverConfig.ProtocolEra switch
        {
            McpProtocolEraSelection.Modern => HttpTransportMode.StreamableHttp,
            McpProtocolEraSelection.Legacy when string.Equals(
                serverConfig.ProtocolVersion,
                ProtocolVersions.V2024_11_05.Value,
                StringComparison.Ordinal) => HttpTransportMode.Sse,
            _ => HttpTransportMode.AutoDetect
        };
    }

    private static void ApplyProtocolVersion(IDictionary<string, string> headers, string? protocolVersion)
    {
        if (string.IsNullOrWhiteSpace(protocolVersion))
        {
            headers.Remove(McpSpecConstants.ProtocolVersionHeader);
            return;
        }

        headers[McpSpecConstants.ProtocolVersionHeader] = protocolVersion.Trim();
    }

    private static void ApplyAuthentication(IDictionary<string, string> headers, AuthenticationConfig? authentication)
    {
        headers.Remove("Authorization");
        var headerValue = McpAuthenticationHelper.BuildAuthorizationHeaderValue(authentication);
        if (string.IsNullOrEmpty(headerValue))
        {
            headerValue = authentication?.CustomHeaders.SingleOrDefault(header =>
                string.Equals(header.Key, "Authorization", StringComparison.OrdinalIgnoreCase)).Value;
        }
        if (!string.IsNullOrWhiteSpace(headerValue)) headers["Authorization"] = headerValue;
    }

    private static IDictionary<string, string> BuildHeaders(
        IDictionary<string, string>? defaultHeaders,
        IDictionary<string, string>? authHeaders)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["User-Agent"] = "Visual-Studio-Code/1.96.0 mcp-compliance-validator/1.0.0"
        };

        if (defaultHeaders != null)
        {
            foreach (var (key, value) in defaultHeaders)
            {
                if (string.IsNullOrWhiteSpace(key) || value is null || McpAuthenticationHelper.IsRuntimeProtocolHeader(key))
                {
                    continue;
                }

                headers[key] = value;
            }
        }

        if (authHeaders != null)
        {
            foreach (var (key, value) in authHeaders)
            {
                if (string.IsNullOrWhiteSpace(key) || value is null || McpAuthenticationHelper.IsRuntimeProtocolHeader(key))
                {
                    continue;
                }

                headers[key] = value;
            }
        }

        return headers;
    }

    private static McpClientCacheKey CreateCacheKey(
        string endpoint,
        string? protocolVersion,
        AuthenticationConfig? authentication,
        IDictionary<string, string>? defaultHeaders,
        IDictionary<string, string>? authHeaders)
    {
        var authFingerprint = ComputeFingerprint(BuildAuthenticationKeyMaterial(authentication));
        var headerFingerprint = ComputeFingerprint(BuildHeaderKeyMaterial(defaultHeaders, authHeaders));
        return new McpClientCacheKey(endpoint, protocolVersion ?? string.Empty, authFingerprint, headerFingerprint);
    }

    private static string BuildAuthenticationKeyMaterial(AuthenticationConfig? authentication)
    {
        if (authentication == null)
        {
            return "none";
        }

        return JsonSerializer.Serialize(new
        {
            type = authentication.Type?.ToLowerInvariant(),
            token = McpAuthenticationHelper.ResolveToken(authentication),
            username = authentication.Username,
            password = authentication.Password,
            allowInteractive = authentication.AllowInteractive
        });
    }

    private static string BuildHeaderKeyMaterial(
        IDictionary<string, string>? defaultHeaders,
        IDictionary<string, string>? authHeaders)
    {
        return JsonSerializer.Serialize(BuildHeaders(defaultHeaders, authHeaders)
            .OrderBy(header => header.Key, StringComparer.OrdinalIgnoreCase)
            .Select(header => new { name = header.Key.ToLowerInvariant(), value = header.Value }));
    }

    private static string ComputeFingerprint(string keyMaterial)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(keyMaterial));
        return Convert.ToHexString(digest);
    }

    private sealed record McpClientCacheKey(
        string Endpoint,
        string ProtocolVersion,
        string AuthFingerprint,
        string HeaderFingerprint);

    private sealed class SdkClientCleanupException(string component)
        : InvalidOperationException($"SDK {component} cleanup did not complete.");

    private sealed class CachedClient : IAsyncDisposable
    {
        public CachedClient(McpClient client, IAsyncDisposable transport)
        {
            Client = client;
            Transport = transport;
        }

        public McpClient Client { get; }
        private IAsyncDisposable Transport { get; }

        public async ValueTask DisposeAsync()
        {
            var failures = new List<string>();
            try { await Client.DisposeAsync().ConfigureAwait(false); }
            catch { failures.Add("client"); }
            try { await Transport.DisposeAsync().ConfigureAwait(false); }
            catch { failures.Add("transport"); }
            if (failures.Count > 0) throw new SdkClientCleanupException(string.Join(" and ", failures));
        }
    }
}
