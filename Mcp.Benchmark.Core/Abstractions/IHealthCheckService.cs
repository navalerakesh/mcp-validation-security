using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.Core.Abstractions;

/// <summary>
/// Service responsible for performing pre-flight health checks on MCP servers.
/// </summary>
public interface IHealthCheckService
{
    /// <summary>
    /// Performs a quick health check on the MCP server to verify basic connectivity.
    /// </summary>
    /// <param name="serverConfig">The server configuration for connection details.</param>
    /// <param name="cancellationToken">Cancellation token to stop the health check.</param>
    /// <returns>A simple health check result indicating server availability.</returns>
    Task<HealthCheckResult> PerformHealthCheckAsync(McpServerConfig serverConfig, CancellationToken cancellationToken = default);
}
