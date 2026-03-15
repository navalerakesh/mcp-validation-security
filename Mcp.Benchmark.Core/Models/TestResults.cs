namespace Mcp.Benchmark.Core.Models;

/// <summary>
/// Represents the result of protocol compliance testing.
/// Contains detailed analysis of JSON-RPC and MCP protocol adherence.
/// </summary>
public class ComplianceTestResult : TestResultBase
{
    /// <summary>
    /// Gets or sets the compliance score (0-100).
    /// </summary>
    public double ComplianceScore 
    { 
        get => Score; 
        set => Score = value; 
    }

    /// <summary>
    /// Gets or sets the JSON-RPC compliance test results.
    /// </summary>
    public JsonRpcComplianceResult JsonRpcCompliance { get; set; } = new();

    /// <summary>
    /// Gets or sets the MCP initialization test results.
    /// </summary>
    public InitializationTestResult Initialization { get; set; } = new();

    /// <summary>
    /// Gets or sets the capability negotiation test results.
    /// </summary>
    public CapabilityTestResult CapabilityNegotiation { get; set; } = new();

    /// <summary>
    /// Gets or sets the notification handling test results.
    /// </summary>
    public NotificationTestResult NotificationHandling { get; set; } = new();

    /// <summary>
    /// Gets or sets the message format validation results.
    /// </summary>
    public MessageFormatTestResult MessageFormat { get; set; } = new();

    /// <summary>
    /// Gets or sets specific compliance violations found.
    /// </summary>
    public List<ComplianceViolation> Violations { get; set; } = new();
}

/// <summary>
/// Represents the result of tool validation testing.
/// </summary>
public class ToolTestResult : TestResultBase
{
    /// <summary>
    /// Gets or sets the number of tools discovered.
    /// </summary>
    public int ToolsDiscovered { get; set; } = 0;

    /// <summary>
    /// Gets or sets the names of all discovered tools.
    /// </summary>
    public List<string> DiscoveredToolNames { get; set; } = new();

    /// <summary>
    /// Gets or sets the number of tools successfully tested.
    /// </summary>
    public int ToolsTestPassed { get; set; } = 0;

    /// <summary>
    /// Gets or sets the number of tools that failed testing.
    /// </summary>
    public int ToolsTestFailed { get; set; } = 0;

    /// <summary>
    /// Gets or sets detailed results for each tool tested.
    /// </summary>
    public List<IndividualToolResult> ToolResults { get; set; } = new();

    /// <summary>
    /// Gets or sets parameter validation test results.
    /// </summary>
    public List<ParameterValidationResult> ParameterValidation { get; set; } = new();

    /// <summary>
    /// Gets or sets any issues found during tool testing.
    /// </summary>
    public List<string> Issues { get; set; } = new();

    /// <summary>
    /// Gets or sets the authentication security validation result.
    /// </summary>
    public AuthenticationSecurityResult? AuthenticationSecurity { get; set; }

    /// <summary>
    /// Gets or sets whether the server properly enforces authentication.
    /// </summary>
    public bool AuthenticationProperlyEnforced { get; set; } = false;

    /// <summary>
    /// Gets or sets security test results (injection attacks, bypasses, etc).
    /// </summary>
    public List<SecurityValidationResult> SecurityTests { get; set; } = new();

    /// <summary>
    /// Gets or sets aggregated static content safety findings for all
    /// tools discovered during validation.
    /// </summary>
    public List<ContentSafetyFinding> ContentSafetyFindings { get; set; } = new();

    /// <summary>
    /// AI readiness score (0-100). Measures how well tool schemas are structured
    /// for consumption by AI agents (description quality, type specificity, token efficiency).
    /// </summary>
    public double AiReadinessScore { get; set; } = -1;

    /// <summary>
    /// Issues found during AI readiness analysis.
    /// </summary>
    public List<string> AiReadinessIssues { get; set; } = new();

    /// <summary>
    /// Estimated token count for the full tools/list response payload.
    /// </summary>
    public long EstimatedTokenCount { get; set; } = 0;
}

/// <summary>
/// Represents authentication security validation results for tool access.
/// </summary>
public class AuthenticationSecurityResult
{
    /// <summary>
    /// Server correctly rejects unauthenticated requests.
    /// </summary>
    public bool RejectsUnauthenticated { get; set; }

    /// <summary>
    /// Server correctly rejects invalid/malformed tokens.
    /// </summary>
    public bool RejectsInvalidTokens { get; set; }

    /// <summary>
    /// Server returns proper WWW-Authenticate headers.
    /// </summary>
    public bool HasProperAuthHeaders { get; set; }

    /// <summary>
    /// Error responses follow JSON-RPC format.
    /// </summary>
    public bool ErrorResponsesCompliant { get; set; }

