using System.Text.Json.Serialization;

namespace Mcp.Benchmark.Core.Models;

/// <summary>
/// Represents the result of testing an individual resource.
/// </summary>
public class IndividualResourceResult
{
    /// <summary>
    /// Gets or sets the resource URI.
    /// </summary>
    public string ResourceUri { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the resource name.
    /// </summary>
    public string ResourceName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the test status for this resource.
    /// </summary>
    public TestStatus Status { get; set; } = TestStatus.NotRun;

    /// <summary>
    /// Gets or sets whether the resource was discovered correctly.
    /// </summary>
    public bool DiscoveredCorrectly { get; set; } = false;

    /// <summary>
    /// Gets or sets whether the resource metadata is valid.
    /// </summary>
    public bool MetadataValid { get; set; } = false;

    /// <summary>
    /// Gets or sets whether resource access was successful.
    /// </summary>
    public bool AccessSuccessful { get; set; } = false;

    /// <summary>
    /// Gets or sets the resource access response time in milliseconds.
    /// </summary>
    public double AccessTimeMs { get; set; } = 0.0;

    /// <summary>
    /// Gets or sets the MIME type of the accessed resource.
    /// </summary>
    public string? MimeType { get; set; }

    /// <summary>
    /// Gets or sets the size of the resource content in bytes.
    /// </summary>
    public long? ContentSize { get; set; }

    /// <summary>
    /// Gets or sets any issues found with this resource.
    /// </summary>
    public List<string> Issues { get; set; } = new();

    /// <summary>
    /// Gets or sets structured findings for this resource.
    /// </summary>
    public List<ValidationFinding> Findings { get; set; } = new();

    /// <summary>
    /// Gets or sets static content safety findings for this resource
    /// based on metadata-only analysis (no live calls).
    /// </summary>
    public List<ContentSafetyFinding> ContentSafetyFindings { get; set; } = new();
}

/// <summary>
/// Represents the result of testing resource subscriptions.
/// </summary>
public class SubscriptionTestResult
{
    /// <summary>
    /// Gets or sets the resource URI being subscribed to.
    /// </summary>
    public string ResourceUri { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets whether subscription was successful.
    /// </summary>
    public bool SubscriptionSuccessful { get; set; } = false;

    /// <summary>
    /// Gets or sets whether unsubscription was successful.
    /// </summary>
    public bool UnsubscriptionSuccessful { get; set; } = false;

    /// <summary>
    /// Gets or sets the number of notifications received.
    /// </summary>
    public int NotificationsReceived { get; set; } = 0;

    /// <summary>
    /// Gets or sets the average notification delivery time in milliseconds.
    /// </summary>
    public double AverageNotificationTimeMs { get; set; } = 0.0;

    /// <summary>
    /// Gets or sets any subscription-related issues.
    /// </summary>
    public List<string> Issues { get; set; } = new();
}

/// <summary>
/// Represents the result of testing an individual prompt.
/// </summary>
public class IndividualPromptResult
{
    /// <summary>
    /// Gets or sets the prompt name.
    /// </summary>
    public string PromptName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the prompt description.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets the number of arguments.
    /// </summary>
    public int ArgumentsCount { get; set; } = 0;

    /// <summary>
    /// Gets or sets the test status for this prompt.
    /// </summary>
    public TestStatus Status { get; set; } = TestStatus.NotRun;

    /// <summary>
    /// Gets or sets whether the prompt was discovered correctly.
    /// </summary>
    public bool DiscoveredCorrectly { get; set; } = false;

    /// <summary>
    /// Gets or sets whether the prompt metadata is valid.
    /// </summary>
    public bool MetadataValid { get; set; } = false;

    /// <summary>
    /// Gets or sets whether prompt execution was successful.
    /// </summary>
    public bool ExecutionSuccessful { get; set; } = false;

    /// <summary>
    /// Gets or sets the prompt execution response time in milliseconds.
    /// </summary>
    public double ExecutionTimeMs { get; set; } = 0.0;

    /// <summary>
    /// Gets or sets argument validation results for this prompt.
    /// </summary>
    public List<ArgumentValidationResult> ArgumentTests { get; set; } = new();

    /// <summary>
    /// Gets or sets any issues found with this prompt.
    /// </summary>
    public List<string> Issues { get; set; } = new();

    /// <summary>
    /// Gets or sets structured findings for this prompt.
    /// </summary>
    public List<ValidationFinding> Findings { get; set; } = new();

