using System.Text.Json.Serialization;
using ModelContextProtocol.Protocol;

namespace Mcp.Benchmark.Core.Models;

/// <summary>
/// Represents the comprehensive result of MCP server validation testing.
/// Contains detailed results from all test categories and overall compliance assessment.
/// </summary>
public class ValidationResult
{
    /// <summary>
    /// Gets or sets metadata about the tool that produced this validation artifact.
    /// </summary>
    public ValidationProducerInfo Producer { get; set; } = ValidationProducerInfo.CreateDefault();

    public ValidationRunDocument Run { get; set; } = new();

    public ValidationAssessmentDocument Assessments { get; set; } = new();

    public ValidationEvidenceDocument Evidence { get; set; } = new();

    public ValidationCompatibilityDocument Compatibility { get; set; } = new();

    /// <summary>
    /// Gets or sets the unique identifier for this validation run.
    /// </summary>
    [JsonIgnore]
    public string ValidationId
    {
        get => Run.ValidationId;
        set => Run.ValidationId = value;
    }

    /// <summary>
    /// Gets or sets the timestamp when validation started.
    /// </summary>
    public DateTime StartTime { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets or sets the timestamp when validation completed.
    /// </summary>
    public DateTime? EndTime { get; set; }

    /// <summary>
    /// Gets the total duration of the validation process.
    /// </summary>
    public TimeSpan? Duration => EndTime?.Subtract(StartTime);

    /// <summary>
    /// Gets or sets the overall validation status.
    /// </summary>
    public ValidationStatus OverallStatus { get; set; } = ValidationStatus.InProgress;

    /// <summary>
    /// Gets or sets the overall compliance score (0-100).
    /// </summary>
    public double ComplianceScore { get; set; } = 0.0;

    /// <summary>
    /// Gets or sets transparent notes explaining how the aggregate score and status were derived.
    /// </summary>
    public List<string> ScoringNotes { get; set; } = new();

    /// <summary>
    /// Gets or sets the canonical aggregate scoring contract for this validation run.
    /// This is the preferred machine-readable score object for CI/CD consumers.
    /// </summary>
    public ScoringResult? ScoringDetails { get; set; }

    /// <summary>
    /// Gets or sets the server configuration that was tested.
    /// </summary>
    public McpServerConfig ServerConfig { get; set; } = new();

    /// <summary>
    /// Gets or sets the declared or inferred server profile used during validation.
    /// </summary>
    public McpServerProfile ServerProfile { get; set; } = McpServerProfile.Unspecified;

    /// <summary>
    /// Gets or sets how the profile was determined (user, server, inferred).
    /// </summary>
    public ServerProfileSource ServerProfileSource { get; set; } = ServerProfileSource.Unknown;

    /// <summary>
    /// Gets or sets the validation configuration used for testing.
    /// </summary>
    public McpValidatorConfiguration ValidationConfig { get; set; } = new();

    /// <summary>
    /// Gets or sets the protocol compliance test results.
    /// </summary>
    [JsonIgnore]
    public ComplianceTestResult? ProtocolCompliance
    {
        get => Assessments.ProtocolCompliance;
        set => Assessments.ProtocolCompliance = value;
    }

    [JsonIgnore]
    public VerdictAssessment? VerdictAssessment
    {
        get => Assessments.VerdictAssessment;
        set => Assessments.VerdictAssessment = value;
    }

    /// <summary>
    /// Gets or sets the tool validation test results.
    /// </summary>
    [JsonIgnore]
    public ToolTestResult? ToolValidation
    {
        get => Assessments.ToolValidation;
        set => Assessments.ToolValidation = value;
    }

    /// <summary>
    /// Gets or sets the resource testing results.
    /// </summary>
    [JsonIgnore]
    public ResourceTestResult? ResourceTesting
    {
        get => Assessments.ResourceTesting;
        set => Assessments.ResourceTesting = value;
    }

    /// <summary>
    /// Gets or sets the prompt testing results.
    /// </summary>
    [JsonIgnore]
    public PromptTestResult? PromptTesting
    {
        get => Assessments.PromptTesting;
        set => Assessments.PromptTesting = value;
    }

    /// <summary>
    /// Gets or sets the security testing results.
    /// </summary>
    [JsonIgnore]
    public SecurityTestResult? SecurityTesting
    {
        get => Assessments.SecurityTesting;
        set => Assessments.SecurityTesting = value;
    }

    /// <summary>
    /// Gets or sets the performance testing results.
    /// </summary>
    [JsonIgnore]
    public PerformanceTestResult? PerformanceTesting
    {
        get => Assessments.PerformanceTesting;
        set => Assessments.PerformanceTesting = value;
    }

    /// <summary>
    /// Gets or sets the error handling test results.
    /// </summary>
    [JsonIgnore]
    public ErrorHandlingTestResult? ErrorHandling
    {
        get => Assessments.ErrorHandling;
        set => Assessments.ErrorHandling = value;
    }

    /// <summary>
    /// Gets or sets the detailed execution logs.
    /// </summary>
    public List<ValidationLogEntry> ExecutionLogs { get; set; } = new();

    /// <summary>
    /// Gets or sets any critical errors that occurred during validation.
    /// </summary>
    public List<string> CriticalErrors { get; set; } = new();

    /// <summary>
    /// Gets or sets recommendations for improving server compliance.
    /// </summary>
    public List<string> Recommendations { get; set; } = new();

    /// <summary>
    /// Gets or sets the summary statistics for this validation run.
    /// </summary>
    public ValidationSummary Summary { get; set; } = new();

    /// <summary>
    /// Gets or sets the MCP protocol version that was effectively used
    /// for this validation run (after any negotiation or defaults).
    /// This is derived from <see cref="ServerConfig.ProtocolVersion"/> and
    /// recorded here for reporting convenience.
    /// </summary>
    [JsonIgnore]
    public string? ProtocolVersion
    {
        get => Run.ProtocolVersion;
        set => Run.ProtocolVersion = value;
    }

    /// <summary>
    /// Gets or sets the captured MCP initialize handshake, including transport
    /// metadata for latency and HTTP status analysis.
    /// </summary>
    [JsonIgnore]
    public TransportResult<InitializeResult>? InitializationHandshake
    {
        get => Run.InitializationHandshake;
        set => Run.InitializationHandshake = value;
    }

    /// <summary>
    /// Gets or sets the snapshot of tool/resource capability discovery that
    /// accompanies validation runs.
    /// </summary>
    [JsonIgnore]
    public TransportResult<CapabilitySummary>? CapabilitySnapshot
    {
        get => Run.CapabilitySnapshot;
        set => Run.CapabilitySnapshot = value;
    }

    /// <summary>
    /// Gets or sets the calibrated bootstrap health outcome used to decide whether
    /// validation could proceed after the initial connectivity and initialize checks.
    /// </summary>
    [JsonIgnore]
    public HealthCheckResult? BootstrapHealth
    {
        get => Run.BootstrapHealth;
        set => Run.BootstrapHealth = value;
    }

    /// <summary>
    /// Gets or sets the MCP Trust Assessment — a multi-dimensional evaluation
    /// of how trustworthy this MCP server is for AI agent consumption.
    /// Computed after all validators complete.
    /// </summary>
    public McpTrustAssessment? TrustAssessment { get; set; }

    /// <summary>
    /// Gets or sets the host-level policy decision derived from the validation result.
    /// </summary>
    public ValidationPolicyOutcome? PolicyOutcome { get; set; }

    /// <summary>
    /// Gets or sets the optional host-side client compatibility interpretation derived from the validation result.
    /// </summary>
    [JsonIgnore]
    public ClientCompatibilityReport? ClientCompatibility
    {
        get => Compatibility.ClientCompatibility;
        set => Compatibility.ClientCompatibility = value;
    }

    /// <summary>
    /// Creates a shallow copy of this validation result with server and validation
    /// configuration cloned to remove sensitive data such as tokens and secrets.
    /// Test results and logs are reused by reference.
    /// </summary>
    public ValidationResult CloneWithoutSecrets()
    {
        return new ValidationResult
        {
            Producer = Producer,
            Run = new ValidationRunDocument
            {
                ValidationId = ValidationId,
                ProtocolVersion = ProtocolVersion,
                SchemaVersion = Run.SchemaVersion,
                ApplicabilityContext = Run.ApplicabilityContext,
                InitializationHandshake = InitializationHandshake,
                CapabilitySnapshot = CapabilitySnapshot,
                BootstrapHealth = BootstrapHealth
            },
            Assessments = new ValidationAssessmentDocument
            {
                ProtocolCompliance = ProtocolCompliance,
                VerdictAssessment = VerdictAssessment,
                ToolValidation = ToolValidation,
                ResourceTesting = ResourceTesting,
                PromptTesting = PromptTesting,
                SecurityTesting = SecurityTesting,
                PerformanceTesting = PerformanceTesting,
                ErrorHandling = ErrorHandling,
                Layers = Assessments.Layers,
                Scenarios = Assessments.Scenarios
            },
            Evidence = new ValidationEvidenceDocument
            {
                Observations = Evidence.Observations,
                Coverage = Evidence.Coverage,
                AppliedPacks = Evidence.AppliedPacks
            },
            Compatibility = new ValidationCompatibilityDocument
            {
                ClientCompatibility = ClientCompatibility
            },
            StartTime = StartTime,
            EndTime = EndTime,
            OverallStatus = OverallStatus,
            ComplianceScore = ComplianceScore,
            ScoringNotes = ScoringNotes,
            ScoringDetails = ScoringDetails,
            ServerConfig = ServerConfig.CloneWithoutSecrets(),
            ValidationConfig = ValidationConfig.CloneForDeterministicResult(),
            ServerProfile = ServerProfile,
            ServerProfileSource = ServerProfileSource,
            ExecutionLogs = ExecutionLogs,
            CriticalErrors = CriticalErrors,
            Recommendations = Recommendations,
            Summary = Summary,
            TrustAssessment = TrustAssessment,
            PolicyOutcome = PolicyOutcome
        };
    }
}

public class ValidationProducerInfo
{
    public string Name { get; set; } = string.Empty;

