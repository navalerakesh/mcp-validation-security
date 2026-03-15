using System.Threading;
using System.Threading.Tasks;
using WireMock.Server;
using Microsoft.Extensions.Logging;
using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Infrastructure.Services;
using Mcp.Benchmark.Infrastructure.Http;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Mcp.Benchmark.Tests.Fixtures;

/// <summary>
/// Professional test fixture for MCP server testing infrastructure.
/// Provides consistent WireMock setup and HTTP client configuration for integration tests.
/// </summary>
public class McpServerTestFixture : IDisposable
{
    public WireMockServer MockServer { get; private set; }
    public HttpClient HttpClient { get; private set; }
    public Mock<ILogger<McpHttpClient>> McpClientLoggerMock { get; private set; }
    public IMcpClient McpClientAbstraction { get; private set; }
    public McpHttpClient McpClient { get; private set; }

    public McpServerTestFixture()
    {
        // Initialize WireMock server for HTTP simulation
        MockServer = WireMockServer.Start();
        
        // Setup HTTP client with realistic configuration
        HttpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(5)
        };

        // Configure logging mocks
        McpClientLoggerMock = new Mock<ILogger<McpHttpClient>>();
        McpClientAbstraction = new NoOpMcpClient();
        
        // Initialize MCP client
        McpClient = new McpHttpClient(HttpClient, McpClientLoggerMock.Object, McpClientAbstraction);
    }

    /// <summary>
    /// Creates a standard test server configuration pointing to the mock server.
    /// </summary>
    /// <param name="path">Optional path override (default: "/mcp")</param>
    /// <param name="timeoutMs">Optional timeout override (default: 2000ms for faster CI/CD)</param>
    /// <returns>Configured McpServerConfig for testing</returns>
    public McpServerConfig CreateTestServerConfig(string path = "/mcp", int timeoutMs = 2000)
    {
        return new McpServerConfig
        {
            Endpoint = MockServer.Urls[0] + path,
            Transport = "http",
            TimeoutMs = timeoutMs,
            Authentication = new AuthenticationConfig
            {
                Type = "none",
                Required = false
            }
        };
    }

    /// <summary>
    /// Creates a test server configuration with authentication requirements.
    /// </summary>
    /// <param name="authType">Authentication type (bearer, basic, etc.)</param>
    /// <param name="token">Authentication token</param>
    /// <returns>Configured McpServerConfig with authentication</returns>
    public McpServerConfig CreateAuthenticatedServerConfig(string authType = "bearer", string token = "test-token")
    {
        var config = CreateTestServerConfig();
        config.Authentication = new AuthenticationConfig
        {
            Type = authType,
            Required = true,
            Token = token
        };
        return config;
    }

    /// <summary>
    /// Gets the count of requests received by the mock server matching the specified pattern.
    /// </summary>
    /// <param name="pathPattern">Path pattern to match</param>
    /// <returns>Number of matching requests</returns>
    public int GetRequestCount(string pathPattern = "/mcp")
    {
        return MockServer.LogEntries
            .Count(entry => entry.RequestMessage.Path.Contains(pathPattern));
    }

    /// <summary>
    /// Resets the mock server state, clearing all configured responses and request logs.
    /// </summary>
    public void ResetMockServer()
    {
        MockServer.Reset();
    }

    public void Dispose()
    {
        MockServer?.Stop();
        MockServer?.Dispose();
        HttpClient?.Dispose();
    }
}

file sealed class NoOpMcpClient : IMcpClient
{
    public Task<McpClient> GetOrCreateClientAsync(
        McpServerConfig serverConfig,
        AuthenticationConfig? perRequestAuthentication,
        string? protocolVersion,
        CancellationToken cancellationToken = default)
    {
        return Task.FromException<McpClient>(
            new NotSupportedException("IMcpClient is not supported in this test fixture."));
    }

    public Task<InitializeResult> InitializeAsync(
        McpServerConfig serverConfig,
        AuthenticationConfig? perRequestAuthentication,
        string? protocolVersion,
        CancellationToken cancellationToken = default)
    {
        return Task.FromException<InitializeResult>(
            new NotSupportedException("InitializeAsync is not supported in this test fixture."));
    }

    public Task<IReadOnlyList<McpClientTool>> ListToolsAsync(
        McpServerConfig serverConfig,
        AuthenticationConfig? perRequestAuthentication,
        string? protocolVersion,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<McpClientTool>>(Array.Empty<McpClientTool>());
    }

    public Task CallToolAsync(
        McpServerConfig serverConfig,
        AuthenticationConfig? perRequestAuthentication,
        string? protocolVersion,
        string toolName,
        IDictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
