using Mcp.Benchmark.Core.Constants;
using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.Core.Services;

public static class AssuranceClaimProjector
{
    public static IReadOnlyList<AssuranceClaim> Project(ValidationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var claims = new List<AssuranceClaim>();
        ProjectTierChecks(result, claims);
        ProjectDecisions(result, claims);
        ProjectCoverage(result, claims);
        ProjectExternalConformance(result, claims);
        ProjectExtensions(result, claims);
        ProjectClientCompatibility(result, claims);
        return claims
            .GroupBy(claim => claim.ClaimId, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(claim => claim.ClaimId, StringComparer.Ordinal)
            .ToArray();
    }

    private static void ProjectTierChecks(ValidationResult result, ICollection<AssuranceClaim> claims)
    {
        foreach (var check in result.TrustAssessment?.TierChecks ?? [])
        {
            var requirementLevel = ParseRequirementLevel(check.Tier);
            claims.Add(new AssuranceClaim
            {
                ClaimId = $"core:tier:{Normalize(check.Tier)}:{Normalize(check.Component)}:{Normalize(check.Requirement)}",
                Subject = string.IsNullOrWhiteSpace(check.Component) ? "protocol" : check.Component,
                Dimension = AssuranceDimension.CoreProtocol,
                RequirementLevel = requirementLevel,
                Authority = AssuranceAuthority.OfficialSpecification,
                Source = new AssuranceSourceReference
                {
                    Revision = result.ProtocolVersion ?? "unknown"
                },
                Outcome = check.Passed ? AssuranceClaimOutcome.Satisfied : AssuranceClaimOutcome.Violated,
                Confidence = EvidenceConfidenceLevel.High,
                Summary = check.Requirement,
                Remediation = check.Detail
            });
        }
    }

    private static void ProjectDecisions(ValidationResult result, ICollection<AssuranceClaim> claims)
    {
        var decisions = (result.VerdictAssessment?.TriggeredDecisions ?? [])
            .Concat(result.VerdictAssessment?.CoverageDecisions ?? []);
        foreach (var decision in decisions)
        {
            var authority = ResolveAuthority(decision);
            claims.Add(new AssuranceClaim
            {
                ClaimId = $"decision:{decision.DecisionId}",
                Subject = decision.Component,
                Dimension = ResolveDimension(decision),
                RequirementLevel = ResolveRequirementLevel(decision, authority),
                Authority = authority,
                Source = new AssuranceSourceReference
                {
                    Uri = decision.SpecReference,
                    Revision = decision.RuleRevision
                },
                Outcome = ResolveOutcome(decision, authority),
                Confidence = ResolveConfidence(decision),
                EvidenceIds = decision.RelatedEvidenceIds,
                Summary = decision.Summary,
                Remediation = decision.EvidenceReferences.Select(reference => reference.Remediation).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)),
                Metadata = new Dictionary<string, string>(decision.Metadata, StringComparer.Ordinal)
            });
        }
    }

    private static void ProjectCoverage(ValidationResult result, ICollection<AssuranceClaim> claims)
    {
        foreach (var coverage in result.Evidence.Coverage)
        {
            claims.Add(new AssuranceClaim
            {
                ClaimId = $"coverage:{Normalize(coverage.LayerId)}:{Normalize(coverage.Scope)}",
                Subject = coverage.Scope,
                Dimension = DimensionForLayer(coverage.LayerId, coverage.Lane),
                RequirementLevel = AssuranceRequirementLevel.Policy,
                Authority = AssuranceAuthority.ProviderPolicy,
                Source = new AssuranceSourceReference { Revision = AssuranceProfileCatalog.Revision },
                Applicability = coverage.Status == ValidationCoverageStatus.NotApplicable
                    ? AssuranceApplicability.NotApplicable
                    : AssuranceApplicability.Applicable,
                ApplicabilityReason = coverage.Status == ValidationCoverageStatus.NotApplicable ? coverage.Reason : null,
                Outcome = CoverageOutcome(coverage),
                Confidence = coverage.Confidence,
                EvidenceIds = [ValidationEvidenceIdBuilder.ForCoverage(coverage)],
                Summary = coverage.Reason ?? $"Coverage outcome: {coverage.Outcome}."
            });
        }
    }

    private static void ProjectExternalConformance(ValidationResult result, ICollection<AssuranceClaim> claims)
    {
        var assessment = result.Assessments.OfficialConformance;
        if (assessment == null) return;

        foreach (var scenario in assessment.Scenarios)
        {
            foreach (var check in scenario.Checks)
            {
                claims.Add(new AssuranceClaim
                {
                    ClaimId = $"official:{Normalize(scenario.ScenarioId)}:{Normalize(check.Id)}",
                    Subject = scenario.ScenarioId,
                    Dimension = AssuranceDimension.ExternalConformance,
                    RequirementLevel = scenario.ScoringDisposition == OfficialConformanceScoringDisposition.Required
                        ? AssuranceRequirementLevel.Must
                        : AssuranceRequirementLevel.May,
                    Authority = AssuranceAuthority.OfficialConformance,
                    Source = new AssuranceSourceReference
                    {
                        Revision = assessment.RequirementSet.Revision,
                        Sha256 = assessment.RequirementSet.Sha256
                    },
                    Outcome = assessment.VerificationStatus != OfficialConformanceVerificationStatus.Verified
                        ? AssuranceClaimOutcome.Incomplete
                        : check.Status switch
                        {
                            OfficialConformanceCheckStatus.Success => AssuranceClaimOutcome.Satisfied,
                            OfficialConformanceCheckStatus.Failure => AssuranceClaimOutcome.Violated,
                            OfficialConformanceCheckStatus.Skipped => AssuranceClaimOutcome.Incomplete,
                            _ => AssuranceClaimOutcome.ReviewRequired
                        },
                    Confidence = assessment.VerificationStatus == OfficialConformanceVerificationStatus.Verified
                        ? EvidenceConfidenceLevel.High
                        : EvidenceConfidenceLevel.None,
                    EvidenceIds = [$"official-conformance/{scenario.ScenarioId}/{check.Id}"],
                    Summary = check.Description
                });
            }
        }

        foreach (var decision in assessment.Decisions)
        {
            claims.Add(new AssuranceClaim
            {
                ClaimId = $"official-decision:{Normalize(decision.DecisionId)}",
                Subject = decision.Component,
                Dimension = AssuranceDimension.ExternalConformance,
                RequirementLevel = AssuranceRequirementLevel.Must,
                Authority = AssuranceAuthority.OfficialConformance,
                Source = new AssuranceSourceReference
                {
                    Uri = decision.SpecReference,
                    Revision = assessment.RequirementSet.Revision,
                    Sha256 = assessment.RequirementSet.Sha256
                },
                Outcome = decision.Gate == GateOutcome.Reject
                    ? AssuranceClaimOutcome.Violated
                    : AssuranceClaimOutcome.ReviewRequired,
                Confidence = EvidenceConfidenceLevel.High,
                EvidenceIds = decision.RelatedEvidenceIds,
                Summary = decision.Summary,
                Metadata = new Dictionary<string, string>(decision.Metadata, StringComparer.Ordinal)
                {
                    ["category"] = decision.Category
                }
            });
        }
    }

    private static void ProjectExtensions(ValidationResult result, ICollection<AssuranceClaim> claims)
    {
        foreach (var extension in result.Assessments.Extensions)
        {
            claims.Add(new AssuranceClaim
            {
                ClaimId = $"extension:{Normalize(extension.ExtensionId)}:assessment",
                Subject = extension.ExtensionId,
                Dimension = AssuranceDimension.Extension,
                RequirementLevel = AssuranceRequirementLevel.Policy,
                Authority = AssuranceAuthority.ProviderPolicy,
                Source = new AssuranceSourceReference
                {
                    Revision = extension.SpecificationRevision,
                    Sha256 = extension.SpecificationSha256
                },
                Outcome = extension.Verdict switch
                {
                    ValidationVerdict.Trusted => AssuranceClaimOutcome.Satisfied,
                    ValidationVerdict.Reject => AssuranceClaimOutcome.Violated,
                    ValidationVerdict.ReviewRequired => AssuranceClaimOutcome.ReviewRequired,
                    _ => AssuranceClaimOutcome.Incomplete
                },
                Confidence = extension.EvidenceSummary.ConfidenceLevel,
                Summary = $"Extension readiness assessment is {extension.Verdict}."
            });
            foreach (var finding in extension.Findings)
            {
                claims.Add(new AssuranceClaim
                {
                    ClaimId = $"extension:{Normalize(extension.ExtensionId)}:{Normalize(finding.RuleId)}:{Normalize(finding.Component)}",
                    Subject = extension.ExtensionId,
                    Dimension = AssuranceDimension.Extension,
                    RequirementLevel = finding.Source == ValidationRuleSource.Spec
                        ? AssuranceRequirementLevel.Must
                        : AssuranceRequirementLevel.SecurityControl,
                    Authority = finding.Source == ValidationRuleSource.Spec
                        ? AssuranceAuthority.OfficialSpecification
                        : AssuranceAuthority.Heuristic,
                    Source = new AssuranceSourceReference
                    {
                        Uri = finding.SpecReference,
                        Revision = extension.SpecificationRevision,
                        Sha256 = extension.SpecificationSha256
                    },
                    Outcome = finding.Source == ValidationRuleSource.Spec && finding.Severity >= ValidationFindingSeverity.High
                        ? AssuranceClaimOutcome.Violated
                        : AssuranceClaimOutcome.ReviewRequired,
                    Confidence = finding.Source == ValidationRuleSource.Spec ? EvidenceConfidenceLevel.High : EvidenceConfidenceLevel.Medium,
                    Summary = finding.Summary,
                    Remediation = finding.Recommendation
                });
            }
        }
    }

    private static void ProjectClientCompatibility(ValidationResult result, ICollection<AssuranceClaim> claims)
    {
        foreach (var profile in result.ClientCompatibility?.Assessments ?? [])
        {
            claims.Add(new AssuranceClaim
            {
                ClaimId = $"client:{Normalize(profile.ProfileId)}",
                Subject = profile.ProfileId,
                Dimension = AssuranceDimension.ClientCompatibility,
                RequirementLevel = AssuranceRequirementLevel.Policy,
                Authority = AssuranceAuthority.ProviderPolicy,
                Source = new AssuranceSourceReference { Revision = "interpreted-v1" },
                Outcome = profile.Status switch
                {
                    ClientProfileCompatibilityStatus.Compatible => AssuranceClaimOutcome.Satisfied,
                    ClientProfileCompatibilityStatus.Incompatible => AssuranceClaimOutcome.Violated,
                    _ => AssuranceClaimOutcome.ReviewRequired
                },
                Confidence = EvidenceConfidenceLevel.Medium,
                Summary = profile.Summary
            });
        }
    }

    private static AssuranceAuthority ResolveAuthority(DecisionRecord decision)
    {
        if (decision.Origin == EvidenceOrigin.ExternalConformanceCheck) return AssuranceAuthority.OfficialConformance;
        if (decision.Authority == ValidationRuleSource.Spec) return AssuranceAuthority.OfficialSpecification;
        if (decision.Origin == EvidenceOrigin.ModelAssistance) return AssuranceAuthority.ModelAdvisory;
        if (decision.Origin == EvidenceOrigin.HeuristicInference) return AssuranceAuthority.Heuristic;
        if (decision.ImpactAreas.Any(area => area is ImpactArea.AuthenticationBoundary or ImpactArea.DataExposure or ImpactArea.UnsafeAutonomy))
            return AssuranceAuthority.DeterministicSecurity;
        if (decision.ImpactAreas.Contains(ImpactArea.OperationalResilience)) return AssuranceAuthority.OperationalMeasurement;
        return AssuranceAuthority.ProviderPolicy;
    }

    private static AssuranceDimension ResolveDimension(DecisionRecord decision)
    {
        if (decision.Lane == EvaluationLane.ExternalConformance) return AssuranceDimension.ExternalConformance;
        if (decision.Lane == EvaluationLane.ExtensionReadiness) return AssuranceDimension.Extension;
        if (decision.Lane == EvaluationLane.ClientCompatibility) return AssuranceDimension.ClientCompatibility;
        if (decision.ImpactAreas.Contains(ImpactArea.OperationalResilience)) return AssuranceDimension.Operations;
        if (decision.ImpactAreas.Any(area => area is ImpactArea.AuthenticationBoundary or ImpactArea.DataExposure or ImpactArea.UnsafeAutonomy))
            return AssuranceDimension.Security;
        if (decision.ImpactAreas.Contains(ImpactArea.CapabilityContract)) return AssuranceDimension.CapabilityContract;
        if (decision.ImpactAreas.Contains(ImpactArea.ProtocolInteroperability)) return AssuranceDimension.CoreProtocol;
        return decision.Category.Contains("Security", StringComparison.OrdinalIgnoreCase)
            ? AssuranceDimension.Security
            : AssuranceDimension.CoreProtocol;
    }

    private static AssuranceRequirementLevel ResolveRequirementLevel(DecisionRecord decision, AssuranceAuthority authority)
    {
        if (authority == AssuranceAuthority.OfficialSpecification)
            return decision.Gate == GateOutcome.Reject ? AssuranceRequirementLevel.Must : AssuranceRequirementLevel.Should;
        if (authority == AssuranceAuthority.DeterministicSecurity) return AssuranceRequirementLevel.SecurityControl;
        if (authority == AssuranceAuthority.OperationalMeasurement) return AssuranceRequirementLevel.OperationalObjective;
        return AssuranceRequirementLevel.Policy;
    }

    private static AssuranceClaimOutcome ResolveOutcome(DecisionRecord decision, AssuranceAuthority authority) => decision.Gate switch
    {
        GateOutcome.Reject when authority is AssuranceAuthority.OfficialSpecification or AssuranceAuthority.OfficialConformance or AssuranceAuthority.DeterministicSecurity or AssuranceAuthority.OperationalMeasurement
            => AssuranceClaimOutcome.Violated,
        GateOutcome.Reject => AssuranceClaimOutcome.ReviewRequired,
        GateOutcome.ReviewRequired => AssuranceClaimOutcome.ReviewRequired,
        GateOutcome.CoverageDebt => AssuranceClaimOutcome.Incomplete,
        _ => AssuranceClaimOutcome.Satisfied
    };

    private static EvidenceConfidenceLevel ResolveConfidence(DecisionRecord decision) => decision.Origin switch
    {
        EvidenceOrigin.DeterministicObservation or EvidenceOrigin.ExternalConformanceCheck => EvidenceConfidenceLevel.High,
        EvidenceOrigin.DeterministicAggregation => EvidenceConfidenceLevel.High,
        EvidenceOrigin.HeuristicInference => EvidenceConfidenceLevel.Medium,
        _ => EvidenceConfidenceLevel.Low
    };

    private static AssuranceClaimOutcome CoverageOutcome(ValidationCoverageDeclaration coverage) => coverage.Status switch
    {
        ValidationCoverageStatus.Covered => coverage.ObservedOutcome switch
        {
            ValidationOutcome.Succeeded => AssuranceClaimOutcome.Satisfied,
            ValidationOutcome.Failed => AssuranceClaimOutcome.Violated,
            ValidationOutcome.Error => AssuranceClaimOutcome.Error,
            _ => AssuranceClaimOutcome.Incomplete
        },
        ValidationCoverageStatus.NotApplicable => AssuranceClaimOutcome.NotApplicable,
        _ => AssuranceClaimOutcome.Incomplete
    };

    private static AssuranceDimension DimensionForLayer(string layerId, EvaluationLane? lane)
    {
        if (lane == EvaluationLane.ExtensionReadiness) return AssuranceDimension.Extension;
        if (lane == EvaluationLane.ClientCompatibility) return AssuranceDimension.ClientCompatibility;
        if (lane == EvaluationLane.ExternalConformance) return AssuranceDimension.ExternalConformance;
        if (layerId.Contains("security", StringComparison.OrdinalIgnoreCase)) return AssuranceDimension.Security;
        if (layerId.Contains("performance", StringComparison.OrdinalIgnoreCase) || layerId.Contains("error", StringComparison.OrdinalIgnoreCase))
            return AssuranceDimension.Operations;
        return AssuranceDimension.CoreProtocol;
    }

    private static AssuranceRequirementLevel ParseRequirementLevel(string? tier) => tier?.Trim().ToUpperInvariant() switch
    {
        "MUST" => AssuranceRequirementLevel.Must,
        "SHOULD" => AssuranceRequirementLevel.Should,
        _ => AssuranceRequirementLevel.May
    };

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "unknown";
        return new string(value.Trim().ToLowerInvariant().Select(character => char.IsLetterOrDigit(character) ? character : '-').ToArray()).Trim('-');
    }
}