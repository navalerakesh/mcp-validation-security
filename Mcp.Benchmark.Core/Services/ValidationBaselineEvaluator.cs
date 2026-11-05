using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.Core.Services;

public static class ValidationBaselineEvaluator
{
    public static AssuranceBaselineComparison Compare(ValidationResult baseline, ValidationResult current)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(current);
        if (!string.Equals(baseline.DocumentType, Constants.ArtifactContracts.ValidationResultDocumentType, StringComparison.Ordinal) ||
            !Constants.ArtifactContracts.IsCompatible(baseline.DocumentType, baseline.DocumentSchemaVersion))
        {
            throw new ArgumentException("Baseline must be a compatible V2 assurance result.", nameof(baseline));
        }
        if (!string.Equals(baseline.Assurance.Profile.ProfileId, current.Assurance.Profile.ProfileId, StringComparison.Ordinal) ||
            !string.Equals(baseline.Assurance.Profile.Revision, current.Assurance.Profile.Revision, StringComparison.Ordinal))
        {
            throw new ArgumentException("Baseline and current results must use the same assurance profile and revision.", nameof(baseline));
        }

        var baselineClaims = GetNonSuccessClaims(baseline).ToDictionary(claim => claim.ClaimId, StringComparer.Ordinal);
        var currentClaims = GetNonSuccessClaims(current).ToDictionary(claim => claim.ClaimId, StringComparer.Ordinal);

        var newClaims = currentClaims
            .Where(pair => !baselineClaims.TryGetValue(pair.Key, out var previous) || OutcomeRank(pair.Value.Outcome) > OutcomeRank(previous.Outcome))
            .Select(pair => new BaselineClaimChange(pair.Value.ClaimId, pair.Value.Dimension, pair.Value.Authority, pair.Value.Outcome, pair.Value.Subject))
            .OrderBy(change => change.ClaimId, StringComparer.Ordinal)
            .ToArray();
        var resolvedClaimIds = baselineClaims.Keys
            .Where(id => !currentClaims.ContainsKey(id))
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

        var before = baseline.Assurance.Assessments;
        var after = current.Assurance.Assessments;
        var coverageDelta = Math.Round(current.Assurance.EvidenceSummary.EvidenceCoverageRatio - baseline.Assurance.EvidenceSummary.EvidenceCoverageRatio, 4);
        var confidenceDelta = Math.Round(current.Assurance.EvidenceSummary.EvidenceConfidenceRatio - baseline.Assurance.EvidenceSummary.EvidenceConfidenceRatio, 4);

        return new AssuranceBaselineComparison
        {
            BaselineValidationId = baseline.ValidationId,
            BaselineDocumentSchemaVersion = baseline.DocumentSchemaVersion,
            ProfileId = current.Assurance.Profile.ProfileId,
            ProfileRevision = current.Assurance.Profile.Revision,
            CoreConformanceBefore = before.CoreConformance.Status,
            CoreConformanceAfter = after.CoreConformance.Status,
            SecurityAssuranceBefore = before.SecurityAssurance.Status,
            SecurityAssuranceAfter = after.SecurityAssurance.Status,
            OperationalReadinessBefore = before.OperationalReadiness.Status,
            OperationalReadinessAfter = after.OperationalReadiness.Status,
            CertificateEligibilityBefore = baseline.Assurance.CertificateEligibility.Status,
            CertificateEligibilityAfter = current.Assurance.CertificateEligibility.Status,
            EvidenceCoverageDelta = coverageDelta,
            EvidenceConfidenceDelta = confidenceDelta,
            IsRegression = newClaims.Length > 0 ||
                CoreRank(after.CoreConformance.Status) < CoreRank(before.CoreConformance.Status) ||
                SecurityRank(after.SecurityAssurance.Status) < SecurityRank(before.SecurityAssurance.Status) ||
                OperationsRank(after.OperationalReadiness.Status) < OperationsRank(before.OperationalReadiness.Status) ||
                CertificateRank(current.Assurance.CertificateEligibility.Status) < CertificateRank(baseline.Assurance.CertificateEligibility.Status),
            NewNonSuccessClaims = newClaims,
            ResolvedNonSuccessClaimIds = resolvedClaimIds
        };
    }

    private static IEnumerable<AssuranceClaim> GetNonSuccessClaims(ValidationResult result) =>
        result.Assurance.Claims.Where(claim => claim.Outcome is not (AssuranceClaimOutcome.Satisfied or AssuranceClaimOutcome.NotApplicable));

    private static int OutcomeRank(AssuranceClaimOutcome outcome) => outcome switch
    {
        AssuranceClaimOutcome.Satisfied or AssuranceClaimOutcome.NotApplicable => 0,
        AssuranceClaimOutcome.ReviewRequired => 1,
        AssuranceClaimOutcome.Incomplete => 2,
        AssuranceClaimOutcome.Violated => 3,
        AssuranceClaimOutcome.Error => 4,
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, null)
    };

    private static int CoreRank(CoreConformanceStatus status) => status switch
    {
        CoreConformanceStatus.Nonconformant => 0,
        CoreConformanceStatus.Incomplete => 1,
        CoreConformanceStatus.Conformant => 2,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
    };

    private static int SecurityRank(SecurityAssuranceStatus status) => status switch
    {
        SecurityAssuranceStatus.IssuesFound => 0,
        SecurityAssuranceStatus.Incomplete => 1,
        SecurityAssuranceStatus.Verified => 2,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
    };

    private static int OperationsRank(OperationalReadinessStatus status) => status switch
    {
        OperationalReadinessStatus.NotReady => 0,
        OperationalReadinessStatus.Incomplete => 1,
        OperationalReadinessStatus.Ready => 2,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
    };

    private static int CertificateRank(CertificateEligibilityStatus status) => status switch
    {
        CertificateEligibilityStatus.Ineligible => 0,
        CertificateEligibilityStatus.ReviewRequired => 1,
        CertificateEligibilityStatus.Eligible => 2,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
    };
}
