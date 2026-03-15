using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Mcp.Benchmark.Core.Models;
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
    private readonly ILogger<McpClientFactory> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly string _clientVersion;

    public McpClientFactory(ILogger<McpClientFactory> logger, ILoggerFactory loggerFactory)
    {
        _logger = logger;
        _loggerFactory = loggerFactory;
        _clientVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0";
    }

    public async Task<McpClient> GetOrCreateClientAsync(
        McpServerConfig serverConfig,
        AuthenticationConfig? perRequestAuthentication,
        string? protocolVersion,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serverConfig.Endpoint))
        {
            throw new ArgumentException("Server endpoint is required to create an MCP client.", nameof(serverConfig));
        }

        var normalizedProtocol = protocolVersion ?? serverConfig.ProtocolVersion;
        var effectiveAuth = perRequestAuthentication ?? serverConfig.Authentication;
        var key = CreateCacheKey(serverConfig.Endpoint, normalizedProtocol, effectiveAuth, serverConfig.Headers, effectiveAuth?.CustomHeaders);

        var lazyClient = _clients.GetOrAdd(key, _ =>
            new Lazy<Task<CachedClient>>(() => CreateClientAsync(serverConfig, normalizedProtocol, effectiveAuth, cancellationToken)));

        try
        {
            var cachedClient = await lazyClient.Value.ConfigureAwait(false);
            return cachedClient.Client;
        }
        catch
        {
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
        var effectiveAuth = perRequestAuthentication ?? serverConfig.Authentication;
        var key = CreateCacheKey(serverConfig.Endpoint, normalizedProtocol, effectiveAuth, serverConfig.Headers, effectiveAuth?.CustomHeaders);

        if (_clients.TryRemove(key, out var lazyClient) && lazyClient.IsValueCreated)
        {
            try
            {
                var cachedClient = await lazyClient.Value.ConfigureAwait(false);
                await cachedClient.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error disposing cached MCP client for {Endpoint}", serverConfig.Endpoint);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        var disposeTasks = new List<Task>();
        foreach (var entry in _clients.Values)
        {
            if (!entry.IsValueCreated)
            {
                continue;
            }

            disposeTasks.Add(DisposeEntryAsync(entry));
        }

        _clients.Clear();
        if (disposeTasks.Count > 0)
        {
            await Task.WhenAll(disposeTasks).ConfigureAwait(false);
        }
    }

    private async Task DisposeEntryAsync(Lazy<Task<CachedClient>> lazyClient)
    {
        try
        {
            var cachedClient = await lazyClient.Value.ConfigureAwait(false);
            await cachedClient.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error disposing cached MCP client entry");
        }
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
            TransportMode = HttpTransportMode.AutoDetect,
            AdditionalHeaders = headers,
            ConnectionTimeout = TimeSpan.FromMilliseconds(serverConfig.TimeoutMs > 0 ? serverConfig.TimeoutMs : 30000),
            Name = $"http-{endpoint.Host}"
        };

        var transport = new HttpClientTransport(transportOptions, _loggerFactory);
        var clientOptions = new McpClientOptions
        {
            ProtocolVersion = protocolVersion,
            Capabilities = new ClientCapabilities(),
            InitializationTimeout = TimeSpan.FromMilliseconds(serverConfig.TimeoutMs > 0 ? serverConfig.TimeoutMs : 60000)
        };

        _logger.LogDebug("Creating MCP client for {Endpoint} (protocol {ProtocolVersion})", endpoint, protocolVersion ?? "auto");
        var client = await McpClient.CreateAsync(transport, clientOptions, _loggerFactory, cancellationToken).ConfigureAwait(false);
        return new CachedClient(client, transport);
    }

    private static void ApplyProtocolVersion(IDictionary<string, string> headers, string? protocolVersion)
    {
        if (string.IsNullOrWhiteSpace(protocolVersion))
        {
            headers.Remove("MCP-Protocol-Version");
            return;
        }

        headers["MCP-Protocol-Version"] = protocolVersion.Trim();
    }

    private static void ApplyAuthentication(IDictionary<string, string> headers, AuthenticationConfig? authentication)
    {
        var headerValue = McpAuthenticationHelper.BuildAuthorizationHeaderValue(authentication);

        if (headerValue is null)
        {
            headers.Remove("Authorization");
            return;
        }

        // For the transport-level SDK, an explicit empty token simply means
        // "no Authorization header".
        if (headerValue.Length == 0)
        {
            headers.Remove("Authorization");
            return;
        }

        headers["Authorization"] = headerValue;
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
                if (string.IsNullOrWhiteSpace(key) || value is null)
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
                if (string.IsNullOrWhiteSpace(key) || value is null)
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
        var authFingerprint = BuildAuthFingerprint(authentication);
        var headerFingerprint = BuildHeaderFingerprint(defaultHeaders, authHeaders);
        return new McpClientCacheKey(endpoint, protocolVersion ?? string.Empty, authFingerprint, headerFingerprint);
    }

    private static string BuildAuthFingerprint(AuthenticationConfig? authentication)
    {
        if (authentication == null)
        {
            return "none";
        }

        var builder = new StringBuilder();
        builder.Append(authentication.Type?.ToLowerInvariant());
        builder.Append('|');
        builder.Append(authentication.Token);
        builder.Append('|');
        builder.Append(authentication.Username);
        builder.Append('|');
        builder.Append(authentication.Password);
        builder.Append('|');
        builder.Append(authentication.AllowInteractive);
        return builder.ToString();
    }

    private static string BuildHeaderFingerprint(
        IDictionary<string, string>? defaultHeaders,
        IDictionary<string, string>? authHeaders)
    {
        var pairs = new List<string>();

        void AppendHeaders(IDictionary<string, string>? source)
        {
            if (source == null)
            {
                return;
            }

            foreach (var (key, value) in source)
            {
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                pairs.Add($"{key.ToLowerInvariant()}={value}");
            }
        }

        AppendHeaders(defaultHeaders);
        AppendHeaders(authHeaders);

        if (pairs.Count == 0)
        {
            return "none";
        }

        pairs.Sort(StringComparer.Ordinal);
        return string.Join('|', pairs);
    }

    private sealed record McpClientCacheKey(
        string Endpoint,
        string ProtocolVersion,
        string AuthFingerprint,
        string HeaderFingerprint);

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
            await Client.DisposeAsync().ConfigureAwait(false);
            await Transport.DisposeAsync().ConfigureAwait(false);
        }
    }
}
