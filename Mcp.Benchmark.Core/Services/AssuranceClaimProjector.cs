using Mcp.Benchmark.Core.Constants;
using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.Core.Services;

public static class AssuranceClaimProjector
{
    public static IReadOnlyList<AssuranceClaim> Project(ValidationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var claims = new List<AssuranceClaim>();
        ProjectProtocolViolations(result, claims);
        ProjectStructuredFindings(result, claims);
        ProjectSecurityVulnerabilities(result, claims);
        ProjectAttackOutcomes(result, claims);
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

    private static void ProjectProtocolViolations(ValidationResult result, ICollection<AssuranceClaim> claims)
    {
        foreach (var violation in result.ProtocolCompliance?.Violations ?? [])
        {
            var claimId = string.IsNullOrWhiteSpace(violation.CheckId)
                ? $"protocol:{Normalize(violation.Category)}:{Normalize(violation.Rule ?? violation.Description)}"
                : $"protocol:{Normalize(violation.CheckId)}";
            claims.Add(new AssuranceClaim
            {
                ClaimId = claimId,
                Subject = string.IsNullOrWhiteSpace(violation.Category) ? "protocol" : violation.Category,
                Dimension = AssuranceDimension.CoreProtocol,
                RequirementLevel = AssuranceRequirementLevel.Must,
                Authority = AssuranceAuthority.OfficialSpecification,
                Source = new AssuranceSourceReference
                {
                    Uri = violation.SpecReference,
                    Revision = result.ProtocolVersion ?? "unknown"
                },
                Outcome = AssuranceClaimOutcome.Violated,
                Confidence = EvidenceConfidenceLevel.High,
                EvidenceIds = string.IsNullOrWhiteSpace(violation.CheckId) ? [] : [$"protocol/{violation.CheckId}"],
                Summary = violation.Description,
                Remediation = violation.Recommendation,
                Metadata = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["ruleId"] = string.IsNullOrWhiteSpace(violation.CheckId) ? claimId : violation.CheckId,
                    ["severity"] = violation.Severity.ToString()
                }
            });
        }
    }

    private static void ProjectStructuredFindings(ValidationResult result, ICollection<AssuranceClaim> claims)
    {
        AddFindings(result.ProtocolCompliance?.Findings, AssuranceDimension.CoreProtocol);
        AddFindings(result.ToolValidation?.Findings, AssuranceDimension.CapabilityContract);
        AddFindings(result.ToolValidation?.AiReadinessFindings, AssuranceDimension.CapabilityContract);
        AddFindings(result.ToolValidation?.AuthenticationSecurity?.StructuredFindings, AssuranceDimension.Security);
        AddFindings(result.ToolValidation?.ToolResults.SelectMany(tool => tool.Findings), AssuranceDimension.CapabilityContract);
        AddFindings(result.ResourceTesting?.Findings, AssuranceDimension.CapabilityContract);
        AddFindings(result.ResourceTesting?.ResourceResults.SelectMany(resource => resource.Findings), AssuranceDimension.CapabilityContract);
        AddFindings(result.PromptTesting?.Findings, AssuranceDimension.CapabilityContract);
        AddFindings(result.PromptTesting?.PromptResults.SelectMany(prompt => prompt.Findings), AssuranceDimension.CapabilityContract);
        AddFindings(result.SecurityTesting?.Findings, AssuranceDimension.Security);
        AddFindings(result.PerformanceTesting?.Findings, AssuranceDimension.Operations);
        AddFindings(result.ErrorHandling?.Findings, AssuranceDimension.Operations);

        void AddFindings(IEnumerable<ValidationFinding>? findings, AssuranceDimension defaultDimension)
        {
            foreach (var finding in findings ?? [])
            {
                var authority = finding.EffectiveSource switch
                {
                    ValidationRuleSource.Spec => AssuranceAuthority.OfficialSpecification,
                    ValidationRuleSource.Heuristic => AssuranceAuthority.Heuristic,
                    _ => AssuranceAuthority.ProviderPolicy
                };
                var dimension = ResolveFindingDimension(finding, defaultDimension);
                var requirement = ResolveFindingRequirement(finding, authority, dimension);
                var outcome = ResolveFindingOutcome(finding, authority);
                var ruleId = string.IsNullOrWhiteSpace(finding.RuleId)
                    ? $"unidentified:{Normalize(finding.Category)}:{Normalize(finding.Component)}"
                    : finding.RuleId;
                var metadata = new Dictionary<string, string>(finding.Metadata, StringComparer.Ordinal)
                {
                    ["ruleId"] = ruleId,
                    ["severity"] = finding.Severity.ToString()
                };
                claims.Add(new AssuranceClaim
                {
                    ClaimId = $"finding:{Normalize(ruleId)}:{Normalize(finding.Component)}",
                    Subject = string.IsNullOrWhiteSpace(finding.Component) ? defaultDimension.ToString() : finding.Component,
                    Dimension = dimension,
                    RequirementLevel = requirement,
                    Authority = authority,
                    Source = new AssuranceSourceReference
                    {
                        Uri = finding.EffectiveSpecReference,
                        Revision = finding.Metadata.TryGetValue("ruleRevision", out var revision)
                            ? revision
                            : ValidationFindingRuleIds.CatalogVersion
                    },
                    Outcome = outcome,
                    Confidence = authority is AssuranceAuthority.OfficialSpecification
                        ? EvidenceConfidenceLevel.High
                        : EvidenceConfidenceLevel.Medium,
                    EvidenceIds = finding.Metadata.TryGetValue("evidenceId", out var evidenceId) ? [evidenceId] : [],
                    Summary = finding.Summary,
                    Remediation = finding.Recommendation,
                    Metadata = metadata
                });
            }
        }
    }