    /// <summary>
    /// HTTP status codes are correct (401/403).
    /// </summary>
    public bool CorrectStatusCodes { get; set; }

    /// <summary>
    /// Indicates whether the server requires authentication for tool access.
    /// </summary>
    public bool AuthenticationRequired { get; set; }

    /// <summary>
    /// Captures the last WWW-Authenticate header observed during validation.
    /// </summary>
    public string? WwwAuthenticateHeader { get; set; }

    /// <summary>
    /// Captures the parsed OAuth metadata document, when available.
    /// </summary>
    public AuthMetadata? AuthMetadata { get; set; }

    /// <summary>
    /// Records how long the challenge/response round trip took in milliseconds.
    /// </summary>
    public double ChallengeDurationMs { get; set; }

    /// <summary>
    /// Gets or sets the HTTP status code returned by the authorization challenge.
    /// </summary>
    public int? ChallengeStatusCode { get; set; }

    /// <summary>
    /// Indicates if an interactive login was attempted during validation.
    /// </summary>
    public bool InteractiveLoginAttempted { get; set; }

    /// <summary>
    /// Indicates if the interactive login attempt succeeded.
    /// </summary>
    public bool InteractiveLoginSucceeded { get; set; }

    /// <summary>
    /// Security score (0-100).
    /// </summary>
    public double SecurityScore { get; set; }

    /// <summary>
    /// Detailed security findings.
    /// </summary>
    public List<string> Findings { get; set; } = new();
}

/// <summary>
/// Represents a security validation test result.
/// </summary>
public class SecurityValidationResult
{
    public string TestName { get; set; } = string.Empty;
    public bool Passed { get; set; }
    public string Description { get; set; } = string.Empty;
    public string? Details { get; set; }
}



/// <summary>/// Represents the result of resource testing.
/// </summary>
public class ResourceTestResult : TestResultBase
{
    /// <summary>
    /// Gets or sets the number of resources discovered.
    /// </summary>
    public int ResourcesDiscovered { get; set; } = 0;

    /// <summary>
    /// Gets or sets the number of resources successfully accessed.
    /// </summary>
    public int ResourcesAccessible { get; set; } = 0;

    /// <summary>
    /// Gets or sets the number of resources that failed testing.
    /// </summary>
    public int ResourcesTestFailed { get; set; } = 0;

    /// <summary>
    /// Gets or sets detailed results for each resource tested.
    /// </summary>
    public List<IndividualResourceResult> ResourceResults { get; set; } = new();

    /// <summary>
    /// Gets or sets subscription test results.
    /// </summary>
    public List<SubscriptionTestResult> SubscriptionResults { get; set; } = new();

    /// <summary>
    /// Gets or sets any issues found during resource testing.
    /// </summary>
    public List<string> Issues { get; set; } = new();

    /// <summary>
    /// Gets or sets aggregated static content safety findings for all
    /// resources discovered during validation.
    /// </summary>
    public List<ContentSafetyFinding> ContentSafetyFindings { get; set; } = new();
}

/// <summary>
/// Represents the result of prompt testing.
/// </summary>
public class PromptTestResult : TestResultBase
{
    /// <summary>
    /// Gets or sets the number of prompts discovered.
    /// </summary>
    public int PromptsDiscovered { get; set; } = 0;

    /// <summary>
    /// Gets or sets the number of prompts successfully tested.
    /// </summary>
    public int PromptsTestPassed { get; set; } = 0;

    /// <summary>
    /// Gets or sets the number of prompts that failed testing.
    /// </summary>
    public int PromptsTestFailed { get; set; } = 0;

    /// <summary>
    /// Gets or sets detailed results for each prompt tested.
    /// </summary>
    public List<IndividualPromptResult> PromptResults { get; set; } = new();

    /// <summary>
    /// Gets or sets any issues found during prompt testing.
    /// </summary>
    public List<string> Issues { get; set; } = new();

    /// <summary>
    /// Gets or sets aggregated static content safety findings for all
    /// prompts discovered during validation.
    /// </summary>
    public List<ContentSafetyFinding> ContentSafetyFindings { get; set; } = new();
}

/// <summary>
/// Represents the result of security testing.
/// </summary>
public class SecurityTestResult : TestResultBase
{
    /// <summary>
    /// Gets or sets the security score (0-100, higher is better).
    /// </summary>
    public double SecurityScore 
    { 
        get => Score; 
        set => Score = value; 
    }

    /// <summary>
    /// Gets or sets vulnerabilities discovered during testing.
    /// </summary>
    public List<SecurityVulnerability> Vulnerabilities { get; set; } = new();

    /// <summary>
    /// Gets or sets input validation test results.
    /// </summary>
    public List<InputValidationResult> InputValidationResults { get; set; } = new();

