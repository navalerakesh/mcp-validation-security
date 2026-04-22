namespace Mcp.Benchmark.Core.Models;

/// <summary>
/// Represents JSON-RPC 2.0 compliance test results.
using System.Text.Json.Serialization;

/// </summary>
public class JsonRpcComplianceResult
{
    /// <summary>
    /// Gets or sets whether request format is JSON-RPC 2.0 compliant.
    /// </summary>
    public bool RequestFormatCompliant { get; set; } = false;

    /// <summary>
    /// Gets or sets whether response format is JSON-RPC 2.0 compliant.
    /// </summary>
    public bool ResponseFormatCompliant { get; set; } = false;

    /// <summary>
    /// Gets or sets whether error handling follows JSON-RPC 2.0 specification.
    /// </summary>
    public bool ErrorHandlingCompliant { get; set; } = false;

    /// <summary>
    /// Gets or sets whether batch request processing is compliant.
    /// </summary>
    public bool BatchProcessingCompliant { get; set; } = false;

    /// <summary>
    /// Gets or sets the compliance score (0-100).
    /// </summary>
    public double ComplianceScore { get; set; } = 0.0;

    /// <summary>
    /// Gets or sets specific violations found.
    /// </summary>
    public List<ComplianceViolation> Violations { get; set; } = new();
}

/// <summary>
/// Represents MCP initialization test results.
/// </summary>
public class InitializationTestResult
{
    /// <summary>
    /// Gets or sets whether initialization handshake completed successfully.
    /// </summary>
    public bool HandshakeSuccessful { get; set; } = false;

    /// <summary>
    /// Gets or sets whether server information was provided correctly.
    /// </summary>
    public bool ServerInfoProvided { get; set; } = false;

    /// <summary>
    /// Gets or sets whether client information was accepted correctly.
    /// </summary>
    public bool ClientInfoAccepted { get; set; } = false;

    /// <summary>
    /// Gets or sets the time taken for initialization in milliseconds.
    /// </summary>
    public double InitializationTimeMs { get; set; } = 0.0;

    /// <summary>
    /// Gets or sets any errors encountered during initialization.
    /// </summary>
    public List<string> InitializationErrors { get; set; } = new();
}

/// <summary>
/// Represents capability negotiation test results.
/// </summary>
public class CapabilityTestResult
{
    /// <summary>
    /// Gets or sets whether capability exchange was successful.
    /// </summary>
    public bool CapabilityExchangeSuccessful { get; set; } = false;

    /// <summary>
    /// Gets or sets the capabilities advertised by the server.
    /// </summary>
    public List<string> AdvertisedCapabilities { get; set; } = new();

    /// <summary>
    /// Gets or sets the capabilities actually implemented by the server.
    /// </summary>
    public List<string> ImplementedCapabilities { get; set; } = new();

    /// <summary>
    /// Gets or sets capabilities that were advertised but not implemented.
    /// </summary>
    public List<string> MissingCapabilities { get; set; } = new();

    /// <summary>
    /// Gets or sets the capability compliance score (0-100).
    /// </summary>
    public double CapabilityComplianceScore { get; set; } = 0.0;
}

/// <summary>
/// Represents notification handling test results.
/// </summary>
public class NotificationTestResult
{
    /// <summary>
    /// Gets or sets whether notifications are properly formatted.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? NotificationFormatCorrect { get; set; }

    /// <summary>
    /// Gets or sets whether subscription mechanisms work correctly.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? SubscriptionMechanismWorking { get; set; }

    /// <summary>
    /// Gets or sets whether unsubscription works correctly.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? UnsubscriptionWorking { get; set; }

    /// <summary>
    /// Gets or sets the number of notifications received during testing.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? NotificationsReceived { get; set; }

    /// <summary>
    /// Gets or sets notification-related issues found.
    /// </summary>
    public List<string> NotificationIssues { get; set; } = new();
}

/// <summary>
/// Represents message format validation results.
/// </summary>
public class MessageFormatTestResult
{
    /// <summary>
    /// Gets or sets whether request messages are properly formatted.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? RequestFormatValid { get; set; }

