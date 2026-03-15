using System.Text.Json.Serialization;

namespace Mcp.Benchmark.Core.Models;

/// <summary>
/// Defines the validation scenarios and test categories to execute against the MCP server.
/// This comprehensive configuration allows granular control over compliance testing.
/// </summary>
public class ValidationScenarios
{
    /// <summary>
    /// Gets or sets the basic protocol compliance tests configuration.
    /// </summary>
    [JsonPropertyName("protocol")]
    public ProtocolComplianceConfig ProtocolCompliance { get; set; } = new();

    /// <summary>
    /// Gets or sets the tool integration testing configuration.
    /// </summary>
    [JsonPropertyName("tools")]
    public ToolTestingConfig ToolTesting { get; set; } = new();

    /// <summary>
    /// Gets or sets the resource access testing configuration.
    /// </summary>
    [JsonPropertyName("resources")]
    public ResourceTestingConfig ResourceTesting { get; set; } = new();

    /// <summary>
    /// Gets or sets the prompts testing configuration.
    /// </summary>
    [JsonPropertyName("prompts")]
    public PromptTestingConfig PromptTesting { get; set; } = new();

    /// <summary>
    /// Gets or sets the security and penetration testing configuration.
    /// </summary>
    [JsonPropertyName("security")]
    public SecurityTestingConfig SecurityTesting { get; set; } = new();

    /// <summary>
    /// Gets or sets the performance and load testing configuration.
    /// </summary>
    [JsonPropertyName("performance")]
    public PerformanceTestingConfig PerformanceTesting { get; set; } = new();

    /// <summary>
    /// Gets or sets the error handling and resilience testing configuration.
    /// </summary>
    [JsonPropertyName("errorHandling")]
    public ErrorHandlingConfig ErrorHandling { get; set; } = new();
}

/// <summary>
/// Configuration for basic MCP protocol compliance testing.
/// </summary>
public class ProtocolComplianceConfig
{
    /// <summary>
    /// Gets or sets whether to test JSON-RPC 2.0 compliance.
    /// </summary>
    public bool TestJsonRpcCompliance { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to test MCP initialization handshake.
    /// </summary>
    public bool TestInitialization { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to test capability negotiation.
    /// </summary>
    public bool TestCapabilities { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to test notification handling.
    /// </summary>
    public bool TestNotifications { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to test message format validation.
    /// </summary>
    public bool TestMessageFormat { get; set; } = true;

    /// <summary>
    /// Gets or sets the MCP protocol version to test against.
    /// </summary>
    /// <summary>
    /// Gets or sets the MCP spec profile to target (e.g., 2025-06-18, 2025-11-25, latest).
    /// </summary>
    public string ProtocolVersion { get; set; } = "latest";
}

/// <summary>
/// Configuration for tool-related testing scenarios.
/// </summary>
public class ToolTestingConfig
{
    /// <summary>
    /// Gets or sets whether to test tool discovery.
    /// </summary>
    public bool TestToolDiscovery { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to test tool execution.
    /// </summary>
    public bool TestToolExecution { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to test tool parameter validation.
    /// </summary>
    public bool TestParameterValidation { get; set; } = true;

    /// <summary>
    /// Gets or sets specific tools to test (empty means test all available).
    /// </summary>
    public List<string> SpecificTools { get; set; } = new();

    /// <summary>
    /// Gets or sets pre-discovered authentication metadata.
    /// </summary>
    public AuthDiscoveryInfo? PreDiscoveredAuth { get; set; }

    /// <summary>
    /// Gets or sets custom tool test scenarios.
    /// </summary>
    public List<ToolTestScenario> CustomScenarios { get; set; } = new();

    /// <summary>
    /// Gets or sets the pre-captured capability snapshot so validators can reuse SDK metadata.
    /// </summary>
    [JsonIgnore]
    public TransportResult<CapabilitySummary>? CapabilitySnapshot { get; set; }
}

/// <summary>
/// Represents a custom tool test scenario.
/// </summary>
public class ToolTestScenario
{
    /// <summary>
    /// Gets or sets the tool name to test.
    /// </summary>
    public string ToolName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the test parameters to use.
    /// </summary>
    public Dictionary<string, object> Parameters { get; set; } = new();

    /// <summary>
    /// Gets or sets the expected result type or pattern.
    /// </summary>
    public string? ExpectedResult { get; set; }

    /// <summary>
    /// Gets or sets whether this scenario should expect an error.
    /// </summary>
    public bool ExpectError { get; set; } = false;
}

/// <summary>
/// Configuration for resource access testing.
/// </summary>
public class ResourceTestingConfig
{
    /// <summary>
    /// Gets or sets whether to test resource discovery.
    /// </summary>
    public bool TestResourceDiscovery { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to test resource reading.
    /// </summary>
    public bool TestResourceReading { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to test resource URI validation.
    /// </summary>
    public bool TestUriValidation { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to test resource subscriptions.
    /// </summary>
    public bool TestSubscriptions { get; set; } = true;

    /// <summary>
    /// Gets or sets specific resource URIs to test.
    /// </summary>
    public List<string> SpecificResources { get; set; } = new();

    /// <summary>
    /// Gets or sets the cached capability snapshot captured before validators execute.
    /// </summary>
    [JsonIgnore]
    public TransportResult<CapabilitySummary>? CapabilitySnapshot { get; set; }
}

/// <summary>
/// Configuration for prompt testing scenarios.
/// </summary>
public class PromptTestingConfig
{
    /// <summary>
    /// Gets or sets whether to test prompt discovery.
    /// </summary>
    public bool TestPromptDiscovery { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to test prompt execution.
    /// </summary>
    public bool TestPromptExecution { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to test prompt argument validation.
    /// </summary>
    public bool TestArgumentValidation { get; set; } = true;

    /// <summary>
    /// Gets or sets specific prompts to test.
    /// </summary>
    public List<string> SpecificPrompts { get; set; } = new();

    /// <summary>
    /// Gets or sets the cached capability snapshot captured before validators execute.
    /// </summary>
    [JsonIgnore]
    public TransportResult<CapabilitySummary>? CapabilitySnapshot { get; set; }
}
