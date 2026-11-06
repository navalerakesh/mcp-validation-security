using Mcp.Benchmark.Core.Constants;
using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.Core.Services;

public static class AssuranceAssessmentEngine
{
    public static ValidationStatus DetermineValidationStatus(
        AssuranceEvaluation evaluation,
        bool hasCriticalErrors = false)
    {
        ArgumentNullException.ThrowIfNull(evaluation);
        if (hasCriticalErrors) return ValidationStatus.Error;
        if (evaluation.Assessments.CoreConformance.Status == CoreConformanceStatus.Nonconformant ||
            evaluation.Assessments.SecurityAssurance.Status == SecurityAssuranceStatus.IssuesFound ||
            evaluation.Assessments.OperationalReadiness.Status == OperationalReadinessStatus.NotReady)
            return ValidationStatus.Failed;
        if (evaluation.Assessments.CoreConformance.Status == CoreConformanceStatus.Incomplete ||
            evaluation.Assessments.SecurityAssurance.Status == SecurityAssuranceStatus.Incomplete ||
            evaluation.Assessments.OperationalReadiness.Status == OperationalReadinessStatus.Incomplete)
            return ValidationStatus.PartiallyCompleted;
        return ValidationStatus.Passed;
    }

    public static AssuranceEvaluation Evaluate(
        ValidationResult result,
        AssuranceProfileManifest profile,
        AssuranceEvaluationContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(profile);
        context ??= new AssuranceEvaluationContext();

        var claims = AssuranceClaimProjector.Project(result);
        var evidenceSummary = ValidationEvidenceSummarizer.Summarize(
            ValidationEvidenceLaneClassifier.BaselineOnly(result.Evidence.Coverage));
        var assessments = new AssuranceAssessmentSet
        {
            CoreConformance = ReduceCore(claims),
            SecurityAssurance = ReduceSecurity(claims, result),
            OperationalReadiness = ReduceOperations(claims, result),
            ExtensionReadiness = ReduceExtensions(claims, result),
            ClientCompatibility = ReduceClientCompatibility(result),
            ExternalConformance = ReduceExternalConformance(result)
        };
        return new AssuranceEvaluation
        {
            Profile = profile,
            Claims = claims,
            Assessments = assessments,
            EvidenceSummary = evidenceSummary,
            CertificateEligibility = EvaluateCertificate(result, profile, context, assessments, evidenceSummary)
        };
    }

    private static CoreConformanceAssessment ReduceCore(IReadOnlyList<AssuranceClaim> claims)
    {
        var required = claims.Where(claim =>
            claim.Applicability == AssuranceApplicability.Applicable &&
            claim.RequirementLevel == AssuranceRequirementLevel.Must &&
            claim.Dimension is AssuranceDimension.CoreProtocol or AssuranceDimension.CapabilityContract or AssuranceDimension.ExternalConformance &&
            claim.Authority is AssuranceAuthority.OfficialSpecification or AssuranceAuthority.OfficialConformance).ToArray();
        var violated = required.Where(claim => claim.Outcome is AssuranceClaimOutcome.Violated or AssuranceClaimOutcome.Error).Select(claim => claim.ClaimId).ToArray();
        var incomplete = required.Where(claim => claim.Outcome is AssuranceClaimOutcome.Incomplete or AssuranceClaimOutcome.ReviewRequired).Select(claim => claim.ClaimId).ToArray();
        var status = violated.Length > 0
            ? CoreConformanceStatus.Nonconformant
            : required.Length == 0 || incomplete.Length > 0
                ? CoreConformanceStatus.Incomplete
                : CoreConformanceStatus.Conformant;
        return new CoreConformanceAssessment
        {
            Status = status,
            RequiredClaims = required.Length,
            SatisfiedRequiredClaims = required.Count(claim => claim.Outcome == AssuranceClaimOutcome.Satisfied),
            BlockingClaimIds = violated,
            IncompleteClaimIds = incomplete
        };
    }