    /// <summary>
    /// Gets or sets whether response messages are properly formatted.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? ResponseFormatValid { get; set; }

    /// <summary>
    /// Gets or sets whether error messages follow the correct format.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? ErrorFormatValid { get; set; }

    /// <summary>
    /// Gets or sets message format violations found.
    /// </summary>
    public List<string> FormatViolations { get; set; } = new();
}

/// <summary>
/// Represents a compliance violation found during testing.
/// </summary>
public class ComplianceViolation
{
    /// <summary>
    /// Gets or sets the stable check identifier (for reporting/SARIF).
    /// </summary>
    public string CheckId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the MCP spec reference (section/URL).
    /// </summary>
    public string? SpecReference { get; set; }
        = null;

    /// <summary>
    /// Gets or sets the severity level of the violation.
    /// </summary>
    public ViolationSeverity Severity { get; set; } = ViolationSeverity.Medium;

    /// <summary>
    /// Gets or sets the category of the violation.
    /// </summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the description of the violation.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the specific rule or specification that was violated.
    /// </summary>
    public string? Rule { get; set; }

    /// <summary>
    /// Gets or sets recommendations for fixing the violation.
    /// </summary>
    public string? Recommendation { get; set; }

    /// <summary>
    /// Gets or sets additional context or evidence for the violation.
    /// </summary>
    public Dictionary<string, object> Context { get; set; } = new();
}

/// <summary>
/// Represents the result of testing an individual tool.
/// </summary>
public class IndividualToolResult
{
    /// <summary>
    /// Gets or sets the name of the tool.
    /// </summary>
    public string ToolName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the test status for this tool.
    /// </summary>
    public TestStatus Status { get; set; } = TestStatus.NotRun;

    /// <summary>
    /// Gets or sets whether the tool was discovered correctly.
    /// </summary>
    public bool DiscoveredCorrectly { get; set; } = false;

    /// <summary>
    /// Gets or sets whether the tool metadata is valid.
    /// </summary>
    public bool MetadataValid { get; set; } = false;

    /// <summary>
    /// Gets or sets whether tool execution was successful.
    /// </summary>
    public bool ExecutionSuccessful { get; set; } = false;

    /// <summary>
    /// Gets or sets the human-friendly display title for the tool, if declared.
    /// </summary>
    public string? DisplayTitle { get; set; }

    /// <summary>
    /// Gets or sets the declared tool description, if available.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets the tool read-only hint, if declared.
    /// </summary>
    public bool? ReadOnlyHint { get; set; }

    /// <summary>
    /// Gets or sets the tool destructive hint, if declared.
    /// </summary>
    public bool? DestructiveHint { get; set; }

    /// <summary>
    /// Gets or sets the tool open-world hint, if declared.
    /// </summary>
    public bool? OpenWorldHint { get; set; }

    /// <summary>
    /// Gets or sets the tool idempotent hint, if declared.
    /// </summary>
    public bool? IdempotentHint { get; set; }

    /// <summary>
    /// Gets or sets the tool execution response time in milliseconds.
    /// </summary>
    public double ExecutionTimeMs { get; set; } = 0.0;

    /// <summary>
    /// Gets or sets parameter validation results for this tool.
    /// </summary>
    public List<ParameterValidationResult> ParameterTests { get; set; } = new();

    /// <summary>
    /// Gets or sets the declared input parameter names for this tool.
    /// </summary>
    public List<string> InputParameterNames { get; set; } = new();

    /// <summary>
    /// Gets or sets any issues found with this tool.
    /// </summary>
    public List<string> Issues { get; set; } = new();

    /// <summary>
    /// Gets or sets structured findings for this tool.
    /// </summary>
    public List<ValidationFinding> Findings { get; set; } = new();

    /// <summary>
    /// Gets or sets the WWW-Authenticate header value if present.
    /// </summary>
    public string? WwwAuthenticateHeader { get; set; }