    /// <summary>
    /// Gets or sets attack simulation results.
    /// </summary>
    public List<AttackSimulationResult> AttackSimulations { get; set; } = new();

    /// <summary>
    /// Gets or sets security recommendations.
    /// </summary>
    public List<string> SecurityRecommendations { get; set; } = new();

    /// <summary>
    /// Gets or sets the authentication test results.
    /// </summary>
    public AuthenticationTestResult? AuthenticationTestResult { get; set; }
}

/// <summary>
/// Represents the result of performance testing.
/// </summary>
public class PerformanceTestResult : TestResultBase
{
    /// <summary>
    /// Gets or sets load testing results.
    /// </summary>
    public LoadTestResult LoadTesting { get; set; } = new();

    /// <summary>
    /// Gets or sets response time benchmark results.
    /// </summary>
    public ResponseTimeBenchmark ResponseTimes { get; set; } = new();

    /// <summary>
    /// Gets or sets resource usage metrics.
    /// </summary>
    public ResourceUsageMetrics ResourceUsage { get; set; } = new();

    /// <summary>
    /// Gets or sets throughput measurements.
    /// </summary>
    public ThroughputMetrics Throughput { get; set; } = new();

    /// <summary>
    /// Gets or sets performance bottlenecks identified.
    /// </summary>
    public List<string> PerformanceBottlenecks { get; set; } = new();
}

/// <summary>
/// Represents the result of error handling testing.
/// </summary>
public class ErrorHandlingTestResult : TestResultBase
{
    /// <summary>
    /// Gets or sets the number of error scenarios tested.
    /// </summary>
    public int ErrorScenariosTestCount { get; set; } = 0;

    /// <summary>
    /// Gets or sets the number of error scenarios handled correctly.
    /// </summary>
    public int ErrorScenariosHandledCorrectly { get; set; } = 0;

    /// <summary>
    /// Gets or sets detailed results for each error scenario.
    /// </summary>
    public List<ErrorScenarioResult> ErrorScenarioResults { get; set; } = new();

    /// <summary>
    /// Gets or sets recovery testing results.
    /// </summary>
    public List<RecoveryTestResult> RecoveryResults { get; set; } = new();
}

/// <summary>
/// Enumeration of test execution statuses.
/// </summary>
public enum TestStatus
{
    /// <summary>
    /// Test has not been executed yet.
    /// </summary>
    NotRun,

    /// <summary>
    /// Test is currently in progress.
    /// </summary>
    InProgress,

    /// <summary>
    /// Test completed successfully.
    /// </summary>
    Passed,

    /// <summary>
    /// Test failed due to assertion failures or non-compliance.
    /// </summary>
    Failed,

    /// <summary>
    /// Test was skipped due to configuration or prerequisites.
    /// </summary>
    Skipped,

    /// <summary>
    /// Test encountered an error during execution.
    /// </summary>
    Error,

    /// <summary>
    /// Test was cancelled before completion.
    /// </summary>
    Cancelled
}

/// <summary>
/// Represents the result of comprehensive authentication testing.
/// </summary>
public class AuthenticationTestResult
{
    /// <summary>
    /// Gets or sets the overall test status.
    /// </summary>
    public TestStatus Status { get; set; } = TestStatus.NotRun;

    /// <summary>
    /// Gets or sets the compliance score (0-100).
    /// </summary>
    public double ComplianceScore { get; set; } = 0.0;

    /// <summary>
    /// Gets or sets the test duration.
    /// </summary>
    public TimeSpan Duration { get; set; } = TimeSpan.Zero;

    /// <summary>
    /// Gets or sets the list of authentication test scenarios.
    /// </summary>
    public List<AuthenticationScenario> TestScenarios { get; set; } = new();
}

/// <summary>
/// Represents a single authentication test scenario.
/// </summary>
public class AuthenticationScenario
{
    /// <summary>
    /// Gets or sets the scenario name.
    /// </summary>
    public string ScenarioName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the test type.
    /// </summary>
    public string TestType { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the method being tested.
    /// </summary>
    public string Method { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the expected behavior.
    /// </summary>
    public string ExpectedBehavior { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the actual behavior observed.
    /// </summary>
    public string ActualBehavior { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the HTTP status code.
    /// </summary>
    public string StatusCode { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the detailed analysis of the response.
    /// </summary>
    public string Analysis { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets whether this scenario is compliant.
    /// </summary>
    public bool IsCompliant { get; set; } = false;

    /// <summary>
    /// Gets or sets the reason for compliance/non-compliance.
    /// </summary>
    public string ComplianceReason { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the WWW-Authenticate header value if present.
    /// </summary>
    public string? WwwAuthenticateHeader { get; set; }
}