    private static SecurityAssuranceAssessment ReduceSecurity(IReadOnlyList<AssuranceClaim> claims, ValidationResult result)
    {
        var securityClaims = claims.Where(claim => claim.Dimension == AssuranceDimension.Security).ToArray();
        var deterministic = securityClaims.Where(claim =>
            claim.Authority is AssuranceAuthority.OfficialSpecification or AssuranceAuthority.DeterministicSecurity).ToArray();
        var issues = deterministic.Where(claim => claim.Outcome is AssuranceClaimOutcome.Violated or AssuranceClaimOutcome.Error).Select(claim => claim.ClaimId).ToArray();
        var incomplete = deterministic
            .Where(claim => claim.Outcome is AssuranceClaimOutcome.Incomplete or AssuranceClaimOutcome.ReviewRequired)
            .Select(claim => claim.ClaimId)
            .ToArray();
        var securityCoverageIncomplete = result.Evidence.Coverage
            .Where(ValidationEvidenceLaneClassifier.IsBaseline)
            .Any(coverage => coverage.LayerId.Contains("security", StringComparison.OrdinalIgnoreCase) && ValidationEvidenceSummarizer.IsEvidenceDebt(coverage));
        var status = issues.Length > 0
            ? SecurityAssuranceStatus.IssuesFound
            : deterministic.Length == 0 || incomplete.Length > 0 || securityCoverageIncomplete
                ? SecurityAssuranceStatus.Incomplete
                : SecurityAssuranceStatus.Verified;
        return new SecurityAssuranceAssessment
        {
            Status = status,
            DeterministicControlClaims = deterministic.Length,
            ReviewClaimCount = securityClaims.Count(claim => claim.Outcome == AssuranceClaimOutcome.ReviewRequired),
            IssueClaimIds = issues,
            IncompleteClaimIds = incomplete
        };
    }

    private static OperationalReadinessAssessment ReduceOperations(IReadOnlyList<AssuranceClaim> claims, ValidationResult result)
    {
        var objectives = claims.Where(claim =>
            claim.Dimension == AssuranceDimension.Operations &&
            claim.RequirementLevel == AssuranceRequirementLevel.OperationalObjective).ToArray();
        var failed = objectives.Where(claim => claim.Outcome is AssuranceClaimOutcome.Violated or AssuranceClaimOutcome.Error).Select(claim => claim.ClaimId).ToArray();
        var incomplete = objectives.Where(claim => claim.Outcome is AssuranceClaimOutcome.Incomplete or AssuranceClaimOutcome.ReviewRequired).Select(claim => claim.ClaimId).ToArray();
        var status = failed.Length > 0
            ? OperationalReadinessStatus.NotReady
            : objectives.Length == 0 || incomplete.Length > 0
                ? OperationalReadinessStatus.Incomplete
                : OperationalReadinessStatus.Ready;
        return new OperationalReadinessAssessment
        {
            Status = status,
            ObjectiveClaims = objectives.Length,
            FailedClaimIds = failed,
            IncompleteClaimIds = incomplete
        };
    }

    private static IReadOnlyList<ExtensionAssuranceAssessment> ReduceExtensions(
        IReadOnlyList<AssuranceClaim> claims,
        ValidationResult result) => result.Assessments.Extensions
        .OrderBy(extension => extension.ExtensionId, StringComparer.Ordinal)
        .Select(extension =>
        {
            var extensionClaims = claims.Where(claim =>
                claim.Dimension == AssuranceDimension.Extension &&
                string.Equals(claim.Subject, extension.ExtensionId, StringComparison.Ordinal)).ToArray();
            var nonSuccess = extensionClaims.Where(claim =>
                claim.Outcome is not (AssuranceClaimOutcome.Satisfied or AssuranceClaimOutcome.NotApplicable)).ToArray();
            var status = nonSuccess.Any(claim => claim.Outcome is AssuranceClaimOutcome.Violated or AssuranceClaimOutcome.Error)
                ? ExtensionReadinessStatus.IssuesFound
                : nonSuccess.Length > 0 || extensionClaims.Length == 0
                    ? ExtensionReadinessStatus.Incomplete
                    : ExtensionReadinessStatus.Ready;
            return new ExtensionAssuranceAssessment
            {
                ExtensionId = extension.ExtensionId,
                Stage = extension.Stage,
                SpecificationRevision = extension.SpecificationRevision,
                SpecificationSha256 = extension.SpecificationSha256,
                Status = status,
                NonSuccessClaimIds = nonSuccess.Select(claim => claim.ClaimId).Order(StringComparer.Ordinal).ToArray()
            };
        })
        .ToArray();

