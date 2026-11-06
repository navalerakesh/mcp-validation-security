using Mcp.Benchmark.Core.Models;

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
        if (IsTargetUnavailable(result)) return TargetUnavailable;
        if (result.OverallStatus == ValidationStatus.Error && result.CriticalErrors.Count > 0)
            return RuntimeIntegrityFailure;
        return Map(result.Assurance, result.PolicyOutcome);
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

        if (string.Equals(assurance.Profile.ProfileId, "ci", StringComparison.Ordinal))
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