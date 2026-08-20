using ModelContextProtocol.Protocol;
using Mcp.Benchmark.Core.Constants;

using System.Collections.Immutable;
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

    public string? ResultType { get; init; }

    public string? CacheScope { get; init; }

    public long? TtlMs { get; init; }

    public string? Instructions { get; init; }

    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
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

    public IReadOnlyList<string> RelatedEvidenceIds { get; init; } = Array.Empty<string>();

    public IReadOnlyList<DecisionEvidenceReference> EvidenceReferences { get; init; } = Array.Empty<DecisionEvidenceReference>();

    public string? RuleId { get; init; }

    public string RuleRevision { get; init; } = DecisionPolicyManifest.CurrentVersion;

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

public sealed class VerdictAssessment
{
    public string RulesetVersion { get; set; } = DecisionPolicyManifest.CurrentVersion;

    public DecisionPolicyManifest Policy { get; set; } = DecisionPolicyManifest.CreateCurrent();

    public ValidationVerdict BaselineVerdict { get; set; } = ValidationVerdict.Unknown;

    public ValidationVerdict ProtocolVerdict { get; set; } = ValidationVerdict.Unknown;

    public ValidationVerdict CoverageVerdict { get; set; } = ValidationVerdict.Unknown;

    public string Summary { get; set; } = string.Empty;

    public EvidenceCoverageSummary EvidenceSummary { get; set; } = new();

    public List<DecisionRecord> TriggeredDecisions { get; init; } = new();

    public List<DecisionRecord> BlockingDecisions { get; init; } = new();

    public List<DecisionRecord> CoverageDecisions { get; init; } = new();
}

public sealed class DecisionPolicyManifest
{
    public const string CurrentVersion = "2026-08-19.2";

    public string Version { get; init; } = CurrentVersion;

    public string OutcomeTaxonomyVersion { get; init; } = ValidationOutcomeTaxonomy.Version;

    public string RuleCatalogVersion { get; init; } = ValidationFindingRuleIds.CatalogVersion;

    public string ScoringPolicyVersion { get; init; } = ScoringConstants.PolicyVersion;

    public ImmutableSortedDictionary<string, string> PackRevisions { get; init; } = ImmutableSortedDictionary<string, string>.Empty.WithComparers(StringComparer.Ordinal);

    public ImmutableSortedDictionary<string, string> RuleRevisions { get; init; } = ImmutableSortedDictionary<string, string>.Empty.WithComparers(StringComparer.Ordinal);

    public ImmutableSortedDictionary<string, double> Weights { get; init; } = ImmutableSortedDictionary<string, double>.Empty.WithComparers(StringComparer.Ordinal);

    public ImmutableSortedDictionary<string, double> Thresholds { get; init; } = ImmutableSortedDictionary<string, double>.Empty.WithComparers(StringComparer.Ordinal);

    public ImmutableSortedDictionary<string, double> Caps { get; init; } = ImmutableSortedDictionary<string, double>.Empty.WithComparers(StringComparer.Ordinal);

    public ImmutableSortedDictionary<string, double> Parameters { get; init; } = ImmutableSortedDictionary<string, double>.Empty.WithComparers(StringComparer.Ordinal);

    public ImmutableSortedDictionary<string, ImmutableArray<string>> PatternSets { get; init; } = ImmutableSortedDictionary<string, ImmutableArray<string>>.Empty.WithComparers(StringComparer.Ordinal);

    public static DecisionPolicyManifest CreateCurrent() => CreateCurrent(Array.Empty<DecisionRecord>(), Array.Empty<ValidationPackDescriptor>());

    public static DecisionPolicyManifest CreateCurrent(
        IEnumerable<DecisionRecord> decisions,
        IEnumerable<ValidationPackDescriptor> appliedPacks)
    {
        var packRevisions = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["verdict-policy"] = CurrentVersion
        };
        foreach (var pack in appliedPacks.OrderBy(pack => pack.Key.Value, StringComparer.Ordinal))
        {
            packRevisions[pack.Key.Value] = pack.Revision.Value;
        }

        var rules = GetRuleRevisions().ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        foreach (var decision in decisions.Where(decision => !string.IsNullOrWhiteSpace(decision.RuleId)))
        {
            rules[decision.RuleId!] = decision.RuleRevision;
        }

