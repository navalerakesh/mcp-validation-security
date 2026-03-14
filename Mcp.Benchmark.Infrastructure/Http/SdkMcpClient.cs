using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Models;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Mcp.Benchmark.Infrastructure.Http;

/// <summary>
/// Default implementation of <see cref="IMcpClient"/> built on top of
/// <see cref="IMcpClientFactory"/> from the official MCP .NET SDK.
/// This class focuses on spec-faithful client behavior and keeps
/// transport details (HTTP, headers, timeouts) in caller-provided
/// <see cref="McpServerConfig"/>.
/// </summary>
public class SdkMcpClient : IMcpClient
{
    private readonly IMcpClientFactory _clientFactory;
    private readonly ILogger<SdkMcpClient> _logger;

    public SdkMcpClient(IMcpClientFactory clientFactory, ILogger<SdkMcpClient> logger)
    {
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<McpClient> GetOrCreateClientAsync(
        McpServerConfig serverConfig,
        AuthenticationConfig? perRequestAuthentication,
        string? protocolVersion,
        CancellationToken cancellationToken = default)
    {
        if (serverConfig == null) throw new ArgumentNullException(nameof(serverConfig));

        return await _clientFactory
            .GetOrCreateClientAsync(serverConfig, perRequestAuthentication, protocolVersion, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<InitializeResult> InitializeAsync(
        McpServerConfig serverConfig,
        AuthenticationConfig? perRequestAuthentication,
        string? protocolVersion,
        CancellationToken cancellationToken = default)
    {
        if (serverConfig == null) throw new ArgumentNullException(nameof(serverConfig));

        var effectiveProtocolVersion = string.IsNullOrWhiteSpace(protocolVersion)
            ? "2025-03-26"
            : protocolVersion;

        _logger.LogInformation("Performing MCP initialize handshake for {Endpoint}", serverConfig.Endpoint);

        // Ensure we start from a clean client instance for this configuration
        await _clientFactory
            .InvalidateAsync(serverConfig, perRequestAuthentication, effectiveProtocolVersion, cancellationToken)
            .ConfigureAwait(false);

        var client = await _clientFactory
            .GetOrCreateClientAsync(serverConfig, perRequestAuthentication, effectiveProtocolVersion, cancellationToken)
            .ConfigureAwait(false);

        return new InitializeResult
        {
            ProtocolVersion = client.NegotiatedProtocolVersion ?? effectiveProtocolVersion,
            Capabilities = client.ServerCapabilities,
            ServerInfo = client.ServerInfo,
            Instructions = client.ServerInstructions
        };
    }

    public async Task<IReadOnlyList<McpClientTool>> ListToolsAsync(
        McpServerConfig serverConfig,
        AuthenticationConfig? perRequestAuthentication,
        string? protocolVersion,
        CancellationToken cancellationToken = default)
    {
        if (serverConfig == null) throw new ArgumentNullException(nameof(serverConfig));

        var client = await GetOrCreateClientAsync(serverConfig, perRequestAuthentication, protocolVersion, cancellationToken)
            .ConfigureAwait(false);

        var tools = await client.ListToolsAsync(new RequestOptions(), cancellationToken).ConfigureAwait(false);
        return tools as IReadOnlyList<McpClientTool> ?? tools.ToList();
    }

    public async Task CallToolAsync(
        McpServerConfig serverConfig,
        AuthenticationConfig? perRequestAuthentication,
        string? protocolVersion,
        string toolName,
        IDictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        if (serverConfig == null) throw new ArgumentNullException(nameof(serverConfig));
        if (string.IsNullOrWhiteSpace(toolName)) throw new ArgumentException("Tool name is required.", nameof(toolName));
        if (arguments == null) throw new ArgumentNullException(nameof(arguments));

        var client = await GetOrCreateClientAsync(serverConfig, perRequestAuthentication, protocolVersion, cancellationToken)
            .ConfigureAwait(false);

        await client.CallToolAsync(toolName, new Dictionary<string, object?>(arguments), null, null, cancellationToken).ConfigureAwait(false);
    }
}
