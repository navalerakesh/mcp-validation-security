using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.Core.Abstractions;

/// <summary>
/// Defines the contract for MCP server validation operations.
/// This is the primary interface for orchestrating comprehensive compliance testing.
/// </summary>
public interface IMcpValidatorService
{
    /// <summary>
    /// Validates an MCP server against the specified configuration and scenarios.
    /// </summary>
    /// <param name="configuration">The validation configuration containing server details and test scenarios.</param>
    /// <param name="cancellationToken">Cancellation token to stop the validation process.</param>
    /// <returns>A comprehensive validation result containing all test outcomes.</returns>
    Task<ValidationResult> ValidateServerAsync(McpValidatorConfiguration configuration, CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs a quick health check on the MCP server to verify basic connectivity.
    /// </summary>
    /// <param name="serverConfig">The server configuration for connection details.</param>
    /// <param name="cancellationToken">Cancellation token to stop the health check.</param>
    /// <returns>A simple health check result indicating server availability.</returns>
    Task<HealthCheckResult> PerformHealthCheckAsync(McpServerConfig serverConfig, CancellationToken cancellationToken = default);

    /// <summary>
    /// Discovers the capabilities and features supported by the MCP server.
    /// </summary>
    /// <param name="serverConfig">The server configuration for connection details.</param>
    /// <param name="cancellationToken">Cancellation token to stop the discovery process.</param>
    /// <returns>A detailed report of server capabilities and supported features.</returns>
    Task<ServerCapabilities> DiscoverServerCapabilitiesAsync(McpServerConfig serverConfig, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates specific aspects of the MCP server based on the provided test categories.
    /// </summary>
    /// <param name="serverConfig">The server configuration for connection details.</param>
    /// <param name="testCategories">The specific test categories to execute.</param>
    /// <param name="cancellationToken">Cancellation token to stop the validation process.</param>
    /// <returns>Validation results for the specified test categories.</returns>
    Task<ValidationResult> ValidateSpecificAspectsAsync(McpServerConfig serverConfig, IEnumerable<TestCategory> testCategories, CancellationToken cancellationToken = default);
}

/// <summary>
/// Builds transport/session context required before executing validator pipelines.
/// Handles handshake negotiation, capability discovery, and authentication probing so
/// the main orchestrator can remain transport-agnostic.
/// </summary>
public interface IValidationSessionBuilder
{
    /// <summary>
    /// Creates a validation session for the supplied configuration, returning the
    /// effective server configuration plus any negotiated metadata (protocol version,
    /// capability snapshot, auth discovery details, etc.).
    /// </summary>
    /// <param name="configuration">User-supplied validator configuration.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Fully-populated <see cref="ValidationSessionContext"/> describing the validation session.</returns>
    Task<ValidationSessionContext> BuildAsync(McpValidatorConfiguration configuration, CancellationToken cancellationToken = default);
}

/// <summary>
/// Defines the contract for protocol-level compliance testing.
/// Focuses on JSON-RPC 2.0 and MCP protocol specification adherence.
/// </summary>
public interface IProtocolComplianceValidator
{
    /// <summary>
    /// Validates JSON-RPC 2.0 compliance including message format, error handling, and batch processing.
    /// </summary>
    /// <param name="serverConfig">The server configuration for connection details.</param>
    /// <param name="config">The protocol compliance configuration.</param>
    /// <param name="cancellationToken">Cancellation token to stop the validation process.</param>
    /// <returns>Protocol compliance test results.</returns>
    Task<ComplianceTestResult> ValidateJsonRpcComplianceAsync(McpServerConfig serverConfig, ProtocolComplianceConfig config, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates MCP initialization handshake and capability negotiation.
    /// </summary>
    /// <param name="serverConfig">The server configuration for connection details.</param>
    /// <param name="config">The protocol compliance configuration.</param>
    /// <param name="cancellationToken">Cancellation token to stop the validation process.</param>
    /// <returns>Initialization compliance test results.</returns>
    Task<ComplianceTestResult> ValidateInitializationAsync(McpServerConfig serverConfig, ProtocolComplianceConfig config, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates notification handling and subscription mechanisms.
    /// </summary>
    /// <param name="serverConfig">The server configuration for connection details.</param>
    /// <param name="config">The protocol compliance configuration.</param>
    /// <param name="cancellationToken">Cancellation token to stop the validation process.</param>
    /// <returns>Notification handling test results.</returns>
    Task<ComplianceTestResult> ValidateNotificationHandlingAsync(McpServerConfig serverConfig, ProtocolComplianceConfig config, CancellationToken cancellationToken = default);
}

/// <summary>
/// Defines the contract for tool-related validation testing.
/// Covers tool discovery, execution, parameter validation, and error handling.
/// </summary>
public interface IToolValidator
{
    /// <summary>
    /// Validates tool discovery functionality and metadata accuracy.
    /// </summary>
    /// <param name="serverConfig">The server configuration for connection details.</param>
    /// <param name="config">The tool testing configuration.</param>
    /// <param name="cancellationToken">Cancellation token to stop the validation process.</param>
    /// <returns>Tool discovery validation results.</returns>
    Task<ToolTestResult> ValidateToolDiscoveryAsync(McpServerConfig serverConfig, ToolTestingConfig config, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates tool execution with various parameter combinations and edge cases.
    /// </summary>
    /// <param name="serverConfig">The server configuration for connection details.</param>
    /// <param name="config">The tool testing configuration.</param>
    /// <param name="cancellationToken">Cancellation token to stop the validation process.</param>
    /// <returns>Tool execution validation results.</returns>
    Task<ToolTestResult> ValidateToolExecutionAsync(McpServerConfig serverConfig, ToolTestingConfig config, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates parameter validation and error handling for tool calls.
    /// </summary>
    /// <param name="serverConfig">The server configuration for connection details.</param>
    /// <param name="toolName">The specific tool to test parameter validation for.</param>
    /// <param name="testScenarios">Custom test scenarios for parameter validation.</param>
    /// <param name="cancellationToken">Cancellation token to stop the validation process.</param>
    /// <returns>Parameter validation test results.</returns>
    Task<ToolTestResult> ValidateParameterValidationAsync(McpServerConfig serverConfig, string toolName, IEnumerable<ToolTestScenario> testScenarios, CancellationToken cancellationToken = default);
}

/// <summary>
/// Defines the contract for resource-related validation testing.
/// </summary>
public interface IResourceValidator
{
    /// <summary>
    /// Validates resource discovery and metadata.
    /// </summary>
    Task<ResourceTestResult> ValidateResourceDiscoveryAsync(McpServerConfig serverConfig, ResourceTestingConfig config, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates resource reading and template handling.
    /// </summary>
    Task<ResourceTestResult> ValidateResourceAccessAsync(McpServerConfig serverConfig, ResourceTestingConfig config, CancellationToken cancellationToken = default);
}

/// <summary>
/// Defines the contract for prompt-related validation testing.
/// </summary>
public interface IPromptValidator
{
    /// <summary>
    /// Validates prompt discovery and metadata.
    /// </summary>
    Task<PromptTestResult> ValidatePromptDiscoveryAsync(McpServerConfig serverConfig, PromptTestingConfig config, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates prompt retrieval and execution.
    /// </summary>
    Task<PromptTestResult> ValidatePromptExecutionAsync(McpServerConfig serverConfig, PromptTestingConfig config, CancellationToken cancellationToken = default);
}

/// <summary>
/// Defines the contract for security and penetration testing.
/// Includes vulnerability assessment and attack simulation.
/// </summary>
public interface ISecurityValidator
{
    /// <summary>
    /// Performs comprehensive security assessment including common vulnerability tests.
    /// </summary>
    /// <param name="serverConfig">The server configuration for connection details.</param>
    /// <param name="config">The security testing configuration.</param>
    /// <param name="cancellationToken">Cancellation token to stop the validation process.</param>
    /// <returns>Security assessment results with vulnerability details.</returns>
    Task<SecurityTestResult> PerformSecurityAssessmentAsync(McpServerConfig serverConfig, SecurityTestingConfig config, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tests input validation and sanitization mechanisms.
    /// </summary>
    /// <param name="serverConfig">The server configuration for connection details.</param>
    /// <param name="payloads">Custom security payloads to test.</param>
    /// <param name="cancellationToken">Cancellation token to stop the validation process.</param>
    /// <returns>Input validation test results.</returns>
    Task<SecurityTestResult> ValidateInputSanitizationAsync(McpServerConfig serverConfig, IEnumerable<SecurityTestPayload> payloads, CancellationToken cancellationToken = default);

    /// <summary>
    /// Simulates various attack vectors to test server resilience.
    /// </summary>
    /// <param name="serverConfig">The server configuration for connection details.</param>
    /// <param name="attackVectors">Specific attack vectors to simulate.</param>
    /// <param name="cancellationToken">Cancellation token to stop the validation process.</param>
    /// <returns>Attack simulation results.</returns>
    Task<SecurityTestResult> SimulateAttackVectorsAsync(McpServerConfig serverConfig, IEnumerable<string> attackVectors, CancellationToken cancellationToken = default);
}

/// <summary>
/// Defines the contract for performance and load testing.
/// Measures server performance under various load conditions.
/// </summary>
public interface IPerformanceValidator
{
    /// <summary>
    /// Performs load testing with concurrent connections and requests.
    /// </summary>
    /// <param name="serverConfig">The server configuration for connection details.</param>
    /// <param name="config">The performance testing configuration.</param>
    /// <param name="cancellationToken">Cancellation token to stop the validation process.</param>
    /// <returns>Performance test results with metrics and benchmarks.</returns>
    Task<PerformanceTestResult> PerformLoadTestingAsync(McpServerConfig serverConfig, PerformanceTestingConfig config, CancellationToken cancellationToken = default);

    /// <summary>
    /// Measures response time performance for various operations.
    /// </summary>
    /// <param name="serverConfig">The server configuration for connection details.</param>
    /// <param name="operations">Specific operations to benchmark.</param>
    /// <param name="cancellationToken">Cancellation token to stop the validation process.</param>
    /// <returns>Response time benchmark results.</returns>
    Task<PerformanceTestResult> BenchmarkResponseTimesAsync(McpServerConfig serverConfig, IEnumerable<string> operations, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tests server behavior under resource exhaustion conditions.
    /// </summary>
    /// <param name="serverConfig">The server configuration for connection details.</param>
    /// <param name="resourceTypes">Types of resources to exhaust (memory, connections, etc.).</param>
    /// <param name="cancellationToken">Cancellation token to stop the validation process.</param>
    /// <returns>Resource exhaustion test results.</returns>
    Task<PerformanceTestResult> TestResourceExhaustionAsync(McpServerConfig serverConfig, IEnumerable<string> resourceTypes, CancellationToken cancellationToken = default);
}