    private static IReadOnlyList<ClientCompatibilityAssuranceAssessment> ReduceClientCompatibility(ValidationResult result) =>
        (result.ClientCompatibility?.Assessments ?? [])
            .OrderBy(assessment => assessment.ProfileId, StringComparer.Ordinal)
            .Select(assessment => new ClientCompatibilityAssuranceAssessment
            {
                ProfileId = assessment.ProfileId,
                Revision = assessment.Revision,
                Status = assessment.Status,
                Verification = ClientCompatibilityVerification.Interpreted,
                Summary = assessment.Summary
            })
            .ToArray();

    private static ExternalConformanceAssuranceAssessment ReduceExternalConformance(ValidationResult result)
    {
        var assessment = result.Assessments.OfficialConformance;
        if (assessment == null) return new ExternalConformanceAssuranceAssessment();
        var status = assessment.VerificationStatus switch
        {
            OfficialConformanceVerificationStatus.Conflict => ExternalConformanceStatus.Conflict,
            OfficialConformanceVerificationStatus.Incomplete or OfficialConformanceVerificationStatus.Unverified => ExternalConformanceStatus.Incomplete,
            OfficialConformanceVerificationStatus.Verified when assessment.Failed > 0 => ExternalConformanceStatus.Nonconformant,
            OfficialConformanceVerificationStatus.Verified => ExternalConformanceStatus.Verified,
            _ => ExternalConformanceStatus.Incomplete
        };
        return new ExternalConformanceAssuranceAssessment
        {
            Status = status,
            RequirementSetRevision = assessment.RequirementSet.Revision,
            RequirementSetSha256 = assessment.RequirementSet.Sha256,
            CreditedPassed = assessment.CreditedPassed,
            Failed = assessment.Failed,
            Skipped = assessment.Skipped,
            ConflictCount = assessment.Conflicts.Count
        };
    }

