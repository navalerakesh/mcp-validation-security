using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Constants;

namespace Mcp.Benchmark.Core.Services;

public static class AssuranceExitCodeMapper
{
    public const int Satisfied = 0;
    public const int Failed = 1;
    public const int Incomplete = 2;
    public const int UsageError = 64;
    public const int RuntimeIntegrityFailure = 70;
    public const int TargetUnavailable = 75;
    public const int TimeoutOrCancellation = 124;

    public static int Map(ValidationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.OverallStatus == ValidationStatus.Cancelled) return TimeoutOrCancellation;
        if (IsTargetUnavailable(result)) return TargetUnavailable;
        if (result.Run.TerminationOutcome == ValidationOutcome.Error) return RuntimeIntegrityFailure;
        if (result.OverallStatus == ValidationStatus.Error && result.CriticalErrors.Count > 0)
            return RuntimeIntegrityFailure;
        var exitCode = Map(result.Assurance, result.PolicyOutcome);
        return exitCode == Satisfied && result.Run.TerminationOutcome != ValidationOutcome.Succeeded
            ? Incomplete
            : exitCode;
    }

    public static int Map(AssuranceEvaluation assurance, ValidationPolicyOutcome? policyOutcome)
    {
        ArgumentNullException.ThrowIfNull(assurance);

        if (string.Equals(assurance.Profile.ProfileId, "provider", StringComparison.Ordinal))
        {
            return assurance.CertificateEligibility.Status switch
            {
                CertificateEligibilityStatus.Eligible => Satisfied,
                CertificateEligibilityStatus.ReviewRequired => Incomplete,
                _ => Failed
            };
        }

        var comprehensive = string.Equals(assurance.Profile.ProfileId, AssuranceProfileCatalog.Comprehensive.ProfileId, StringComparison.Ordinal);
        var requiredExtensions = assurance.RequiredExtensionIds ?? [];
        var selectedExtensions = assurance.Assessments.ExtensionReadiness
            .Where(extension => requiredExtensions.Contains(extension.ExtensionId, StringComparer.Ordinal)).ToArray();
        var requiredComprehensiveClaims = comprehensive ? assurance.Claims.Where(claim =>
            claim.Applicability == AssuranceApplicability.Applicable &&
            (claim.Dimension != AssuranceDimension.Extension || requiredExtensions.Contains(claim.Subject, StringComparer.Ordinal)) &&
            claim.Authority is not (AssuranceAuthority.Heuristic or AssuranceAuthority.ModelAdvisory) &&
            (claim.RequirementLevel is AssuranceRequirementLevel.Must or AssuranceRequirementLevel.SecurityControl or AssuranceRequirementLevel.OperationalObjective ||
             claim.RequirementLevel == AssuranceRequirementLevel.Policy &&
             claim.Dimension is (AssuranceDimension.CoreProtocol or AssuranceDimension.CapabilityContract) &&
             claim.Outcome is (AssuranceClaimOutcome.Violated or AssuranceClaimOutcome.Error or AssuranceClaimOutcome.Incomplete) ||
             claim.Dimension is AssuranceDimension.ControlledApplication or AssuranceDimension.ExecutionIntegrity)).ToArray() : [];
        if (comprehensive && (requiredComprehensiveClaims.Any(claim => claim.Outcome is AssuranceClaimOutcome.Violated or AssuranceClaimOutcome.Error) ||
            selectedExtensions.Any(extension => extension.Status == ExtensionReadinessStatus.IssuesFound)))
            return Failed;

        if (comprehensive || string.Equals(assurance.Profile.ProfileId, "ci", StringComparison.Ordinal))
        {
            if (assurance.Assessments.CoreConformance.Status == CoreConformanceStatus.Nonconformant ||
                assurance.Assessments.SecurityAssurance.Status == SecurityAssuranceStatus.IssuesFound ||
                assurance.Assessments.OperationalReadiness.Status == OperationalReadinessStatus.NotReady ||
                policyOutcome is { Passed: false })
            {
                return Failed;
            }

            if (assurance.Assessments.CoreConformance.Status == CoreConformanceStatus.Incomplete ||
                assurance.Assessments.SecurityAssurance.Status == SecurityAssuranceStatus.Incomplete ||
                assurance.Assessments.OperationalReadiness.Status == OperationalReadinessStatus.Incomplete)
            {
                return Incomplete;
            }
        }

        if (comprehensive && (assurance.Assessments.CoreConformance.Status != CoreConformanceStatus.Conformant ||
            assurance.Assessments.SecurityAssurance.Status != SecurityAssuranceStatus.Verified ||
            assurance.Assessments.OperationalReadiness.Status != OperationalReadinessStatus.Ready ||
            assurance.Assessments.ExternalConformance.VerificationStatus != OfficialConformanceVerificationStatus.Verified ||
            assurance.Assessments.ExternalConformance.Status is not (ExternalConformanceStatus.Verified or ExternalConformanceStatus.Nonconformant or ExternalConformanceStatus.Incomplete) ||
            assurance.RequiredExtensionIds == null ||
            assurance.RequiredIdentityControls == null ||
            ControlledIdentitySecurityBaseline.RequiredClaimIds.Any(claimId => assurance.Claims.Count(claim =>
                claim.ClaimId == claimId && claim.Dimension == AssuranceDimension.Security &&
                claim.RequirementLevel == AssuranceRequirementLevel.SecurityControl && claim.Authority == AssuranceAuthority.DeterministicSecurity &&
                claim.Outcome == AssuranceClaimOutcome.Satisfied) != 1) ||
            requiredExtensions.Any(extensionId => selectedExtensions.Count(extension => extension.ExtensionId == extensionId) != 1) ||
            selectedExtensions.Any(extension => extension.Status != ExtensionReadinessStatus.Ready) ||
            assurance.EvidenceSummary.TotalDeclarations == 0 || assurance.EvidenceSummary.EvidenceCoverageRatio != 1 ||
            assurance.EvidenceSummary.Covered != assurance.EvidenceSummary.ApplicableDeclarations ||
            requiredComprehensiveClaims.Any(claim => claim.Outcome is not (AssuranceClaimOutcome.Satisfied or AssuranceClaimOutcome.NotApplicable))))
            return Incomplete;

        return policyOutcome?.RecommendedExitCode ?? Satisfied;
    }

    private static bool IsTargetUnavailable(ValidationResult result)
    {
        if (result.BootstrapHealth?.Disposition == HealthCheckDisposition.Unhealthy) return true;
        var baselineCoverage = result.Evidence.Coverage
            .Where(ValidationEvidenceLaneClassifier.IsBaseline)
            .Where(coverage => coverage.Status != ValidationCoverageStatus.NotApplicable)
            .ToArray();
        return baselineCoverage.Length > 0 &&
            baselineCoverage.All(coverage => coverage.Status == ValidationCoverageStatus.Unavailable);
    }
}