        return new DecisionPolicyManifest
        {
        PackRevisions = packRevisions.ToImmutableSortedDictionary(StringComparer.Ordinal),
        RuleRevisions = rules.ToImmutableSortedDictionary(StringComparer.Ordinal),
        Weights = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["aggregate.errorHandling"] = ScoringConstants.WeightErrorHandling,
            ["aggregate.performance"] = ScoringConstants.WeightPerformance,
            ["aggregate.prompts"] = ScoringConstants.WeightPrompts,
            ["aggregate.protocol"] = ScoringConstants.WeightProtocol,
            ["aggregate.resources"] = ScoringConstants.WeightResources,
            ["aggregate.security"] = ScoringConstants.WeightSecurity,
            ["aggregate.tools"] = ScoringConstants.WeightTools,
            ["trust.aiSafety"] = ScoringConstants.TrustWeightAiSafety,
            ["trust.operations"] = ScoringConstants.TrustWeightOperations,
            ["trust.protocol"] = ScoringConstants.TrustWeightProtocol,
            ["trust.security"] = ScoringConstants.TrustWeightSecurity
        }.ToImmutableSortedDictionary(StringComparer.Ordinal),
        Thresholds = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["coverage.minimum"] = ScoringConstants.MinCoverageRatio,
            ["score.excellent"] = ScoringConstants.ExcellentThreshold,
            ["score.pass"] = ScoringConstants.PassThreshold,
            ["trust.l2"] = ScoringConstants.TrustL2Threshold,
            ["trust.l3"] = ScoringConstants.TrustL3Threshold,
            ["trust.l4"] = ScoringConstants.TrustL4Threshold,
            ["trust.l5"] = ScoringConstants.TrustL5Threshold
        }.ToImmutableSortedDictionary(StringComparer.Ordinal),
        Caps = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["coverage.lowScore"] = ScoringConstants.LowCoverageScoreCap,
            ["security.authProtocolPenalty"] = ScoringConstants.MaxAuthProtocolPenalty,
            ["trust.incompleteEvidenceMaxLevel"] = (double)McpTrustLevel.L3_Acceptable,
            ["trust.mustFailureMaxLevel"] = (double)McpTrustLevel.L2_Caution
        }.ToImmutableSortedDictionary(StringComparer.Ordinal),
        Parameters = GetNumericScoringParameters(),
        PatternSets = GetScoringPatternSets()
        };
    }

    private static ImmutableSortedDictionary<string, double> GetNumericScoringParameters()
    {
        var parameters = typeof(ScoringConstants)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(field => field.IsLiteral && !field.IsInitOnly && field.Name != nameof(ScoringConstants.PolicyVersion))
            .Select(field => (field.Name, Value: field.GetRawConstantValue()))
            .Where(pair => pair.Value is byte or short or int or long or float or double or decimal)
            .ToDictionary(
                pair => pair.Name,
                pair => Convert.ToDouble(pair.Value, System.Globalization.CultureInfo.InvariantCulture),
                StringComparer.Ordinal);
        return parameters.ToImmutableSortedDictionary(StringComparer.Ordinal);
    }

    private static IReadOnlyDictionary<string, string> GetRuleRevisions()
    {
        var rules = ValidationFindingRuleIds.GetVersionedRules()
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        rules["MCP.OUTCOME.DETERMINISTIC_FAILURE"] = ValidationOutcomeTaxonomy.Version;
        rules["MCP.COVERAGE.EVIDENCE_DEBT"] = CurrentVersion;
        rules["MCP.COVERAGE.LOW_CONFIDENCE"] = CurrentVersion;
        rules["MCP.ATTACK.UNKNOWN"] = CurrentVersion;
        rules["MCP.TIER.*"] = CurrentVersion;
        foreach (var rule in new[]
                 {
                     "SCORING.AGGREGATE.BLOCKING_AUTHENTICATION_EXPOSURE",
                     "SCORING.AGGREGATE.CRITICAL_VULNERABILITIES",
                     "SCORING.AGGREGATE.SEVERE_SECURITY_DEGRADATION",
                     "SCORING.AGGREGATE.AUTH_GUIDANCE_GAP",
                     "SCORING.AGGREGATE.CRITICAL_PROTOCOL_VIOLATION",
                     "SCORING.AGGREGATE.JSON_RPC_FORMAT",
                     "SCORING.TRUST.INCOMPLETE_EVIDENCE_CAP",
                     "SCORING.TRUST.MUST_FAILURE_CAP",
                     "SCORING.TRUST.EXFILTRATION_EXPOSURE",
                     "SCORING.TRUST.PROMPT_INJECTION_EXPOSURE",
                     "SCORING.TRUST.INJECTION_REFLECTION",
                     "SCORING.TRUST.LLM_FRIENDLINESS",
                     "SCORING.PERFORMANCE.FAILURES",
                     "SCORING.PERFORMANCE.LATENCY",
                     "SCORING.AI_READINESS.SCHEMA",
                     "SCORING.AI_READINESS.TOKEN_BUDGET",
                     "SCORING.CONTENT_SAFETY.KEYWORDS"
                 })
        {
            rules[rule] = ScoringConstants.PolicyVersion;
        }
        foreach (var kind in Enum.GetValues<AiBoundaryKind>())
        {
            rules[$"MCP.AI.BOUNDARY.{kind}"] = CurrentVersion;
        }
        foreach (var axis in Enum.GetValues<ContentRiskAxis>())
        {
            rules[$"MCP.CONTENT.{axis}"] = CurrentVersion;
        }
        foreach (var attackId in new[]
                 {
                     "MCP-AI-001",
                     "MCP-SEC-001",
                     "MCP-SEC-002",
                     "MCP-SEC-003",
                     ValidationConstants.AttackVectors.InputValidation1,
                     ValidationConstants.AttackVectors.InputValidation2,
                     ValidationConstants.AttackVectors.InputValidation3
                 })
        {
            rules[attackId] = CurrentVersion;
        }

        return new SortedDictionary<string, string>(rules, StringComparer.Ordinal);
    }

    private static ImmutableSortedDictionary<string, ImmutableArray<string>> GetScoringPatternSets() =>
        new Dictionary<string, ImmutableArray<string>>(StringComparer.Ordinal)
        {
            ["exfiltration.behavior"] = ScoringConstants.ExfiltrationBehaviorPatterns.OrderBy(value => value, StringComparer.Ordinal).ToImmutableArray(),
            ["exfiltration.targetParameter"] = ScoringConstants.ExfiltrationTargetParameterPatterns.OrderBy(value => value, StringComparer.Ordinal).ToImmutableArray(),
            ["promptInjection"] = ScoringConstants.PromptInjectionPatterns.OrderBy(value => value, StringComparer.Ordinal).ToImmutableArray(),
            ["promptInjection.sentenceLeading"] = ScoringConstants.SentenceLeadingPromptInjectionPatterns.OrderBy(value => value, StringComparer.Ordinal).ToImmutableArray(),
            ["tool.destructiveName"] = ScoringConstants.DestructiveToolNamePatterns.OrderBy(value => value, StringComparer.Ordinal).ToImmutableArray(),
            ["contentSafety.systemImpact.high"] = ScoringConstants.ContentSafetySystemImpactHighKeywords.OrderBy(value => value, StringComparer.Ordinal).ToImmutableArray(),
            ["contentSafety.systemImpact.medium"] = ScoringConstants.ContentSafetySystemImpactMediumKeywords.OrderBy(value => value, StringComparer.Ordinal).ToImmutableArray(),
            ["contentSafety.dataExfiltration.high"] = ScoringConstants.ContentSafetyDataExfiltrationHighKeywords.OrderBy(value => value, StringComparer.Ordinal).ToImmutableArray(),
            ["contentSafety.dataExfiltration.medium"] = ScoringConstants.ContentSafetyDataExfiltrationMediumKeywords.OrderBy(value => value, StringComparer.Ordinal).ToImmutableArray(),
            ["contentSafety.abuse.high"] = ScoringConstants.ContentSafetyAbuseHighKeywords.OrderBy(value => value, StringComparer.Ordinal).ToImmutableArray(),
            ["contentSafety.abuse.medium"] = ScoringConstants.ContentSafetyAbuseMediumKeywords.OrderBy(value => value, StringComparer.Ordinal).ToImmutableArray(),
            ["aiReadiness.enumeratedChoice"] = ScoringConstants.AiEnumeratedChoiceMarkers.OrderBy(value => value, StringComparer.Ordinal).ToImmutableArray(),
            ["aiReadiness.structuredFormat"] = ScoringConstants.AiStructuredFormatMarkers.OrderBy(value => value, StringComparer.Ordinal).ToImmutableArray(),
            ["toolError.upstreamHttpStatusRegex"] = [ScoringConstants.UpstreamHttpStatusPattern],
            ["toolError.upstreamMethodUrlRegex"] = [ScoringConstants.UpstreamMethodUrlPattern]
        }.ToImmutableSortedDictionary(StringComparer.Ordinal);
}