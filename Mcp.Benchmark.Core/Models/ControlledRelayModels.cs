using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Utilities;

namespace Mcp.Benchmark.Core.Models;

public sealed record ControlledRelayBoundaryContract
{
    public required ControlledOperationBinding Binding { get; init; }
    public string? ParentRequestId { get; init; }
    public required string ProtocolVersion { get; init; }
    public required string Transport { get; init; }
    public required string Method { get; init; }
    public required string Subject { get; init; }
    public required string CapabilityDigest { get; init; }
    public required string ResponseDigest { get; init; }
    public int? ExpectedHttpStatus { get; init; }
    public int? ExpectedProtocolError { get; init; }
    public bool ExpectedApplicationError { get; init; }
    public bool ExpectedCancellation { get; init; }
    public int MaximumAttempts { get; init; } = 1;
}

public sealed record ControlledRelayContract
{
    public const string Revision = "1.0.0";
    public string ContractRevision { get; init; } = Revision;
    public required string ScenarioId { get; init; }
    public required string PolicyRevision { get; init; }
    public required string ObserverId { get; init; }
    public required IReadOnlyList<ControlledRelayBoundaryContract> Boundaries { get; init; }
    public TimeSpan MaximumDuration { get; init; }
    public TimeSpan MaximumEvidenceAge { get; init; }
    public int MaximumObserverReads { get; init; }
    public ControlledStateScenarioContract? EffectContract { get; init; }
}

public sealed record ControlledRelayObservation
{
    public required ControlledOperationBinding Binding { get; init; }
    public string? ParentRequestId { get; init; }
    public string? ResponseRequestId { get; init; }
    public required string ObserverId { get; init; }
    public required string ProtocolVersion { get; init; }
    public required string Transport { get; init; }
    public required string Method { get; init; }
    public required string Subject { get; init; }
    public required string CapabilityDigest { get; init; }
    public required string ResponseDigest { get; init; }
    public int? HttpStatus { get; init; }
    public int? ProtocolError { get; init; }
    public bool? ApplicationError { get; init; }
    public bool? EffectiveActorMatched { get; init; }
    public bool? AudienceMatched { get; init; }
    public bool? CancellationObserved { get; init; }
    public int Attempts { get; init; }
    public int ObserverReads { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public DateTimeOffset? ObservedThrough { get; init; }
    public ValidationOutcome ObserverOutcome { get; init; } = ValidationOutcome.Unavailable;
    public bool Complete { get; init; }
    public ValidationOutcome CleanupOutcome { get; init; } = ValidationOutcome.NotEvaluated;
}

public sealed record ControlledRelayCheck(string BoundaryId, string CheckId, ValidationOutcome Outcome);

public sealed record ControlledRelayAssessment
{
    public required ValidationOutcome Outcome { get; init; }
    public required IReadOnlyList<ControlledRelayCheck> Checks { get; init; }
    public required string Reason { get; init; }
    public ControlledRelayContract? AdmittedContract { get; init; }
    public IReadOnlyList<ControlledRelayObservation>? Observations { get; init; }
    public ControlledStateAssessment? Effect { get; init; }
    public DateTimeOffset AssessedAt { get; init; }
    public string? SourceEvidenceSha256 { get; init; }
    public string? VerifiedObserverKeyId { get; init; }
}

public sealed record ControlledRelayEvidenceDocument : ISignedControlledEvidence
{
    public const string Type = "mcpval.controlled-relay-evidence";
    public string DocumentType { get; init; } = Type;
    public string DocumentSchemaVersion { get; init; } = ControlledRelayContract.Revision;
    public required ControlledRelayContract Contract { get; init; }
    public required IReadOnlyList<ControlledRelayObservation> Observations { get; init; }
    public ControlledStateEvidence? EffectEvidence { get; init; }
    public DateTimeOffset IssuedAt { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
    public string SignatureAlgorithm { get; init; } = P256ArtifactSignatures.Algorithm;
    public required string SigningKeyId { get; init; }
    public required string SignatureBase64 { get; init; }
    public string ComputePayloadDigest() => CanonicalJsonDigest.ComputeObject(new
    {
        DocumentType, DocumentSchemaVersion, Contract, Observations, EffectEvidence, IssuedAt, ExpiresAt, SignatureAlgorithm, SigningKeyId
    });
}
