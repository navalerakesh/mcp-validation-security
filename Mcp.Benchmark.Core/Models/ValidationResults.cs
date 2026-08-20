using System.Text.Json.Serialization;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using Mcp.Benchmark.Core.Constants;

namespace Mcp.Benchmark.Core.Models;

/// <summary>
/// Represents the comprehensive result of MCP server validation testing.
/// Contains detailed results from all test categories and overall compliance assessment.
/// </summary>
public class ValidationResult
{
    public string DocumentType { get; set; } = ArtifactContracts.ValidationResultDocumentType;

    public string DocumentSchemaVersion { get; set; } = ArtifactContracts.ValidationResultSchemaVersion;

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

    [JsonIgnore]
    public TransportResult<ModernDiscoveryEvidence>? ModernDiscovery
    {
        get => Run.ModernDiscovery;
        set => Run.ModernDiscovery = value;
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

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ValidationBaselineComparison? BaselineComparison { get; set; }

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
            DocumentType = DocumentType,
            DocumentSchemaVersion = DocumentSchemaVersion,
            Producer = DeepCloneRequired(Producer),
            Run = new ValidationRunDocument
            {
                ValidationId = ValidationId,
                ProtocolVersion = ProtocolVersion,
                SchemaVersion = Run.SchemaVersion,
                ApplicabilityContext = Run.ApplicabilityContext,
                InitializationHandshake = SanitizeInitializationHandshake(InitializationHandshake),
                CapabilitySnapshot = SanitizeCapabilitySnapshot(CapabilitySnapshot),
                ModernDiscovery = SanitizeModernDiscovery(ModernDiscovery),
                BootstrapHealth = SanitizeBootstrapHealth(BootstrapHealth),
                OperationalMetrics = Run.OperationalMetrics
            },
            Assessments = SanitizeAssessments(Assessments),
            Evidence = SanitizeEvidence(Evidence),
            Compatibility = DeepCloneRequired(Compatibility),
            StartTime = StartTime,
            EndTime = EndTime,
            OverallStatus = OverallStatus,
            ComplianceScore = ComplianceScore,
            ScoringNotes = ScoringNotes.ToList(),
            ScoringDetails = DeepClone(ScoringDetails),
            ServerConfig = ServerConfig.CloneWithoutSecrets(),
            ValidationConfig = ValidationConfig.CloneForDeterministicResult(),
            ServerProfile = ServerProfile,
            ServerProfileSource = ServerProfileSource,
            ExecutionLogs = ExecutionLogs.Select(log => new ValidationLogEntry
            {
                Timestamp = log.Timestamp,
                Level = log.Level,
                Category = log.Category,
                Message = "Validation execution event recorded.",
                Context = new Dictionary<string, object>(),
                Exception = log.Exception == null ? null : "Exception details redacted from canonical artifacts."
            }).ToList(),
            CriticalErrors = CriticalErrors.Select(_ => "Validation framework error details were redacted from the canonical artifact.").ToList(),
            Recommendations = Recommendations.ToList(),
            Summary = DeepCloneRequired(Summary),
            TrustAssessment = DeepClone(TrustAssessment),
            PolicyOutcome = DeepClone(PolicyOutcome),
            BaselineComparison = DeepClone(BaselineComparison)
        };
    }

    private static T? DeepClone<T>(T? value)
    {
        if (value == null) return default;
        return JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value));
    }

    private static T DeepCloneRequired<T>(T value) where T : class =>
        DeepClone(value) ?? throw new InvalidOperationException($"Unable to clone {typeof(T).Name}.");

    private static ValidationAssessmentDocument SanitizeAssessments(ValidationAssessmentDocument source)
    {
        var node = JsonSerializer.SerializeToNode(source) ?? new JsonObject();
        SanitizeCanonicalNode(node, propertyName: null);
        return node.Deserialize<ValidationAssessmentDocument>() ?? new ValidationAssessmentDocument();
    }

    private static ValidationEvidenceDocument SanitizeEvidence(ValidationEvidenceDocument source)
    {
        var clone = DeepCloneRequired(source);
        return new ValidationEvidenceDocument
        {
            Observations = clone.Observations.Select(observation => new ValidationObservation
            {
                Id = observation.Id,
                LayerId = observation.LayerId,
                Component = observation.Component,
                ObservationKind = observation.ObservationKind,
                ScenarioId = observation.ScenarioId,
                RedactedPayloadPreview = string.IsNullOrWhiteSpace(observation.RedactedPayloadPreview)
                    ? null
                    : "Redacted typed observation available.",
                Metadata = new Dictionary<string, string>(observation.Metadata, StringComparer.OrdinalIgnoreCase),
                ProbeContexts = observation.ProbeContexts?.Select(SanitizeProbeContext).Where(context => context != null).Cast<ProbeContext>().ToList()
            }).ToList(),
            Coverage = clone.Coverage,
            AppliedPacks = clone.AppliedPacks
        };
    }

    private static void SanitizeCanonicalNode(JsonNode node, string? propertyName)
    {
        if (node is JsonObject obj)
        {
            foreach (var property in obj.ToArray())
            {
                if (property.Value == null) continue;
                var name = property.Key;
                if (name.Equals("rawJson", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("rawContent", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("bodyPreview", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("stdoutPreview", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("stderrPreview", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("proofOfConcept", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("testPayload", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("actualResponse", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("serverResponse", StringComparison.OrdinalIgnoreCase))
                {
                    obj[name] = null;
                    continue;
                }
                if (name.Equals("redactedPayloadPreview", StringComparison.OrdinalIgnoreCase))
                {
                    obj[name] = property.Value.GetValueKind() == JsonValueKind.Null
                        ? null
                        : "Redacted typed observation available.";
                    continue;
                }
                if (name.Equals("headers", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("requestHeaders", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("responseHeaders", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("context", StringComparison.OrdinalIgnoreCase))
                {
                    obj[name] = new JsonObject();
                    continue;
                }
                if (name.Equals("evidence", StringComparison.OrdinalIgnoreCase) && obj.ContainsKey("AttackVector"))
                {
                    obj[name] = new JsonObject();
                    continue;
                }
                if (name.Equals("probeContexts", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("criticalErrors", StringComparison.OrdinalIgnoreCase))
                {
                    obj[name] = new JsonArray();
                    continue;
                }
                if (name.Equals("exception", StringComparison.OrdinalIgnoreCase))
                {
                    obj[name] = "Exception details redacted from canonical artifacts.";
                    continue;
                }
                SanitizeCanonicalNode(property.Value, name);
            }
            return;
        }

        if (node is JsonArray array)
        {
            foreach (var item in array)
            {
                if (item != null) SanitizeCanonicalNode(item, propertyName);
            }
        }
    }

    private static HealthCheckResult? SanitizeBootstrapHealth(HealthCheckResult? source)
    {
        if (source == null) return null;
        return new HealthCheckResult
        {
            IsHealthy = source.IsHealthy,
            Disposition = source.Disposition,
            ResponseTimeMs = source.ResponseTimeMs,
            ServerVersion = source.ServerVersion,
            ProtocolVersion = source.ProtocolVersion,
            ErrorMessage = source.IsHealthy ? null : "Bootstrap health did not complete successfully.",
            ServerMetadata = new Dictionary<string, object>(),
            InitializationDetails = SanitizeInitializationHandshake(source.InitializationDetails)
        };
    }

    private static TransportResult<InitializeResult>? SanitizeInitializationHandshake(TransportResult<InitializeResult>? source)
    {
        if (source == null) return null;
        return new TransportResult<InitializeResult>
        {
            IsSuccessful = source.IsSuccessful,
            Error = source.IsSuccessful ? null : "Initialize request did not complete successfully.",
            Payload = source.Payload == null ? null : new InitializeResult
            {
                ProtocolVersion = source.Payload.ProtocolVersion,
                Capabilities = source.Payload.Capabilities,
                ServerInfo = source.Payload.ServerInfo,
                Instructions = null
            },
            Transport = SanitizeTransport(source.Transport)
        };
    }

    private static TransportResult<CapabilitySummary>? SanitizeCapabilitySnapshot(TransportResult<CapabilitySummary>? source)
    {
        if (source == null) return null;
        var payload = source.Payload;
        return new TransportResult<CapabilitySummary>
        {
            IsSuccessful = source.IsSuccessful,
            Error = source.IsSuccessful ? null : "Capability discovery did not complete successfully.",
            Payload = payload == null ? null : new CapabilitySummary
            {
                CapabilityDeclarationsAvailable = payload.CapabilityDeclarationsAvailable,
                AdvertisedCapabilities = payload.AdvertisedCapabilities.ToArray(),
                Tools = payload.Tools.ToArray(),
                ToolListingSucceeded = payload.ToolListingSucceeded,
                ToolInvocationAttempted = payload.ToolInvocationAttempted,
                ToolInvocationSucceeded = payload.ToolInvocationSucceeded,
                FirstToolName = payload.FirstToolName,
                DiscoveredToolsCount = payload.DiscoveredToolsCount,
                Score = payload.Score,
                ToolListResponse = SanitizeJsonRpcResponse(payload.ToolListResponse),
                ResourceListResponse = SanitizeJsonRpcResponse(payload.ResourceListResponse),
                PromptListResponse = SanitizeJsonRpcResponse(payload.PromptListResponse),
                ToolListDurationMs = payload.ToolListDurationMs,
                ResourceListDurationMs = payload.ResourceListDurationMs,
                PromptListDurationMs = payload.PromptListDurationMs,
                ResourceListingSucceeded = payload.ResourceListingSucceeded,
                PromptListingSucceeded = payload.PromptListingSucceeded,
                DiscoveredResourcesCount = payload.DiscoveredResourcesCount,
                DiscoveredPromptsCount = payload.DiscoveredPromptsCount
            },
            Transport = SanitizeTransport(source.Transport)
        };
    }

    private static TransportResult<ModernDiscoveryEvidence>? SanitizeModernDiscovery(TransportResult<ModernDiscoveryEvidence>? source)
    {
        if (source == null) return null;
        var payload = source.Payload;
        return new TransportResult<ModernDiscoveryEvidence>
        {
            IsSuccessful = source.IsSuccessful,
            Error = source.IsSuccessful ? null : "Modern discovery did not complete successfully.",
            Payload = payload == null ? null : new ModernDiscoveryEvidence
            {
                IsValid = payload.IsValid,
                SupportedVersions = payload.SupportedVersions.ToArray(),
                CapabilityNames = payload.CapabilityNames.ToArray(),
                ExtensionIds = payload.ExtensionIds.ToArray(),
                ResultType = payload.ResultType,
                CacheScope = payload.CacheScope,
                TtlMs = payload.TtlMs,
                Instructions = null,
                Errors = payload.Errors.Count == 0 ? Array.Empty<string>() : ["Discovery evidence contained validation errors."]
            },
            Transport = SanitizeTransport(source.Transport)
        };
    }

    private static TransportMetadata SanitizeTransport(TransportMetadata source) => new()
    {
        StatusCode = source.StatusCode,
        Duration = source.Duration,
        Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
        RawContent = null
    };

    private static JsonRpcResponse? SanitizeJsonRpcResponse(JsonRpcResponse? source)
    {
        if (source == null) return null;
        return new JsonRpcResponse
        {
            StatusCode = source.StatusCode,
            IsSuccess = source.IsSuccess,
            Error = source.IsSuccess ? null : "JSON-RPC request did not complete successfully.",
            ResultType = source.ResultType,
            ProtocolSemanticsValid = source.ProtocolSemanticsValid,
            ProtocolSemanticError = source.ProtocolSemanticsValid == false ? "Protocol semantics were invalid." : null,
            RequestState = source.RequestState,
            InputRequestCount = source.InputRequestCount,
            ProbeContext = SanitizeProbeContext(source.ProbeContext),
            ElapsedMs = source.ElapsedMs
        };
    }

    private static ProbeContext? SanitizeProbeContext(ProbeContext? source)
    {
        if (source == null) return null;
        return new ProbeContext
        {
            ProbeId = source.ProbeId,
            RequestId = source.RequestId,
            Method = source.Method,
            Transport = source.Transport,
            ProtocolVersion = source.ProtocolVersion,
            AuthApplied = source.AuthApplied,
            AuthScheme = source.AuthScheme,
            AuthStatus = source.AuthStatus,
            ResponseClassification = source.ResponseClassification,
            Confidence = source.Confidence,
            StatusCode = source.StatusCode
        };
    }
}

public class ValidationProducerInfo
{
    public string Name { get; set; } = string.Empty;

    public string Version { get; set; } = string.Empty;

    public string PackageId { get; set; } = string.Empty;

    public string RepositoryUrl { get; set; } = string.Empty;

    public string PackageUrl { get; set; } = string.Empty;

    public static ValidationProducerInfo CreateDefault()
    {
        return new ValidationProducerInfo
        {
            Name = "MCP Validator",
            Version = GetCurrentVersion(),
            PackageId = "McpVal",
            RepositoryUrl = "https://github.com/navalerakesh/mcp-validation-security",
            PackageUrl = "https://www.nuget.org/packages/McpVal#versions-body-tab"
        };
    }

    private static string GetCurrentVersion()
    {
        var version = typeof(ValidationProducerInfo).Assembly.GetName().Version;
        return version == null ? "0.0.0" : $"{version.Major}.{version.Minor}.{version.Build}";
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
    /// Gets or sets the number of tests blocked by authentication requirements.
    /// </summary>
    public int AuthRequiredTests { get; set; } = 0;

    /// <summary>
    /// Gets or sets the number of tests that ended without enough evidence for pass/fail.
    /// </summary>
    public int InconclusiveTests { get; set; } = 0;

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

    /// <summary>
    /// Gets or sets the evidence confidence ratio separate from the score.
    /// </summary>
    public double EvidenceConfidenceRatio { get; set; } = 1.0;
}
