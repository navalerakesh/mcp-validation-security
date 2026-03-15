using Mcp.Benchmark.Core.Models;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Mcp.Benchmark.Core.Abstractions;

/// <summary>
/// High-level MCP client abstraction for spec-faithful operations
/// such as initialize, tool discovery, and tool invocation.
/// This sits on top of the underlying transport/client factory
/// and is intended to be reusable by validators, benchmarks,
/// and future CLI experiences.
/// </summary>
public interface IMcpClient
{
    /// <summary>
    /// Gets or creates an MCP client instance for the specified server.
    /// </summary>
    /// <param name="serverConfig">Server configuration including endpoint and headers.</param>
    /// <param name="perRequestAuthentication">Optional per-request authentication override.</param>
    /// <param name="protocolVersion">Protocol version to negotiate with the server.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Initialized <see cref="McpClient"/> instance.</returns>
    Task<McpClient> GetOrCreateClientAsync(
        McpServerConfig serverConfig,
        AuthenticationConfig? perRequestAuthentication,
        string? protocolVersion,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs the MCP initialize handshake and returns the negotiated
    /// protocol version, server capabilities, server info, and
    /// optional instructions.
    /// </summary>
    /// <param name="serverConfig">Server configuration including endpoint and headers.</param>
    /// <param name="perRequestAuthentication">Optional per-request authentication override.</param>
    /// <param name="protocolVersion">Protocol version to negotiate with the server.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Populated <see cref="InitializeResult"/>.</returns>
    Task<InitializeResult> InitializeAsync(
        McpServerConfig serverConfig,
        AuthenticationConfig? perRequestAuthentication,
        string? protocolVersion,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists tools exposed by the MCP server.
    /// </summary>
    /// <param name="serverConfig">Server configuration including endpoint and headers.</param>
    /// <param name="perRequestAuthentication">Optional per-request authentication override.</param>
    /// <param name="protocolVersion">Protocol version to negotiate with the server.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Collection of discovered tools.</returns>
    Task<IReadOnlyList<McpClientTool>> ListToolsAsync(
        McpServerConfig serverConfig,
        AuthenticationConfig? perRequestAuthentication,
        string? protocolVersion,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Invokes a tool on the MCP server.
    /// </summary>
    /// <param name="serverConfig">Server configuration including endpoint and headers.</param>
    /// <param name="perRequestAuthentication">Optional per-request authentication override.</param>
    /// <param name="protocolVersion">Protocol version to negotiate with the server.</param>
    /// <param name="toolName">Name of the tool to invoke.</param>
    /// <param name="arguments">Arguments to pass to the tool.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task CallToolAsync(
        McpServerConfig serverConfig,
        AuthenticationConfig? perRequestAuthentication,
        string? protocolVersion,
        string toolName,
        IDictionary<string, object?> arguments,
        CancellationToken cancellationToken = default);
}
