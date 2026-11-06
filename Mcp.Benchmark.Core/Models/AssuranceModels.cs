namespace Mcp.Benchmark.Core.Models;

public enum AssuranceDimension
{
    CoreProtocol,
    CapabilityContract,
    Security,
    Operations,
    Extension,
    ClientCompatibility,
    ExternalConformance,
    ExecutionIntegrity
}

public enum AssuranceRequirementLevel
{
    Must,
    Should,
    May,
    SecurityControl,
    OperationalObjective,
    Policy
}

public enum AssuranceAuthority
{
    OfficialSpecification,
    OfficialConformance,
    DeterministicSecurity,
    OperationalMeasurement,
    ProviderPolicy,
    Heuristic,
    ModelAdvisory
}

public enum AssuranceClaimOutcome
{
    Satisfied,
    Violated,
    Incomplete,
    ReviewRequired,
    NotApplicable,
    Error
}

public enum AssuranceApplicability
{
    Applicable,
    NotApplicable
}

public sealed class AssuranceSourceReference
{
    public string? Uri { get; init; }

    public required string Revision { get; init; }

    public string? Sha256 { get; init; }
}

public sealed class AssuranceClaim
{
    public required string ClaimId { get; init; }

    public required string Subject { get; init; }

    public required AssuranceDimension Dimension { get; init; }

    public required AssuranceRequirementLevel RequirementLevel { get; init; }

    public required AssuranceAuthority Authority { get; init; }

    public required AssuranceSourceReference Source { get; init; }

    public AssuranceApplicability Applicability { get; init; } = AssuranceApplicability.Applicable;

    public string? ApplicabilityReason { get; init; }

    public required AssuranceClaimOutcome Outcome { get; init; }

    public EvidenceConfidenceLevel Confidence { get; init; } = EvidenceConfidenceLevel.None;

    public IReadOnlyList<string> EvidenceIds { get; init; } = Array.Empty<string>();

    public required string Summary { get; init; }

    public string? Remediation { get; init; }

    public Dictionary<string, string> Metadata { get; init; } = new(StringComparer.Ordinal);
}

public enum CoreConformanceStatus
{
    Conformant,
    Nonconformant,
    Incomplete
}

public enum SecurityAssuranceStatus
{
    Verified,
    IssuesFound,
    Incomplete
}

public enum OperationalReadinessStatus
{
    Ready,
    NotReady,
    Incomplete
}

public sealed class CoreConformanceAssessment
{
    public CoreConformanceStatus Status { get; init; } = CoreConformanceStatus.Incomplete;

    public int RequiredClaims { get; init; }

    public int SatisfiedRequiredClaims { get; init; }

    public IReadOnlyList<string> BlockingClaimIds { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> IncompleteClaimIds { get; init; } = Array.Empty<string>();
}

public sealed class SecurityAssuranceAssessment
{
    public SecurityAssuranceStatus Status { get; init; } = SecurityAssuranceStatus.Incomplete;

    public int DeterministicControlClaims { get; init; }

    public int ReviewClaimCount { get; init; }

    public IReadOnlyList<string> IssueClaimIds { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> IncompleteClaimIds { get; init; } = Array.Empty<string>();
}

public sealed class OperationalReadinessAssessment
{
    public OperationalReadinessStatus Status { get; init; } = OperationalReadinessStatus.Incomplete;

    public int ObjectiveClaims { get; init; }

    public IReadOnlyList<string> FailedClaimIds { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> IncompleteClaimIds { get; init; } = Array.Empty<string>();
}

public enum ExtensionReadinessStatus
{
    Ready,
    IssuesFound,
    Incomplete
}

public sealed class ExtensionAssuranceAssessment
{
    public required string ExtensionId { get; init; }

    public ExtensionLifecycleStage Stage { get; init; }

    public required string SpecificationRevision { get; init; }

    public required string SpecificationSha256 { get; init; }

    public ExtensionReadinessStatus Status { get; init; } = ExtensionReadinessStatus.Incomplete;

