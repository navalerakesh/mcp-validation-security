using ModelContextProtocol.Protocol;
using Mcp.Benchmark.Core.Constants;

using System.Text.Json.Serialization;

namespace Mcp.Benchmark.Core.Models;

public readonly record struct ValidationDescriptorKey(string Value)
{
    public override string ToString() => Value;
}

public readonly record struct ValidationRevision(string Value)
{
    public override string ToString() => Value;
}

public enum ValidationStability
{
    Stable,
    Preview,
    Experimental,
    Deprecated
}

public enum ValidationPackKind
{
    ProtocolFeatures,
    RulePack,
    ScenarioPack,
    ClientProfilePack
}

public sealed class ValidationPackDescriptor
{
    public required ValidationDescriptorKey Key { get; init; }

    public required ValidationPackKind Kind { get; init; }

    public required ValidationRevision Revision { get; init; }

    public required string DisplayName { get; init; }

    public required ValidationStability Stability { get; init; }

    public string? DocumentationUrl { get; init; }

    public string? ExtensionId { get; init; }

    public ExtensionLifecycleStage? ExtensionStage { get; init; }

    public string? SpecificationRevision { get; init; }

    public string? SpecificationSha256 { get; init; }

    public ExtensionPolicyBehavior ExtensionPolicyBehavior { get; init; } = ExtensionPolicyBehavior.Core;
}