    public string PackageId { get; set; } = string.Empty;

    public string RepositoryUrl { get; set; } = string.Empty;

    public string PackageUrl { get; set; } = string.Empty;

    public static ValidationProducerInfo CreateDefault()
    {
        return new ValidationProducerInfo
        {
            Name = "MCP Validator",
            PackageId = "McpVal",
            RepositoryUrl = "https://github.com/navalerakesh/mcp-validation-security",
            PackageUrl = "https://www.nuget.org/packages/McpVal#versions-body-tab"
        };
    }
}

/// <summary>
/// Represents the result of a server health check operation.
/// </summary>
public class HealthCheckResult
{
    /// <summary>
    /// Gets or sets whether the server is healthy and responsive.
    /// </summary>
    public bool IsHealthy { get; set; } = false;

    /// <summary>
    /// Gets or sets the calibrated health disposition used by CLI rendering and session bootstrap decisions.
    /// </summary>
    public HealthCheckDisposition Disposition { get; set; } = HealthCheckDisposition.Unknown;

    /// <summary>
    /// Gets or sets the response time for the health check in milliseconds.
    /// </summary>
    public double ResponseTimeMs { get; set; } = 0.0;

    /// <summary>
    /// Gets or sets the server version information if available.
    /// </summary>
    public string? ServerVersion { get; set; }

