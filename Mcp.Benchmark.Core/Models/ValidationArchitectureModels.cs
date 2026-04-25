using ModelContextProtocol.Protocol;

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
}

public sealed class ValidationApplicability
{
    public IReadOnlyList<string> ProtocolVersions { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> SchemaVersions { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Transports { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> AccessModes { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> RequiredCapabilities { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> RequiredSurfaces { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> ClientProfiles { get; init; } = Array.Empty<string>();

    public bool? RequiresAuthentication { get; init; }
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

    public IReadOnlyList<string> SelectedClientProfiles { get; init; } = Array.Empty<string>();

    public IReadOnlyDictionary<string, string> EnvironmentHints { get; init; }
        = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}

public sealed class ProtocolFeatureSet
{
    public required string NegotiatedProtocolVersion { get; init; }

    public required string SchemaVersion { get; init; }

    public bool RequiresHttpProtocolHeader { get; init; }

    public bool SupportsToolListChangedNotifications { get; init; }

    public bool SupportsTasksSurface { get; init; }

    public bool SupportsDeferredWorkflows { get; init; }

    public bool SupportsBatchJsonRpc { get; init; }

    public IReadOnlyList<string> OptionalCapabilities { get; init; } = Array.Empty<string>();
}

public sealed class ValidationRunDocument
{
    public string ValidationId { get; set; } = Guid.NewGuid().ToString();

    public string? ProtocolVersion { get; set; }

    public string? SchemaVersion { get; set; }

    public ValidationApplicabilityContext? ApplicabilityContext { get; set; }

    public TransportResult<InitializeResult>? InitializationHandshake { get; set; }

    public TransportResult<CapabilitySummary>? CapabilitySnapshot { get; set; }

    public HealthCheckResult? BootstrapHealth { get; set; }
}

public sealed class ValidationAssessmentDocument
{
    public ComplianceTestResult? ProtocolCompliance { get; set; }

    public VerdictAssessment? VerdictAssessment { get; set; }

    public ToolTestResult? ToolValidation { get; set; }

    public ResourceTestResult? ResourceTesting { get; set; }

    public PromptTestResult? PromptTesting { get; set; }

    public SecurityTestResult? SecurityTesting { get; set; }

    public PerformanceTestResult? PerformanceTesting { get; set; }

    public ErrorHandlingTestResult? ErrorHandling { get; set; }

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

    public Dictionary<string, string> Metadata { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public string? RedactedPayloadPreview { get; init; }
}

public enum ValidationCoverageStatus
{
    Covered,
    Skipped,
    NotApplicable,
    Unavailable,
    Blocked
}

public sealed class ValidationCoverageDeclaration
{
    public required string LayerId { get; init; }

    public required string Scope { get; init; }

    public required ValidationCoverageStatus Status { get; init; }

    public string? Reason { get; init; }
}

public enum EvaluationLane
{
    Baseline,
    ClientCompatibility,
    ModelAdvisory
}

public enum EvidenceOrigin
{
    DeterministicObservation,
    DeterministicAggregation,
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

    public string? RuleId { get; init; }

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

public sealed class VerdictAssessment
{
    public string RulesetVersion { get; set; } = "2026-04";

    public ValidationVerdict BaselineVerdict { get; set; } = ValidationVerdict.Unknown;

    public ValidationVerdict ProtocolVerdict { get; set; } = ValidationVerdict.Unknown;

    public ValidationVerdict CoverageVerdict { get; set; } = ValidationVerdict.Unknown;

    public string Summary { get; set; } = string.Empty;

    public List<DecisionRecord> TriggeredDecisions { get; init; } = new();

    public List<DecisionRecord> BlockingDecisions { get; init; } = new();

    public List<DecisionRecord> CoverageDecisions { get; init; } = new();
}