    /// <summary>
    /// Gets or sets static content safety findings for this prompt
    /// based on metadata-only analysis (no live content).
    /// </summary>
    public List<ContentSafetyFinding> ContentSafetyFindings { get; set; } = new();
}

/// <summary>
/// Represents argument validation test results for prompts.
/// </summary>
public class ArgumentValidationResult
{
    /// <summary>
    /// Gets or sets the argument name being tested.
    /// </summary>
    public string ArgumentName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the test scenario name.
    /// </summary>
    public string TestScenario { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets whether the argument validation passed.
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
/// Represents a security vulnerability discovered during testing.
/// </summary>
public class SecurityVulnerability
{
    /// <summary>
    /// Gets or sets the vulnerability identifier or CVE number.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the vulnerability name or title.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the vulnerability description.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the severity level of the vulnerability.
    /// </summary>
    public VulnerabilitySeverity Severity { get; set; } = VulnerabilitySeverity.Medium;

    /// <summary>
    /// Gets or sets the Common Vulnerability Scoring System (CVSS) score.
    /// </summary>
    public double? CvssScore { get; set; }

    /// <summary>
    /// Gets or sets the category of the vulnerability.
    /// </summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the affected component or endpoint.
    /// </summary>
    public string AffectedComponent { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the proof of concept or exploit details.
    /// </summary>
    public string? ProofOfConcept { get; set; }

    /// <summary>
    /// Gets or sets remediation recommendations.
    /// </summary>
    public string? Remediation { get; set; }

    /// <summary>
    /// Gets or sets whether this vulnerability is exploitable.
    /// </summary>
    public bool IsExploitable { get; set; } = false;

    /// <summary>
    /// Gets or sets the probe contexts that produced this vulnerability finding.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<ProbeContext>? ProbeContexts { get; set; }
}

/// <summary>
/// Represents input validation test results.
/// </summary>
public class InputValidationResult
{
    /// <summary>
    /// Gets or sets the input field or parameter being tested.
    /// </summary>
    public string InputField { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the test payload used.
    /// </summary>
    public string TestPayload { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets whether the input was properly validated.
    /// </summary>
    public bool ValidationPassed { get; set; } = false;

    /// <summary>
    /// Gets or sets the expected validation behavior.
    /// </summary>
    public string ExpectedBehavior { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the actual server response.
    /// </summary>
    public string ActualResponse { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets whether the input was properly sanitized.
    /// </summary>
    public bool PropertySanitized { get; set; } = false;

    /// <summary>
    /// Gets or sets any security concerns identified.
    /// </summary>
    public List<string> SecurityConcerns { get; set; } = new();
}

/// <summary>
/// Represents attack simulation test results.
/// </summary>
public class AttackSimulationResult
{
    /// <summary>
    /// Gets or sets the attack vector name or type.
    /// </summary>
    public string AttackVector { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the attack description.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets whether the attack was successful.
    /// </summary>
    public bool AttackSuccessful { get; set; } = false;

    /// <summary>
    /// Gets or sets whether the server properly defended against the attack.
    /// </summary>
    public bool DefenseSuccessful { get; set; } = false;

    /// <summary>
    /// Gets or sets the server response to the attack.
    /// </summary>
    public string ServerResponse { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the attack execution time in milliseconds.
    /// </summary>
    public double ExecutionTimeMs { get; set; } = 0.0;

    /// <summary>
    /// Gets or sets any evidence or details from the attack simulation.
    /// </summary>
    public Dictionary<string, object> Evidence { get; set; } = new();

    /// <summary>
    /// Gets or sets the probe contexts that produced this attack simulation result.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<ProbeContext>? ProbeContexts { get; set; }
}

/// <summary>
/// Enumeration of vulnerability severity levels.
/// </summary>
public enum VulnerabilitySeverity
{
    /// <summary>
    /// Informational - minimal security impact.
    /// </summary>
    Informational,

    /// <summary>
    /// Low severity - minor security impact.
    /// </summary>
    Low,

    /// <summary>
    /// Medium severity - moderate security impact.
    /// </summary>
    Medium,

    /// <summary>
    /// High severity - significant security impact.
    /// </summary>
    High,

    /// <summary>
    /// Critical severity - severe security impact.
    /// </summary>
    Critical
}

/// <summary>
/// High-level axis for content and behavior risk assessment.
/// </summary>
public enum ContentRiskAxis
{
    /// <summary>
    /// Risk of abusive or spammy behavior (e.g., mass messaging).
    /// </summary>
    Abuse,

    /// <summary>
    /// Risk of sensitive data exfiltration (e.g., dumps, exports).
    /// </summary>
    DataExfiltration,

    /// <summary>
    /// Risk of impacting system state or integrity (e.g., delete/execute).
    /// </summary>
    SystemImpact
}

/// <summary>
/// Overall risk level for a specific content safety axis.
/// </summary>
public enum ContentRiskLevel
{
    None,
    Low,
    Medium,
    High
}

/// <summary>
/// Kind of MCP capability the content safety finding applies to.
/// </summary>
public enum ContentItemKind
{
    Tool,
    Resource,
    Prompt
}

/// <summary>
/// Represents a static, metadata-only content safety finding for a
/// tool, resource, or prompt. Designed to be independent of any
/// specific MCP server implementation.
/// </summary>
public class ContentSafetyFinding
{
    /// <summary>
    /// Gets or sets the kind of item this finding applies to.
    /// </summary>
    public ContentItemKind ItemKind { get; set; }

    /// <summary>
    /// Gets or sets the logical name or identifier of the item.
    /// </summary>
    public string ItemName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the primary risk axis for this finding.
    /// </summary>
    public ContentRiskAxis Axis { get; set; }
        = ContentRiskAxis.SystemImpact;

    /// <summary>
    /// Gets or sets the overall risk level (None–High).
    /// </summary>
    public ContentRiskLevel RiskLevel { get; set; } = ContentRiskLevel.Low;

    /// <summary>
    /// Gets or sets a normalized risk score (0-100).
    /// </summary>
    public double RiskScore { get; set; }
        = 0.0;

    /// <summary>
    /// Gets or sets a short, human-readable reason explaining why
    /// this item was flagged.
    /// </summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets an optional remediation recommendation.
    /// </summary>
    public string? Recommendation { get; set; }
        = null;

    /// <summary>
    /// Gets or sets additional structured context such as matched
    /// keywords or parameter names.
    /// </summary>
    public Dictionary<string, object> Context { get; set; } = new();
}
