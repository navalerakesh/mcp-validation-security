namespace Mcp.Benchmark.Core.Models;

public enum ControlledStateExpectation
{
    Unspecified,
    ChangeRequired,
    NoOpPermitted,
    UnchangedRequired
}

public enum ControlledApplicationOutcome
{
    Unknown,
    Succeeded,
    Rejected,
    Pending
}

public sealed record ControlledOperationBinding(
    string RunId,
    string OperationId,
    string AttemptId,
    string RequestId,
    string BoundaryId,
    string TargetIdentitySha256,
    string RouteIdentitySha256,
    string ArgumentsSha256,
    string ActorAlias,
    string ObjectAlias);

public sealed record ControlledStateScenarioContract
{
    public const string CurrentRevision = "1.0.0";
    public const int MaximumIdentifierCharacters = 512;

    public string ContractRevision { get; init; } = CurrentRevision;
    public required string ScenarioId { get; init; }
    public required string PolicyRevision { get; init; }
    public required string ToolName { get; init; }
    public required string ObserverId { get; init; }
    public required string PredicateId { get; init; }
    public required string PreconditionId { get; init; }
    public string? AuditPredicateId { get; init; }
    public required string CleanupOwner { get; init; }
    public required ControlledOperationBinding Binding { get; init; }
    public ControlledStateExpectation StateExpectation { get; init; }
    public ControlledApplicationOutcome ExpectedApplicationOutcome { get; init; }
    public TimeSpan ConsistencyWindow { get; init; }
    public TimeSpan MaximumBaselineAge { get; init; }
    public TimeSpan MaximumEvidenceAge { get; init; }
    public int MaximumObserverReads { get; init; }
    public bool AuditRequired { get; init; }
}

public sealed record ControlledStateEvidence
{
    public required ControlledOperationBinding Binding { get; init; }
    public required string ObserverId { get; init; }
    public required string PredicateId { get; init; }
    public required string PreconditionId { get; init; }
    public string? AuditPredicateId { get; init; }
    public DateTimeOffset? BaselineAt { get; init; }
    public DateTimeOffset? OperationStartedAt { get; init; }
    public DateTimeOffset? OperationEndedAt { get; init; }
    public DateTimeOffset? ObservedThrough { get; init; }
    public ValidationOutcome ObserverOutcome { get; init; } = ValidationOutcome.Unavailable;
    public bool ObservationComplete { get; init; }
    public bool? PreconditionsSatisfied { get; init; }
    public bool? BeforePredicateSatisfied { get; init; }
    public bool? AfterPredicateSatisfied { get; init; }
    public bool? StateChanged { get; init; }
    public ControlledApplicationOutcome ApplicationOutcome { get; init; }
    public bool? AuditMatched { get; init; }
    public ValidationOutcome AuditObserverOutcome { get; init; } = ValidationOutcome.Unavailable;
    public bool AuditObservationComplete { get; init; }
    public DateTimeOffset? AuditObservedThrough { get; init; }
    public int ObserverReads { get; init; }
    public ValidationOutcome CleanupOutcome { get; init; } = ValidationOutcome.NotEvaluated;
}

public sealed record ControlledStateAssessment
{
    public string? SourceEvidenceSha256 { get; init; }
    public string? VerifiedObserverKeyId { get; init; }
    public ControlledStateScenarioContract? AdmittedContract { get; init; }
    public ControlledStateEvidence? RecordedEvidence { get; init; }
    public DateTimeOffset AssessedAt { get; init; }
    public required ValidationOutcome Outcome { get; init; }
    public required ValidationOutcome StateOutcome { get; init; }
    public required ValidationOutcome ApplicationOutcome { get; init; }
    public required ValidationOutcome AuditOutcome { get; init; }
    public required ValidationOutcome CleanupOutcome { get; init; }
    public required string Reason { get; init; }
    public bool RetryPermitted => false;
}