    private static void ProjectSecurityVulnerabilities(ValidationResult result, ICollection<AssuranceClaim> claims)
    {
        foreach (var vulnerability in result.SecurityTesting?.Vulnerabilities ?? [])
        {
            var ruleId = string.IsNullOrWhiteSpace(vulnerability.Id)
                ? $"security:{Normalize(vulnerability.Category)}:{Normalize(vulnerability.Name)}"
                : vulnerability.Id;
            var deterministic = vulnerability.IsExploitable;
            claims.Add(new AssuranceClaim
            {
                ClaimId = $"vulnerability:{Normalize(ruleId)}:{Normalize(vulnerability.AffectedComponent)}",
                Subject = string.IsNullOrWhiteSpace(vulnerability.AffectedComponent) ? "security" : vulnerability.AffectedComponent,
                Dimension = AssuranceDimension.Security,
                RequirementLevel = AssuranceRequirementLevel.SecurityControl,
                Authority = deterministic ? AssuranceAuthority.DeterministicSecurity : AssuranceAuthority.Heuristic,
                Source = new AssuranceSourceReference { Revision = ValidationFindingRuleIds.CatalogVersion },
                Outcome = deterministic ? AssuranceClaimOutcome.Violated : AssuranceClaimOutcome.ReviewRequired,
                Confidence = deterministic ? EvidenceConfidenceLevel.High : EvidenceConfidenceLevel.Medium,
                EvidenceIds = vulnerability.ProbeContexts?.Where(context => !string.IsNullOrWhiteSpace(context.ProbeId)).Select(context => context.ProbeId).ToArray() ?? [],
                Summary = vulnerability.Description,
                Remediation = vulnerability.Remediation,
                Metadata = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["ruleId"] = ruleId,
                    ["severity"] = vulnerability.Severity.ToString(),
                    ["exploitable"] = vulnerability.IsExploitable.ToString()
                }
            });
        }
    }

    private static void ProjectAttackOutcomes(ValidationResult result, ICollection<AssuranceClaim> claims)
    {
        foreach (var attack in result.SecurityTesting?.AttackSimulations ?? [])
        {
            var authority = attack.Authority == ValidationRuleSource.Spec
                ? AssuranceAuthority.OfficialSpecification
                : attack.Authority == ValidationRuleSource.Heuristic
                    ? AssuranceAuthority.Heuristic
                    : AssuranceAuthority.DeterministicSecurity;
            if (attack.Outcome == ValidationOutcome.Succeeded && authority == AssuranceAuthority.Heuristic) continue;
            claims.Add(new AssuranceClaim
            {
                ClaimId = $"attack:{Normalize(attack.AttackVector)}",
                Subject = attack.AttackVector,
                Dimension = AssuranceDimension.Security,
                RequirementLevel = AssuranceRequirementLevel.SecurityControl,
                Authority = authority,
                Source = new AssuranceSourceReference { Revision = ValidationFindingRuleIds.CatalogVersion },
                Outcome = attack.Outcome switch
                {
                    ValidationOutcome.Succeeded => AssuranceClaimOutcome.Satisfied,
                    ValidationOutcome.Failed when authority != AssuranceAuthority.Heuristic => AssuranceClaimOutcome.Violated,
                    ValidationOutcome.Error => AssuranceClaimOutcome.Error,
                    ValidationOutcome.Inconclusive or ValidationOutcome.Unavailable or ValidationOutcome.Blocked => AssuranceClaimOutcome.Incomplete,
                    _ => AssuranceClaimOutcome.ReviewRequired
                },
                Confidence = authority == AssuranceAuthority.Heuristic ? EvidenceConfidenceLevel.Medium : EvidenceConfidenceLevel.High,
                EvidenceIds = attack.ProbeContexts?.Where(context => !string.IsNullOrWhiteSpace(context.ProbeId)).Select(context => context.ProbeId).ToArray() ?? [],
                Summary = attack.Description
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
                RequirementLevel = DimensionForLayer(coverage.LayerId, coverage.Lane) switch
                {
                    AssuranceDimension.Operations => AssuranceRequirementLevel.OperationalObjective,
                    AssuranceDimension.Security => AssuranceRequirementLevel.SecurityControl,
                    _ => AssuranceRequirementLevel.Policy
                },
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
            var hasSpecFailure = extension.Findings.Any(finding =>
                finding.Source == ValidationRuleSource.Spec &&
                finding.Severity >= ValidationFindingSeverity.High);
            var hasReviewFinding = extension.Findings.Count > 0;
            var evidenceIncomplete = extension.EvidenceSummary.ApplicableDeclarations > 0 &&
                extension.EvidenceSummary.Covered < extension.EvidenceSummary.ApplicableDeclarations;
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
                Outcome = hasSpecFailure
                    ? AssuranceClaimOutcome.Violated
                    : evidenceIncomplete
                        ? AssuranceClaimOutcome.Incomplete
                        : hasReviewFinding
                            ? AssuranceClaimOutcome.ReviewRequired
                            : AssuranceClaimOutcome.Satisfied,
                Confidence = extension.EvidenceSummary.ConfidenceLevel,
                Summary = hasSpecFailure
                    ? "Extension has specification failures."
                    : evidenceIncomplete
                        ? "Extension evidence is incomplete."
                        : hasReviewFinding
                            ? "Extension findings require review."
                            : "Extension evidence satisfies the assessed server requirements."
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
                Source = new AssuranceSourceReference { Uri = profile.DocumentationUrl, Revision = profile.Revision },
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

    private static AssuranceDimension ResolveFindingDimension(
        ValidationFinding finding,
        AssuranceDimension defaultDimension)
    {
        if (finding.ImpactAreas?.Contains(ImpactArea.OperationalResilience) == true) return AssuranceDimension.Operations;
        if (finding.ImpactAreas?.Any(area => area is ImpactArea.AuthenticationBoundary or ImpactArea.DataExposure or ImpactArea.UnsafeAutonomy) == true)
            return AssuranceDimension.Security;
        if (finding.ImpactAreas?.Contains(ImpactArea.ProtocolInteroperability) == true) return AssuranceDimension.CoreProtocol;
        if (finding.ImpactAreas?.Contains(ImpactArea.CapabilityContract) == true) return AssuranceDimension.CapabilityContract;
        if (finding.Category.Contains("security", StringComparison.OrdinalIgnoreCase) ||
            finding.Category.Contains("auth", StringComparison.OrdinalIgnoreCase))
            return AssuranceDimension.Security;
        return defaultDimension;
    }

    private static AssuranceRequirementLevel ResolveFindingRequirement(
        ValidationFinding finding,
        AssuranceAuthority authority,
        AssuranceDimension dimension)
    {
        if (finding.Metadata.TryGetValue("requirementLevel", out var requirementLevel) &&
            Enum.TryParse<AssuranceRequirementLevel>(requirementLevel, ignoreCase: true, out var parsed))
            return parsed;
        if (dimension == AssuranceDimension.Security) return AssuranceRequirementLevel.SecurityControl;
        if (dimension == AssuranceDimension.Operations) return AssuranceRequirementLevel.OperationalObjective;
        if (authority == AssuranceAuthority.OfficialSpecification)
            return finding.GateOverride == GateOutcome.Reject || finding.Severity >= ValidationFindingSeverity.High
                ? AssuranceRequirementLevel.Must
                : AssuranceRequirementLevel.Should;
        return AssuranceRequirementLevel.Policy;
    }

    private static AssuranceClaimOutcome ResolveFindingOutcome(
        ValidationFinding finding,
        AssuranceAuthority authority)
    {
        if (finding.GateOverride == GateOutcome.CoverageDebt) return AssuranceClaimOutcome.Incomplete;
        if (authority is AssuranceAuthority.Heuristic or AssuranceAuthority.ProviderPolicy)
            return AssuranceClaimOutcome.ReviewRequired;
        if (finding.GateOverride == GateOutcome.Reject || finding.Severity >= ValidationFindingSeverity.High)
            return AssuranceClaimOutcome.Violated;
        return AssuranceClaimOutcome.ReviewRequired;
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "unknown";
        return new string(value.Trim().ToLowerInvariant().Select(character => char.IsLetterOrDigit(character) ? character : '-').ToArray()).Trim('-');
    }
}