    /// <summary>
    /// Gets or sets the MCP protocol version supported by the server.
    /// </summary>
    public string? ProtocolVersion { get; set; }

    /// <summary>
    /// Gets or sets any error message if the health check failed.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Gets or sets additional server metadata discovered during health check.
    /// </summary>
    public Dictionary<string, object> ServerMetadata { get; set; } = new();

    /// <summary>
    /// Gets or sets the initialize handshake used during the health check.
    /// </summary>
    public TransportResult<InitializeResult>? InitializationDetails { get; set; }

    /// <summary>
    /// Gets a value indicating whether validation may continue even though the health check did not complete cleanly.
    /// </summary>
    public bool AllowsDeferredValidation =>
        Disposition is HealthCheckDisposition.Healthy or HealthCheckDisposition.Protected or HealthCheckDisposition.TransientFailure or HealthCheckDisposition.Inconclusive;
}

/// <summary>
/// High-level interpretation of a health check outcome.
/// </summary>
public enum HealthCheckDisposition
{
    Unknown = 0,
    Healthy = 1,
    Protected = 2,
    TransientFailure = 3,
    Inconclusive = 4,
    Unhealthy = 5
}

/// <summary>
/// Represents the capabilities and features supported by an MCP server.
/// </summary>
public class ServerCapabilities
{
    /// <summary>
    /// Gets or sets the supported MCP protocol version.
    /// </summary>
    public string ProtocolVersion { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the server implementation details.
    /// </summary>
    public ServerImplementation Implementation { get; set; } = new();

    /// <summary>
    /// Gets or sets the supported tools and their specifications.
    /// </summary>
    public List<ToolCapability> SupportedTools { get; set; } = new();

    /// <summary>
    /// Gets or sets the supported resources and their specifications.
    /// </summary>
    public List<ResourceCapability> SupportedResources { get; set; } = new();

    /// <summary>
    /// Gets or sets the supported prompts and their specifications.
    /// </summary>
    public List<PromptCapability> SupportedPrompts { get; set; } = new();

    /// <summary>
    /// Gets or sets the supported transport mechanisms.
    /// </summary>
    public List<string> SupportedTransports { get; set; } = new();

    /// <summary>
    /// Gets or sets whether the server supports experimental features.
    /// </summary>
    public bool SupportsExperimentalFeatures { get; set; } = false;

    /// <summary>
    /// Gets or sets additional capabilities discovered.
    /// </summary>
    public Dictionary<string, object> AdditionalCapabilities { get; set; } = new();
}

/// <summary>
/// Represents server implementation details.
/// </summary>
public class ServerImplementation
{
    /// <summary>
    /// Gets or sets the server name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the server version.
    /// </summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the server description.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets the server author or organization.
    /// </summary>
    public string? Author { get; set; }