    public IReadOnlyList<string> NonSuccessClaimIds { get; init; } = Array.Empty<string>();
}

public enum ClientCompatibilityVerification
{
    Interpreted,
    Verified
}

public sealed class ClientCompatibilityAssuranceAssessment
{
    public required string ProfileId { get; init; }

    public required string Revision { get; init; }

    public ClientProfileCompatibilityStatus Status { get; init; }

    public ClientCompatibilityVerification Verification { get; init; } = ClientCompatibilityVerification.Interpreted;

    public required string Summary { get; init; }
}

public enum ExternalConformanceStatus
{
    NotProvided,
    Verified,
    Nonconformant,
    Incomplete,
    Conflict
}

public sealed class ExternalConformanceAssuranceAssessment
{
    public ExternalConformanceStatus Status { get; init; } = ExternalConformanceStatus.NotProvided;

    public string? RequirementSetRevision { get; init; }

    public string? RequirementSetSha256 { get; init; }

    public int CreditedPassed { get; init; }

    public int Failed { get; init; }

    public int Skipped { get; init; }

    public int ConflictCount { get; init; }
}

public enum CertificateEligibilityStatus
{
    Eligible,
    Ineligible,
    ReviewRequired
}

public sealed class CertificateEligibilityAssessment
{
    public CertificateEligibilityStatus Status { get; init; } = CertificateEligibilityStatus.Ineligible;

    public required string ProfileId { get; init; }

    public IReadOnlyList<string> Reasons { get; init; } = Array.Empty<string>();

    public double EvidenceCoverageRatio { get; init; }

    public double EvidenceConfidenceRatio { get; init; }
}

public sealed class AssuranceAssessmentSet
{
    public CoreConformanceAssessment CoreConformance { get; init; } = new();

    public SecurityAssuranceAssessment SecurityAssurance { get; init; } = new();

    public OperationalReadinessAssessment OperationalReadiness { get; init; } = new();

    public IReadOnlyList<ExtensionAssuranceAssessment> ExtensionReadiness { get; init; } = Array.Empty<ExtensionAssuranceAssessment>();

    public IReadOnlyList<ClientCompatibilityAssuranceAssessment> ClientCompatibility { get; init; } = Array.Empty<ClientCompatibilityAssuranceAssessment>();

    public ExternalConformanceAssuranceAssessment ExternalConformance { get; init; } = new();
}

public sealed class AssuranceProfileManifest
{
    public required string ProfileId { get; init; }

    public required string Revision { get; init; }

    public ExecutionMode MinimumExecutionMode { get; init; }

    public double MinimumEvidenceCoverageRatio { get; init; }

    public double MinimumEvidenceConfidenceRatio { get; init; }

    public double MinimumRepeatedRunConsistency { get; init; }

    public bool RequiresOfficialConformance { get; init; }

    public bool RequiresSignedAttestation { get; init; }

    public bool RequiresRepeatedRuns { get; init; }

    public bool CertificateEligible { get; init; }
}

public sealed class AssuranceEvaluationContext
{
    public ExecutionMode ExecutionMode { get; init; } = ExecutionMode.Safe;

    public bool HasSignedAttestation { get; init; }

    public double? RepeatedRunConsistency { get; init; }

    public ProviderHarnessAssessment? ProviderHarness { get; init; }

    public IReadOnlyList<string> RequiredExtensionIds { get; init; } = Array.Empty<string>();

    public int ActiveWaiverCount { get; init; }

    public int InvalidWaiverCount { get; init; }
}

public sealed class AssuranceEvaluation
{
    public string ContractVersion { get; init; } = "2.0.0";

    public required AssuranceProfileManifest Profile { get; init; }

    public IReadOnlyList<AssuranceClaim> Claims { get; init; } = Array.Empty<AssuranceClaim>();

    public AssuranceAssessmentSet Assessments { get; init; } = new();

    public EvidenceCoverageSummary EvidenceSummary { get; init; } = new();

    public required CertificateEligibilityAssessment CertificateEligibility { get; init; }
}