    private static CertificateEligibilityAssessment EvaluateCertificate(
        ValidationResult result,
        AssuranceProfileManifest profile,
        AssuranceEvaluationContext context,
        AssuranceAssessmentSet assessments,
        EvidenceCoverageSummary evidenceSummary)
    {
        var reasons = new List<string>();
        if (!profile.CertificateEligible) reasons.Add($"Assurance profile '{profile.ProfileId}' does not issue provider certificates.");
        if (context.ExecutionMode < profile.MinimumExecutionMode) reasons.Add($"Execution mode {context.ExecutionMode} is below required mode {profile.MinimumExecutionMode}.");
        if (assessments.CoreConformance.Status != CoreConformanceStatus.Conformant) reasons.Add($"Core conformance is {assessments.CoreConformance.Status}.");
        if (assessments.SecurityAssurance.Status != SecurityAssuranceStatus.Verified) reasons.Add($"Security assurance is {assessments.SecurityAssurance.Status}.");
        if (assessments.OperationalReadiness.Status != OperationalReadinessStatus.Ready) reasons.Add($"Operational readiness is {assessments.OperationalReadiness.Status}.");
        if (evidenceSummary.EvidenceCoverageRatio < profile.MinimumEvidenceCoverageRatio) reasons.Add($"Evidence coverage {evidenceSummary.EvidenceCoverageRatio:P1} is below {profile.MinimumEvidenceCoverageRatio:P1}.");
        if (evidenceSummary.EvidenceConfidenceRatio < profile.MinimumEvidenceConfidenceRatio) reasons.Add($"Evidence confidence {evidenceSummary.EvidenceConfidenceRatio:P1} is below {profile.MinimumEvidenceConfidenceRatio:P1}.");
        if (profile.RequiresOfficialConformance && result.Assessments.OfficialConformance?.VerificationStatus != OfficialConformanceVerificationStatus.Verified)
            reasons.Add("Verified official conformance evidence is required.");
        if (result.Assessments.OfficialConformance?.VerificationStatus == OfficialConformanceVerificationStatus.Conflict ||
            result.Assessments.OfficialConformance?.Conflicts.Count > 0)
            reasons.Add("Official and native evidence conflicts remain unresolved.");
        var requiredExtensionFailure = false;
        foreach (var extensionId in context.RequiredExtensionIds.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            var extension = assessments.ExtensionReadiness.FirstOrDefault(candidate =>
                string.Equals(candidate.ExtensionId, extensionId, StringComparison.Ordinal));
            if (extension == null)
            {
                reasons.Add($"Required extension '{extensionId}' was not assessed.");
            }
            else if (extension.Status == ExtensionReadinessStatus.IssuesFound)
            {
                reasons.Add($"Required extension '{extensionId}' was rejected.");
                requiredExtensionFailure = true;
            }
            else if (extension.Status != ExtensionReadinessStatus.Ready)
            {
                reasons.Add($"Required extension '{extensionId}' is {extension.Status}.");
            }
        }
        if (context.ActiveWaiverCount > 0)
            reasons.Add($"Provider certification does not permit {context.ActiveWaiverCount} applied waiver(s).");
        if (context.InvalidWaiverCount > 0)
            reasons.Add($"Provider certification found {context.InvalidWaiverCount} expired or invalid waiver(s).");
        if (profile.RequiresSignedAttestation && !context.HasSignedAttestation) reasons.Add("A verified signed attestation is required.");
        if (profile.RequiresRepeatedRuns && context.ProviderHarness?.Status != ProviderHarnessStatus.Verified)
            reasons.Add("A verified provider harness receipt with target-bound authorization is required.");
        var repeatedRunConsistency = context.ProviderHarness?.RepeatedRunConsistency ?? context.RepeatedRunConsistency;
        if (profile.RequiresRepeatedRuns && (!repeatedRunConsistency.HasValue || repeatedRunConsistency < profile.MinimumRepeatedRunConsistency))
            reasons.Add($"Repeated-run consistency of at least {profile.MinimumRepeatedRunConsistency:P1} is required.");

        var deterministicFailure = assessments.CoreConformance.Status == CoreConformanceStatus.Nonconformant ||
            assessments.SecurityAssurance.Status == SecurityAssuranceStatus.IssuesFound ||
            assessments.OperationalReadiness.Status == OperationalReadinessStatus.NotReady ||
            requiredExtensionFailure;
        var integrityFailure = result.Assessments.OfficialConformance?.VerificationStatus == OfficialConformanceVerificationStatus.Conflict ||
            result.Assessments.OfficialConformance?.Conflicts.Count > 0 ||
            context.ProviderHarness?.Status == ProviderHarnessStatus.Invalid ||
            context.InvalidWaiverCount > 0;
        var status = !profile.CertificateEligible || deterministicFailure || integrityFailure
            ? CertificateEligibilityStatus.Ineligible
            : reasons.Count > 0
                ? CertificateEligibilityStatus.ReviewRequired
                : CertificateEligibilityStatus.Eligible;
        return new CertificateEligibilityAssessment
        {
            Status = status,
            ProfileId = profile.ProfileId,
            Reasons = reasons,
            EvidenceCoverageRatio = evidenceSummary.EvidenceCoverageRatio,
            EvidenceConfidenceRatio = evidenceSummary.EvidenceConfidenceRatio
        };
    }
}