    /// <summary>
    /// Gets or sets the discovered authentication metadata.
    /// </summary>
    public AuthMetadata? AuthMetadata { get; set; }

    /// <summary>
    /// Gets or sets static content safety findings for this tool
    /// based on metadata-only analysis (no live content).
    /// </summary>
    public List<ContentSafetyFinding> ContentSafetyFindings { get; set; } = new();
}

/// <summary>
/// Represents OAuth 2.0 Protected Resource Metadata.
/// </summary>
public class AuthMetadata
{
    [System.Text.Json.Serialization.JsonPropertyName("resource")]
    public string? Resource { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("authorization_servers")]
    public List<string>? AuthorizationServers { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("scopes_supported")]
    public List<string>? ScopesSupported { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("bearer_methods_supported")]
    public List<string>? BearerMethodsSupported { get; set; }
}

/// <summary>
/// Represents parameter validation test results.
/// </summary>
public class ParameterValidationResult
{
    /// <summary>
    /// Gets or sets the parameter name being tested.
    /// </summary>
    public string ParameterName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the test scenario name.
    /// </summary>
    public string TestScenario { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets whether the parameter validation passed.
    /// </summary>
    public bool ValidationPassed { get; set; } = false;

    /// <summary>
    /// Gets or sets the expected validation behavior.
    /// </summary>
    public string ExpectedBehavior { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the actual validation behavior observed.
    /// </summary>
    public string ActualBehavior { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets any validation error messages.
    /// </summary>
    public string? ValidationError { get; set; }
}

/// <summary>
/// Enumeration of violation severity levels.
/// </summary>
public enum ViolationSeverity
{
    /// <summary>
    /// Low severity - minor issues that don't affect core functionality.
    /// </summary>
    Low,

    /// <summary>
    /// Medium severity - moderate issues that may impact some functionality.
    /// </summary>
    Medium,

    /// <summary>
    /// High severity - significant issues that impact important functionality.
    /// </summary>
    High,

    /// <summary>
    /// Critical severity - severe issues that break core functionality.
    /// </summary>
    Critical
}

/// <summary>
/// Represents capability information for a tool.
/// </summary>
public class ToolCapability
{
    /// <summary>
    /// Gets or sets the tool name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the tool description.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets the tool input schema.
    /// </summary>
    public Dictionary<string, object>? InputSchema { get; set; }

    /// <summary>
    /// Gets or sets whether the tool supports streaming.
    /// </summary>
    public bool SupportsStreaming { get; set; } = false;
}

/// <summary>
/// Represents capability information for a resource.
/// </summary>
public class ResourceCapability
{
    /// <summary>
    /// Gets or sets the resource URI pattern.
    /// </summary>
    public string UriPattern { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the resource description.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets supported MIME types for this resource.
    /// </summary>
    public List<string> SupportedMimeTypes { get; set; } = new();

    /// <summary>
    /// Gets or sets whether the resource supports subscriptions.
    /// </summary>
    public bool SupportsSubscriptions { get; set; } = false;
}

/// <summary>
/// Represents capability information for a prompt.
/// </summary>
public class PromptCapability
{
    /// <summary>
    /// Gets or sets the prompt name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the prompt description.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets the prompt arguments schema.
    /// </summary>
    public Dictionary<string, object>? ArgumentsSchema { get; set; }
}

/// <summary>
/// Represents the result of an authentication discovery process.
/// </summary>
public class AuthDiscoveryInfo
{
    /// <summary>
    /// Gets or sets the WWW-Authenticate header value.
    /// </summary>
    public string? WwwAuthenticateHeader { get; set; }

    /// <summary>
    /// Gets or sets the discovered authentication metadata.
    /// </summary>
    public AuthMetadata? Metadata { get; set; }

    /// <summary>
    /// Gets or sets the list of issues or logs from the discovery process.
    /// </summary>
    public List<string> Issues { get; set; } = new();

    /// <summary>
    /// Gets or sets the time taken for discovery in milliseconds.
    /// </summary>
    public double DiscoveryTimeMs { get; set; }
}
