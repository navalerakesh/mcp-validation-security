namespace Mcp.Benchmark.Core.Models;

public enum ControlledAuthorizationDecision { Unknown, Allowed, Denied }

public enum ControlledIdentityRole { Unknown, CallerAssertion, ApproverAssertion, ResourceSelector, DisplayOnly, DelegationReference }

public enum ControlledIdentityChallenge { ActorSubstitution, InsufficientScope, UnauthorizedActorOrObject }

public sealed record ControlledIdentityAdmission(string RunId, string TargetIdentitySha256, string RouteIdentitySha256, string ObserverId);

public sealed record ControlledIdentitySecurityRequirement(
    ControlledIdentityMatrixContract Contract,
    ControlledIdentityAdmission Admission,
    string SignedEvidenceJson,
    string TrustedObserverPublicKeyPem,
    string ActorBindingCaseId,
    string ScopeEnforcementCaseId,
    string AuthorizationCaseId);

public sealed record ControlledIdentityCase
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public ControlledIdentityChallenge? SecurityChallenge { get; init; }

    public required string CaseId { get; init; }
    public required ControlledOperationBinding Binding { get; init; }
    public required string ScopePredicateId { get; init; }
    public required string ExpectedEffectiveActorAlias { get; init; }
    public ControlledAuthorizationDecision ExpectedDecision { get; init; }
    public ControlledIdentityRole IdentityRole { get; init; }
    public bool IsPositiveControl { get; init; }
    public string? PositiveControlCaseId { get; init; }
}

public sealed record ControlledIdentityMatrixContract
{
    public const string LegacyRevision = "1.0.0";
    public const string CurrentRevision = "1.1.0";
    public static bool IsSupportedRevision(string revision) => revision is LegacyRevision or CurrentRevision;
    public const int MaximumCases = 64;
    public string ContractRevision { get; init; } = CurrentRevision;
    public required string PolicyRevision { get; init; }
    public required string ToolName { get; init; }
    public required string ObserverId { get; init; }
    public required IReadOnlyList<ControlledIdentityCase> Cases { get; init; }
    public TimeSpan MaximumEvidenceAge { get; init; }
    public TimeSpan MaximumObservationDelay { get; init; }
}

public sealed record ControlledIdentityObservation
{
    public required string CaseId { get; init; }
    public required ControlledOperationBinding Binding { get; init; }
    public required string ObserverId { get; init; }
    public required string ScopePredicateId { get; init; }
    public DateTimeOffset? OperationStartedAt { get; init; }
    public DateTimeOffset? OperationEndedAt { get; init; }
    public DateTimeOffset? ObservedAt { get; init; }
    public ValidationOutcome ObserverOutcome { get; init; } = ValidationOutcome.Unavailable;
    public bool Complete { get; init; }
    public bool? PreconditionsSatisfied { get; init; }
    public ControlledAuthorizationDecision Decision { get; init; }
    public bool? EffectiveActorMatched { get; init; }
    public bool? AuthenticationBindingMatched { get; init; }
    public bool? ScopeMatched { get; init; }
    public bool? DelegationBindingMatched { get; init; }
    public bool? ApprovalBindingMatched { get; init; }
}

public sealed record ControlledIdentityCaseAssessment
{
    public required string CaseId { get; init; }
    public required ValidationOutcome Outcome { get; init; }
    public required ValidationOutcome ObservationOutcome { get; init; }
    public required ValidationOutcome DecisionOutcome { get; init; }
    public required ValidationOutcome EffectiveActorOutcome { get; init; }
    public required ValidationOutcome AuthenticationBindingOutcome { get; init; }
    public required ValidationOutcome ScopeOutcome { get; init; }
    public required ValidationOutcome RoleBindingOutcome { get; init; }
    public required ValidationOutcome PositiveControlOutcome { get; init; }
}

public sealed record ControlledIdentityMatrixAssessment
{
    public string? SourceEvidenceSha256 { get; init; }
    public string? VerifiedObserverKeyId { get; init; }
    public ControlledIdentityMatrixContract? AdmittedContract { get; init; }
    public IReadOnlyList<ControlledIdentityObservation>? RecordedObservations { get; init; }
    public IReadOnlyList<ControlledIdentityEvidenceGap> RecordedGaps { get; init; } = [];
    public ValidationOutcome EvidenceIntegrityOutcome { get; init; } = ValidationOutcome.Blocked;
    public DateTimeOffset AssessedAt { get; init; }
    public required ValidationOutcome Outcome { get; init; }
    public required IReadOnlyList<ControlledIdentityCaseAssessment> Cases { get; init; }
    public required string Reason { get; init; }
    public bool RetryPermitted => false;
}

public sealed record ControlledIdentityEvidenceGap(string? CaseId, int RejectedObservations);