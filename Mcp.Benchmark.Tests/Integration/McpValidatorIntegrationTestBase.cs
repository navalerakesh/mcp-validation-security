using Mcp.Benchmark.Tests.Fixtures;

namespace Mcp.Benchmark.Tests.Integration;

/// <summary>
/// Base class for MCP validator integration tests.
/// Provides common infrastructure and utilities for testing validators against mock servers.
/// </summary>
public abstract class McpValidatorIntegrationTestBase : IClassFixture<McpServerTestFixture>, IDisposable
{
    protected readonly McpServerTestFixture TestFixture;

    protected McpValidatorIntegrationTestBase(McpServerTestFixture testFixture)
    {
        TestFixture = testFixture;
        
        // Reset mock server state before each test
        TestFixture.ResetMockServer();
    }

    /// <summary>
    /// Helper method to create compliant JSON-RPC response structures.
    /// </summary>
    /// <param name="result">The result object to include</param>
    /// <param name="id">Request ID (default: "test-request")</param>
    /// <returns>Compliant JSON-RPC 2.0 response object</returns>
    protected static object CreateCompliantJsonRpcResponse(object result, string id = "test-request")
    {
        return new
        {
            jsonrpc = "2.0",
            result,
            id
        };
    }

    /// <summary>
    /// Helper method to create JSON-RPC error response structures.
    /// </summary>
    /// <param name="code">Error code</param>
    /// <param name="message">Error message</param>
    /// <param name="data">Optional error data</param>
    /// <param name="id">Request ID</param>
    /// <returns>JSON-RPC 2.0 error response object</returns>
    protected static object CreateJsonRpcErrorResponse(int code, string message, object? data = null, string? id = null)
    {
        var error = new
        {
            code,
            message,
            data
        };

        return new
        {
            jsonrpc = "2.0",
            error,
            id
        };
    }

    /// <summary>
    /// Helper method to create standard MCP initialization response.
    /// </summary>
    /// <param name="serverName">Server name</param>
    /// <param name="serverVersion">Server version</param>
    /// <param name="protocolVersion">MCP protocol version</param>
    /// <returns>Standard MCP initialization response</returns>
    protected static object CreateMcpInitializationResponse(
        string serverName = "Test MCP Server",
        string serverVersion = "1.0.0",
        string protocolVersion = "2024-11-05")
    {
        return CreateCompliantJsonRpcResponse(new
        {
            protocolVersion,
            capabilities = new
            {
                tools = new { },
                logging = new { },
                prompts = new { },
                resources = new { }
            },
            serverInfo = new
            {
                name = serverName,
                version = serverVersion
            }
        });
    }

    public virtual void Dispose()
    {
        // Base disposal - derived classes should call base.Dispose()
    }
}
