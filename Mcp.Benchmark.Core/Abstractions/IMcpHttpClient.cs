using Mcp.Benchmark.Core.Models;
using ModelContextProtocol.Protocol;
using ValidatorJsonRpcResponse = Mcp.Benchmark.Core.Models.JsonRpcResponse;

namespace Mcp.Benchmark.Core.Abstractions;

/// <summary>
/// Interface for MCP HTTP client to enable mocking in tests.
/// </summary>
public interface IMcpHttpClient
{
    /// <summary>
    /// Makes a JSON-RPC 2.0 call to the MCP server.
    /// </summary>
    Task<ValidatorJsonRpcResponse> CallAsync(string endpoint, string method, object? parameters = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Makes a JSON-RPC 2.0 call to the MCP server with authentication configuration.
    /// </summary>
    Task<ValidatorJsonRpcResponse> CallAsync(string endpoint, string method, object? parameters, AuthenticationConfig? authentication, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a raw HTTP request to the specified endpoint.
    /// </summary>
    Task<HttpResponseMessage> SendAsync(string endpoint, HttpContent content, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a structured HTTP transport probe and captures headers, status, body, and SSE events.
    /// </summary>
    Task<HttpTransportProbeResponse> SendHttpTransportProbeAsync(HttpTransportProbeRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a structured STDIO transport probe and captures stdout, stderr, process lifecycle, and parser-boundary evidence.
    /// </summary>
    Task<StdioTransportProbeResponse> SendStdioTransportProbeAsync(StdioTransportProbeRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tests basic connectivity to the server endpoint.
    /// </summary>
    Task<bool> TestConnectivityAsync(string endpoint, CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs a health check on the MCP server.
    /// </summary>
    Task<ValidationResult> HealthCheckAsync(string endpoint, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tests if server returns proper JSON-RPC error codes.
    /// </summary>
    Task<JsonRpcErrorValidationResult> ValidateErrorCodesAsync(string endpoint, CancellationToken cancellationToken = default);

    /// <summary>
    /// Forces a client-observed timeout during an MCP request and verifies the endpoint remains responsive afterwards.
    /// </summary>
    Task<TransportResilienceProbeResult> ProbeTimeoutRecoveryAsync(string endpoint, CancellationToken cancellationToken = default);

    /// <summary>
    /// Forces a transport interruption during an MCP request and verifies the endpoint remains responsive afterwards.
    /// </summary>
    Task<TransportResilienceProbeResult> ProbeConnectionInterruptionRecoveryAsync(string endpoint, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tests the MCP initialize handshake.
    /// </summary>
    Task<TransportResult<InitializeResult>> ValidateInitializeAsync(string endpoint, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates declared capabilities match actual implementation.
    /// </summary>
    Task<TransportResult<CapabilitySummary>> ValidateCapabilitiesAsync(string endpoint, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends raw JSON to the endpoint for testing error handling.
    /// </summary>
    Task<ValidatorJsonRpcResponse> SendRawJsonAsync(string endpoint, string rawJson, CancellationToken cancellationToken, bool setContentType = true);

    /// <summary>
    /// Sets the authentication configuration for subsequent requests.
    /// </summary>
    void SetAuthentication(AuthenticationConfig? authentication);

    /// <summary>
    /// Sets the maximum number of concurrent HTTP requests issued by the validator.
    /// </summary>
    /// <param name="maxConcurrency">Maximum simultaneous in-flight calls.</param>
    void SetConcurrencyLimit(int maxConcurrency);

    /// <summary>
    /// Sets the MCP protocol version that should be advertised to the
    /// server (via the MCP-Protocol-Version header and initialize
    /// parameters) for subsequent JSON-RPC requests.
    /// </summary>
    /// <param name="protocolVersion">The protocol version string to advertise, or null to clear.</param>
    void SetProtocolVersion(string? protocolVersion);

    /// <summary>
    /// Applies execution governance constraints for the current run.
    /// </summary>
    void ConfigureExecutionPolicy(ExecutionPolicy? executionPolicy);

    /// <summary>
    /// Performs a GET request to the specified URL and returns the response body as a string.
    /// </summary>
    Task<string> GetStringAsync(string url, CancellationToken cancellationToken = default);
}
