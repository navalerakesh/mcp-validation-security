using System.Text.Json;
using Mcp.Benchmark.Core.Constants;

namespace Mcp.Benchmark.Core.Models;

public enum OfficialConformanceCheckStatus
{
    Success,
    Failure,
    Warning,
    Skipped,
    Info
}

public enum OfficialConformanceRunState
{
    Completed,
    Failed,
    TimedOut,
    Cancelled
}

public enum OfficialConformanceVerificationStatus
{
    Verified,
    Unverified,
    Incomplete,
    Conflict
}

public enum OfficialConformanceScoringDisposition
{
    Required,
    NotScored
}

public sealed class OfficialConformanceEvidenceDocument
{
    public string DocumentType { get; init; } = ArtifactContracts.OfficialConformanceEvidenceDocumentType;

    public string DocumentSchemaVersion { get; init; } = ArtifactContracts.OfficialConformanceEvidenceSchemaVersion;

    public required OfficialConformanceRunnerIdentity Runner { get; init; }

    public required OfficialConformanceRequirementSet RequirementSet { get; init; }

    public required OfficialConformanceTargetIdentity Target { get; init; }

    public required OfficialConformanceInvocation Invocation { get; init; }

    public required OfficialConformanceRunRecord Run { get; init; }

    public IReadOnlyList<OfficialConformanceScenarioRecord> Scenarios { get; init; } = Array.Empty<OfficialConformanceScenarioRecord>();
}

public sealed class OfficialConformanceRunnerIdentity
{
    public required string PackageName { get; init; }

    public required string Version { get; init; }

    public required string SourceCommit { get; init; }

    public required string PackageSha256 { get; init; }

    public string? PackageIntegrity { get; init; }
}

public sealed class OfficialConformanceRequirementSet
{
    public required string Revision { get; init; }

    public required string Sha256 { get; init; }
}

public sealed class OfficialConformanceTargetIdentity
{
    public required string IdentitySha256 { get; init; }

    public required string Transport { get; init; }

    public required string ProtocolVersion { get; init; }
}

public sealed class OfficialConformanceInvocation
{
    public required string Mode { get; init; }

    public IReadOnlyList<string> SanitizedArguments { get; init; } = Array.Empty<string>();
}

public sealed class OfficialConformanceRunRecord
{
    public required DateTimeOffset StartedAtUtc { get; init; }

    public required DateTimeOffset EndedAtUtc { get; init; }

    public required int ExitCode { get; init; }

    public required OfficialConformanceRunState State { get; init; }
}

public sealed class OfficialConformanceScenarioRecord
{
    public required string ScenarioId { get; init; }

    public string? ExtensionId { get; init; }

    public OfficialConformanceScoringDisposition ScoringDisposition { get; init; } = OfficialConformanceScoringDisposition.Required;

    public string? NotScoredReason { get; init; }

    public required string ChecksSha256 { get; init; }

    public IReadOnlyList<OfficialConformanceCheckRecord> Checks { get; init; } = Array.Empty<OfficialConformanceCheckRecord>();
}

public sealed class OfficialConformanceCheckRecord
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required string Description { get; init; }

    public required OfficialConformanceCheckStatus Status { get; init; }

    public required DateTimeOffset Timestamp { get; init; }

    public IReadOnlyList<OfficialConformanceSpecReference> SpecReferences { get; init; } = Array.Empty<OfficialConformanceSpecReference>();

    public JsonElement? Details { get; init; }

    public JsonElement? Metadata { get; init; }

    public string? ErrorMessage { get; init; }
}

public sealed class OfficialConformanceSpecReference
{
    public required string Id { get; init; }

    public string? Url { get; init; }
}

public sealed class OfficialConformanceAssessment
{
    public OfficialConformanceVerificationStatus VerificationStatus { get; set; }

    public string SourceEvidenceSha256 { get; init; } = string.Empty;

    public required OfficialConformanceRunnerIdentity Runner { get; init; }

    public required OfficialConformanceRequirementSet RequirementSet { get; init; }

    public required OfficialConformanceTargetIdentity Target { get; init; }

    public OfficialConformanceRunState RunState { get; init; }

    public int Passed { get; init; }

    public int CreditedPassed { get; init; }

    public int Failed { get; init; }

    public int Warnings { get; init; }

    public int Skipped { get; init; }

    public int Informational { get; init; }

    public EvidenceCoverageSummary EvidenceSummary { get; init; } = new();

    public IReadOnlyList<OfficialConformanceScenarioAssessment> Scenarios { get; init; } = Array.Empty<OfficialConformanceScenarioAssessment>();

    public List<OfficialConformanceConflict> Conflicts { get; init; } = new();

    public List<DecisionRecord> Decisions { get; init; } = new();
}

public sealed class OfficialConformanceScenarioAssessment
{
    public required string ScenarioId { get; init; }

    public string? ExtensionId { get; init; }

    public OfficialConformanceScoringDisposition ScoringDisposition { get; init; }

    public string? NotScoredReason { get; init; }

    public int Passed { get; init; }

    public int Failed { get; init; }

    public int Warnings { get; init; }

    public int Skipped { get; init; }

    public int Informational { get; init; }

    public IReadOnlyList<OfficialConformanceCheckAssessment> Checks { get; init; } = Array.Empty<OfficialConformanceCheckAssessment>();
}

public sealed class OfficialConformanceCheckAssessment
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required string Description { get; init; }

    public OfficialConformanceCheckStatus Status { get; init; }

    public IReadOnlyList<OfficialConformanceSpecReference> SpecReferences { get; init; } = Array.Empty<OfficialConformanceSpecReference>();

    public bool HasErrorDetail { get; init; }

    public string? ErrorDetailSha256 { get; init; }
}

public sealed class OfficialConformanceConflict
{
    public required string ConflictId { get; init; }

    public required string Component { get; init; }

    public required string Summary { get; init; }

    public IReadOnlyList<string> EvidenceIds { get; init; } = Array.Empty<string>();
}

public sealed class OfficialConformanceImportResult
{
    public required OfficialConformanceAssessment Assessment { get; init; }

    public IReadOnlyList<ValidationCoverageDeclaration> Coverage { get; init; } = Array.Empty<ValidationCoverageDeclaration>();

    public IReadOnlyList<ValidationObservation> Observations { get; init; } = Array.Empty<ValidationObservation>();

    public IReadOnlyList<DecisionRecord> Decisions { get; init; } = Array.Empty<DecisionRecord>();
}

public sealed class OfficialConformanceImportContext
{
    public string? ExpectedTargetIdentitySha256 { get; init; }

    public string? ExpectedProtocolVersion { get; init; }
}

public sealed class ExtensionAssessment
{
    public required string ExtensionId { get; init; }

    public ExtensionLifecycleStage Stage { get; init; }

    public required string SpecificationRevision { get; init; }

    public required string SpecificationSha256 { get; init; }

    public ValidationVerdict Verdict { get; init; } = ValidationVerdict.Unknown;

    public EvidenceCoverageSummary EvidenceSummary { get; init; } = new();

    public IReadOnlyList<ValidationFinding> Findings { get; init; } = Array.Empty<ValidationFinding>();
}