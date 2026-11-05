namespace Mcp.Benchmark.Core.Models;

public sealed class AssuranceBaselineComparison
{
    public string BaselineValidationId { get; init; } = string.Empty;

    public string BaselineDocumentSchemaVersion { get; init; } = string.Empty;

    public required string ProfileId { get; init; }

    public required string ProfileRevision { get; init; }

    public CoreConformanceStatus CoreConformanceBefore { get; init; }

    public CoreConformanceStatus CoreConformanceAfter { get; init; }

    public SecurityAssuranceStatus SecurityAssuranceBefore { get; init; }

    public SecurityAssuranceStatus SecurityAssuranceAfter { get; init; }

    public OperationalReadinessStatus OperationalReadinessBefore { get; init; }

    public OperationalReadinessStatus OperationalReadinessAfter { get; init; }

    public CertificateEligibilityStatus CertificateEligibilityBefore { get; init; }

    public CertificateEligibilityStatus CertificateEligibilityAfter { get; init; }

    public double EvidenceCoverageDelta { get; init; }

    public double EvidenceConfidenceDelta { get; init; }

    public bool IsRegression { get; init; }

    public IReadOnlyList<BaselineClaimChange> NewNonSuccessClaims { get; init; } = Array.Empty<BaselineClaimChange>();

    public IReadOnlyList<string> ResolvedNonSuccessClaimIds { get; init; } = Array.Empty<string>();
}

public sealed record BaselineClaimChange(
    string ClaimId,
    AssuranceDimension Dimension,
    AssuranceAuthority Authority,
    AssuranceClaimOutcome Outcome,
    string Subject);