public sealed class ValidationApplicability
{
    public IReadOnlyList<string> ProtocolVersions { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> SchemaVersions { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Transports { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> AccessModes { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> RequiredCapabilities { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> RequiredSurfaces { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> RequiredExtensionIds { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> ClientProfiles { get; init; } = Array.Empty<string>();

    public bool? RequiresAuthentication { get; init; }
}

public enum ProtocolRuleRequirement
{
    Must,
    Should,
    May
}

public sealed class ProtocolRuleMatrixEntry
{
    public required string RuleId { get; init; }

    public required string Title { get; init; }

    public required ValidationRuleSource Source { get; init; }

    public required ProtocolRuleRequirement Requirement { get; init; }

    public required string SpecReference { get; init; }

    public required ValidationApplicability Applicability { get; init; }

    public IReadOnlyList<string> ValidatorAreas { get; init; } = Array.Empty<string>();

    public string? Notes { get; init; }
}

public sealed class ValidationApplicabilityContext
{
    public required string NegotiatedProtocolVersion { get; init; }

    public required string SchemaVersion { get; init; }

    public required string Transport { get; init; }

    public required string AccessMode { get; init; }

    public string? ServerProfile { get; init; }

    public bool IsAuthenticated { get; init; }

    public IReadOnlyList<string> AdvertisedCapabilities { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> AdvertisedSurfaces { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> AdvertisedExtensionIds { get; init; } = Array.Empty<string>();

    public IReadOnlyDictionary<string, ModernExtensionAdvertisement> AdvertisedExtensions { get; init; }
        = new Dictionary<string, ModernExtensionAdvertisement>(StringComparer.Ordinal);

    public IReadOnlyList<string> SelectedExtensionIds { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> SelectedClientProfiles { get; init; } = Array.Empty<string>();

    public IReadOnlyDictionary<string, string> EnvironmentHints { get; init; }
        = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}

public enum McpProtocolEra
{
    Legacy,
    Modern
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum McpProtocolEraSelection
{
    Auto,
    Legacy,
    Modern
}

public enum ProtocolFeatureLifecycle
{
    Active,
    Deprecated,
    Removed
}

public enum ExtensionLifecycleStage
{
    Experimental,
    Beta,
    Stable,
    Core
}

public enum ExtensionPolicyBehavior
{
    ObservationOnly,
    Preview,
    ExplicitExtensionGate,
    Core
}

public sealed class ProtocolFeatureSet
{
    public required string NegotiatedProtocolVersion { get; init; }

    public required string SchemaVersion { get; init; }

    public McpProtocolEra Era { get; init; }

    public bool RequiresHttpProtocolHeader { get; init; }

    public bool SupportsToolListChangedNotifications { get; init; }

    public bool SupportsTasksSurface { get; init; }

    public bool SupportsDeferredWorkflows { get; init; }

    public bool SupportsBatchJsonRpc { get; init; }

    public bool SupportsServerDiscovery { get; init; }

    public bool UsesPerRequestMetadata { get; init; }

    public bool IsSessionless { get; init; }

    public bool SupportsSubscriptionsListen { get; init; }

    public bool RequiresCacheMetadata { get; init; }

    public bool SupportsMultiRoundTripRequests { get; init; }

    public bool SupportsExtensionNegotiation { get; init; }

    public bool UsesTasksExtension { get; init; }

    public IReadOnlyList<string> OptionalCapabilities { get; init; } = Array.Empty<string>();

    public IReadOnlyDictionary<string, ProtocolFeatureLifecycle> FeatureLifecycle { get; init; }
        = new Dictionary<string, ProtocolFeatureLifecycle>(StringComparer.Ordinal);
}

public sealed class ValidationRunDocument
{
    public string ValidationId { get; set; } = Guid.NewGuid().ToString();

    public string? ProtocolVersion { get; set; }

    public string? SchemaVersion { get; set; }

    public string? TargetIdentitySha256 { get; set; }

    public string? ExecutionPlanSha256 { get; set; }

    public ExecutionMode ExecutionMode { get; set; } = ExecutionMode.Safe;

    public IReadOnlyList<string> PlannedChecks { get; set; } = Array.Empty<string>();

    public ValidationApplicabilityContext? ApplicabilityContext { get; set; }

    public TransportResult<InitializeResult>? InitializationHandshake { get; set; }

    public TransportResult<CapabilitySummary>? CapabilitySnapshot { get; set; }

    public TransportResult<ModernDiscoveryEvidence>? ModernDiscovery { get; set; }

    public HealthCheckResult? BootstrapHealth { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ValidationOperationalMetrics? OperationalMetrics { get; set; }
}

public sealed class ModernDiscoveryEvidence
{
    public bool IsValid { get; init; }

    public IReadOnlyList<string> SupportedVersions { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> CapabilityNames { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> ExtensionIds { get; init; } = Array.Empty<string>();

    public IReadOnlyDictionary<string, ModernExtensionAdvertisement> ExtensionAdvertisements { get; init; }
        = new Dictionary<string, ModernExtensionAdvertisement>(StringComparer.Ordinal);

    public string? ResultType { get; init; }

    public string? CacheScope { get; init; }

    public long? TtlMs { get; init; }

    public string? Instructions { get; init; }

    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
}

public sealed class ModernExtensionAdvertisement
{
    public required string ExtensionId { get; init; }

    public bool IsValid { get; init; }

    public IReadOnlyList<string> MimeTypes { get; init; } = Array.Empty<string>();

    public bool? DirectoryRead { get; init; }

    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
}

public sealed class ValidationAssessmentDocument
{
    public ComplianceTestResult? ProtocolCompliance { get; set; }

    public ToolTestResult? ToolValidation { get; set; }

    public ResourceTestResult? ResourceTesting { get; set; }

    public PromptTestResult? PromptTesting { get; set; }

    public SecurityTestResult? SecurityTesting { get; set; }

    public PerformanceTestResult? PerformanceTesting { get; set; }

    public ErrorHandlingTestResult? ErrorHandling { get; set; }

    public OfficialConformanceAssessment? OfficialConformance { get; set; }

    public List<ExtensionAssessment> Extensions { get; set; } = new();

    public List<ValidationLayerResult> Layers { get; init; } = new();

    public List<ValidationScenarioResult> Scenarios { get; init; } = new();
}

public sealed class ValidationEvidenceDocument
{
    public List<ValidationObservation> Observations { get; init; } = new();

    public List<ValidationCoverageDeclaration> Coverage { get; init; } = new();

    public List<ValidationPackDescriptor> AppliedPacks { get; init; } = new();
}

public sealed class ValidationCompatibilityDocument
{
    public ClientCompatibilityReport? ClientCompatibility { get; set; }
}

public sealed class ValidationLayerResult
{
    public required string LayerId { get; init; }

    public string DisplayName { get; init; } = string.Empty;

    public TestStatus Status { get; init; } = TestStatus.NotRun;

    public string? Summary { get; init; }

    public List<ValidationFinding> Findings { get; init; } = new();
}

public sealed class ValidationScenarioResult
{
    public required string ScenarioId { get; init; }

    public string DisplayName { get; init; } = string.Empty;

    public TestStatus Status { get; init; } = TestStatus.NotRun;

    public string? Summary { get; init; }

    public List<ValidationFinding> Findings { get; init; } = new();
}

public sealed class ValidationScenarioExecutionResult
{
    public required ValidationScenarioResult Scenario { get; init; }

    public List<ValidationObservation> Observations { get; init; } = new();

    public List<ValidationCoverageDeclaration> Coverage { get; init; } = new();
}

public sealed class ValidationScenarioContext
{
    public required McpServerConfig ServerConfig { get; init; }

    public required ValidationApplicabilityContext ApplicabilityContext { get; init; }

    public required McpValidatorConfiguration ValidationConfiguration { get; init; }

    public required ValidationResult ValidationResult { get; init; }
}

public sealed class ValidationObservation
{
    public required string Id { get; init; }

    public required string LayerId { get; init; }

    public required string Component { get; init; }

    public required string ObservationKind { get; init; }

    public string? ScenarioId { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public EvaluationLane? Lane { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public EvidenceOrigin? Origin { get; init; }

    public Dictionary<string, string> Metadata { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<ProbeContext>? ProbeContexts { get; init; }

    public string? RedactedPayloadPreview { get; init; }
}

public enum ValidationCoverageStatus
{
    Covered,
    Skipped,
    AuthRequired,
    Inconclusive,
    NotApplicable,
    Unavailable,
    Blocked
}

public enum ValidationEvidenceBlocker
{
    None = 0,
    NotAdvertised,
    ConfigDisabled,
    AuthRequired,
    TransientFailure,
    UnsupportedTransport,
    Unimplemented,
    Timeout,
    ParserBoundary,
    NoSafeTarget,
    TransportError
}

public enum EvidenceConfidenceLevel
{
    None = 0,
    Low = 1,
    Medium = 2,
    High = 3
}

public enum ProbeAuthStatus
{
    Unknown = 0,
    NotRequired,
    NotApplied,
    Applied,
    AuthRequired,
    InvalidOrExpired
}

public enum ProbeResponseClassification
{
    Unknown = 0,
    Success,
    ProtocolError,
    AuthenticationChallenge,
    AuthorizationFailure,
    TransientFailure,
    TransportFailure,
    ParserBoundary,
    Timeout,
    NoResponse
}

public sealed class ProbeContext
{
    public string ProbeId { get; init; } = Guid.NewGuid().ToString("N");

    public string? RequestId { get; init; }

    public string? Method { get; init; }

    public string? Transport { get; init; }

    public string? ProtocolVersion { get; init; }

    public bool AuthApplied { get; init; }

    public string? AuthScheme { get; init; }

    public ProbeAuthStatus AuthStatus { get; init; } = ProbeAuthStatus.Unknown;

    public ProbeResponseClassification ResponseClassification { get; init; } = ProbeResponseClassification.Unknown;

    public EvidenceConfidenceLevel Confidence { get; init; } = EvidenceConfidenceLevel.None;

    public int? StatusCode { get; init; }

    public string? Reason { get; init; }

    public Dictionary<string, string> Metadata { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ValidationCoverageDeclaration
{
    public required string LayerId { get; init; }

    public required string Scope { get; init; }

    public required ValidationCoverageStatus Status { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public EvaluationLane? Lane { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public EvidenceOrigin? Origin { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ValidationOutcome? ObservedOutcome { get; init; }

    public ValidationOutcome Outcome => Status switch
    {
        ValidationCoverageStatus.Covered => ObservedOutcome switch
        {
            ValidationOutcome.Succeeded => ValidationOutcome.Succeeded,
            ValidationOutcome.Failed => ValidationOutcome.Failed,
            ValidationOutcome.Error => ValidationOutcome.Error,
            _ => throw new InvalidOperationException("Covered evidence requires an observed succeeded, failed, or error outcome.")
        },
        ValidationCoverageStatus.Skipped => RequireCompatible(ValidationOutcome.Skipped),
        ValidationCoverageStatus.AuthRequired => RequireCompatible(ValidationOutcome.AuthRequired),
        ValidationCoverageStatus.Inconclusive => RequireCompatible(ValidationOutcome.Inconclusive),
        ValidationCoverageStatus.NotApplicable => RequireCompatible(ValidationOutcome.NotApplicable),
        ValidationCoverageStatus.Unavailable => RequireCompatible(ValidationOutcome.Unavailable),
        ValidationCoverageStatus.Blocked => ObservedOutcome == ValidationOutcome.Cancelled
            ? ValidationOutcome.Cancelled
            : RequireCompatible(ValidationOutcome.Blocked),
        _ => throw new ArgumentOutOfRangeException(nameof(Status), Status, "Unknown coverage status.")
    };

    public ValidationEvidenceBlocker Blocker { get; init; } = ValidationEvidenceBlocker.None;

    public EvidenceConfidenceLevel Confidence { get; init; } = EvidenceConfidenceLevel.None;

    public ProbeContext? ProbeContext { get; init; }

    public string? Reason { get; init; }

    private ValidationOutcome RequireCompatible(ValidationOutcome expected)
    {
        if (ObservedOutcome.HasValue && ObservedOutcome.Value != expected)
        {
            throw new InvalidOperationException($"Coverage status {Status} is incompatible with observed outcome {ObservedOutcome}.");
        }

        return expected;
    }
}

public sealed class EvidenceCoverageSummary
{
    public int TotalDeclarations { get; init; }

    public int ApplicableDeclarations { get; init; }

    public int Covered { get; init; }

    public int AuthRequired { get; init; }

    public int Inconclusive { get; init; }

    public int Skipped { get; init; }

    public int NotApplicable { get; init; }

    public int Unavailable { get; init; }

    public int Blocked { get; init; }

    public double EvidenceCoverageRatio { get; init; } = 1.0;

    public double EvidenceConfidenceRatio { get; init; } = 1.0;

    public EvidenceConfidenceLevel ConfidenceLevel { get; init; } = EvidenceConfidenceLevel.High;

    public List<EvidenceCoverageCategory> Categories { get; init; } = new();
}

public sealed class EvidenceCoverageCategory
{
    public required string LayerId { get; init; }

    public int TotalDeclarations { get; init; }

    public int ApplicableDeclarations { get; init; }

    public int Covered { get; init; }

    public int AuthRequired { get; init; }

    public int Inconclusive { get; init; }

    public int Skipped { get; init; }

    public int Unavailable { get; init; }

    public int Blocked { get; init; }

    public double EvidenceCoverageRatio { get; init; }

    public double EvidenceConfidenceRatio { get; init; }

    public EvidenceConfidenceLevel ConfidenceLevel { get; init; }
}

public enum EvaluationLane
{
    Baseline,
    ExternalConformance,
    ExtensionReadiness,
    ClientCompatibility,
    ModelAdvisory
}

public enum EvidenceOrigin
{
    DeterministicObservation,
    DeterministicAggregation,
    ExternalConformanceCheck,
    HeuristicInference,
    ModelAssistance
}

public enum GateOutcome
{
    Note = 0,
    CoverageDebt = 1,
    ReviewRequired = 2,
    Reject = 3
}

public enum ValidationVerdict
{
    Unknown = 0,
    Reject = 1,
    ReviewRequired = 2,
    ConditionallyAcceptable = 3,
    Trusted = 4
}

public enum ImpactArea
{
    ProtocolInteroperability,
    CapabilityContract,
    AuthenticationBoundary,
    UnsafeAutonomy,
    OutputIntegrity,
    DataExposure,
    RecoveryIntegrity,
    OperationalResilience,
    CoverageIntegrity
}

public sealed class DecisionRecord
{
    public required string DecisionId { get; init; }

    public IReadOnlyList<string> RelatedEvidenceIds { get; init; } = Array.Empty<string>();

    public IReadOnlyList<DecisionEvidenceReference> EvidenceReferences { get; init; } = Array.Empty<DecisionEvidenceReference>();

    public string? RuleId { get; init; }

    public string RuleRevision { get; init; } = ValidationFindingRuleIds.CatalogVersion;

    public required EvaluationLane Lane { get; init; }

    public required ValidationRuleSource Authority { get; init; }

    public required EvidenceOrigin Origin { get; init; }

    public required GateOutcome Gate { get; init; }

    public required ValidationFindingSeverity Severity { get; init; }

    public required string Category { get; init; }

    public required string Component { get; init; }

    public required string Summary { get; init; }

    public string? SpecReference { get; init; }

    public IReadOnlyList<ImpactArea> ImpactAreas { get; init; } = Array.Empty<ImpactArea>();

    public Dictionary<string, string> Metadata { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class DecisionEvidenceReference
{
    public required string EvidenceId { get; init; }

    public required string EvidenceKind { get; init; }

    public string? Summary { get; init; }

    public string? SpecReference { get; init; }

    public string? Remediation { get; init; }

    public string? RedactedPayloadPreview { get; init; }

    public Dictionary<string, string> Metadata { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}