    /// <summary>
    /// Gets or sets the server homepage or documentation URL.
    /// </summary>
    public string? Homepage { get; set; }
}

/// <summary>
/// Enumeration of possible validation statuses.
/// </summary>
public enum ValidationStatus
{
    /// <summary>
    /// Validation is currently in progress.
    /// </summary>
    InProgress,

    /// <summary>
    /// Validation completed successfully with full compliance.
    /// </summary>
    Passed,

    /// <summary>
    /// Validation completed with some failures or non-compliance issues.
    /// </summary>
    Failed,

    /// <summary>
    /// Validation completed partially due to errors or interruptions.
    /// </summary>
    PartiallyCompleted,

    /// <summary>
    /// Validation was cancelled before completion.
    /// </summary>
    Cancelled,

    /// <summary>
    /// Validation encountered critical errors and could not complete.
    /// </summary>
    Error
}

/// <summary>
/// Enumeration of test categories for targeted validation.
/// </summary>
public enum TestCategory
{
    /// <summary>
    /// Protocol compliance and JSON-RPC adherence tests.
    /// </summary>
    ProtocolCompliance,

    /// <summary>
    /// Tool discovery and execution tests.
    /// </summary>
    ToolValidation,

    /// <summary>
    /// Resource access and management tests.
    /// </summary>
    ResourceTesting,

    /// <summary>
    /// Prompt handling and execution tests.
    /// </summary>
    PromptTesting,

    /// <summary>
    /// Security vulnerability and penetration tests.
    /// </summary>
    SecurityTesting,

    /// <summary>
    /// Performance and load testing.
    /// </summary>
    PerformanceTesting,

    /// <summary>
    /// Error handling and resilience tests.
    /// </summary>
    ErrorHandling,

    /// <summary>
    /// Basic health and connectivity tests.
    /// </summary>
    HealthCheck
}

/// <summary>
/// Represents a log entry from the validation execution.
/// </summary>
public class ValidationLogEntry
{
    /// <summary>
    /// Gets or sets the timestamp of the log entry.
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets or sets the log level.
    /// </summary>
    public LogLevel Level { get; set; } = LogLevel.Information;

    /// <summary>
    /// Gets or sets the test category this log entry relates to.
    /// </summary>
    public TestCategory? Category { get; set; }

    /// <summary>
    /// Gets or sets the log message.
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets additional context data for the log entry.
    /// </summary>
    public Dictionary<string, object> Context { get; set; } = new();

    /// <summary>
    /// Gets or sets the exception details if this is an error log.
    /// </summary>
    public string? Exception { get; set; }
}

/// <summary>
/// Represents summary statistics for a validation run.
/// </summary>
public class ValidationSummary
{
    /// <summary>
    /// Gets or sets the total number of tests executed.
    /// </summary>
    public int TotalTests { get; set; } = 0;

    /// <summary>
    /// Gets or sets the number of tests that passed.
    /// </summary>
    public int PassedTests { get; set; } = 0;

    /// <summary>
    /// Gets or sets the number of tests that failed.
    /// </summary>
    public int FailedTests { get; set; } = 0;

    /// <summary>
    /// Gets or sets the number of tests that were skipped.
    /// </summary>
    public int SkippedTests { get; set; } = 0;

    /// <summary>
    /// Gets or sets the number of critical issues found.
    /// </summary>
    public int CriticalIssues { get; set; } = 0;

    /// <summary>
    /// Gets or sets the number of warnings identified.
    /// </summary>
    public int Warnings { get; set; } = 0;

    /// <summary>
    /// Gets the overall pass rate as a percentage.
    /// </summary>
    public double PassRate => TotalTests > 0 ? (double)PassedTests / TotalTests * 100 : 0.0;

    /// <summary>
    /// Gets or sets the fraction of the weighting model that was actually executed (0-1).
    /// Used by scoring strategies to surface coverage-aware scores.
    /// </summary>
    public double CoverageRatio { get; set; } = 1.0;
}
