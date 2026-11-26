using System.Globalization;
using FluentAssertions;
using Mcp.Benchmark.Core.Constants;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Services;

namespace Mcp.Benchmark.Tests.Unit.Services;

public sealed class AssuranceAssessmentEngineTests
{
    [Theory]
    [InlineData(true, false, false, AssuranceRequirementLevel.May)]
    [InlineData(true, true, false, AssuranceRequirementLevel.Policy)]
    [InlineData(false, false, false, AssuranceRequirementLevel.Policy)]
    [InlineData(true, false, true, AssuranceRequirementLevel.Policy)]
    public void Project_ExternalCoverage_ShouldPreserveVerifiedScoringDisposition(bool verified, bool required, bool unknownScope,
        AssuranceRequirementLevel expected)
    {
        var result = new ValidationResult();
        result.Assessments.OfficialConformance = CreateOfficialConformance(
            verified ? OfficialConformanceVerificationStatus.Verified : OfficialConformanceVerificationStatus.Unverified,
            [new OfficialConformanceScenarioAssessment
            {
                ScenarioId = "fixture", ScoringDisposition = required ? OfficialConformanceScoringDisposition.Required : OfficialConformanceScoringDisposition.NotScored,
                Checks = [new OfficialConformanceCheckAssessment
                {
                    Id = "check", SourceIndex = 0, Name = "Fixture", Description = "Observed failure", Status = OfficialConformanceCheckStatus.Failure
                }]
            }]);
        result.Evidence.Coverage.Add(new ValidationCoverageDeclaration
        {
            LayerId = "official-conformance",
            Scope = OfficialConformanceContract.GetCheckEvidenceId(unknownScope ? "unknown" : "fixture", "check", 0),
            Lane = EvaluationLane.ExternalConformance, Origin = EvidenceOrigin.ExternalConformanceCheck,
            Status = ValidationCoverageStatus.Covered, ObservedOutcome = ValidationOutcome.Failed
        });
        var claim = AssuranceClaimProjector.Project(result).Single(claim => claim.ClaimId.StartsWith("coverage:", StringComparison.Ordinal));
        claim.RequirementLevel.Should().Be(expected);
        claim.Outcome.Should().Be(AssuranceClaimOutcome.Violated);
        result.Evidence.Coverage.Single().ObservedOutcome.Should().Be(ValidationOutcome.Failed);
    }

    [Theory]
    [InlineData(OfficialConformanceCheckStatus.Success, AssuranceApplicability.Applicable, false, true, true, AssuranceClaimOutcome.Satisfied)]
    [InlineData(OfficialConformanceCheckStatus.Failure, AssuranceApplicability.Applicable, false, true, false, AssuranceClaimOutcome.Violated)]
    [InlineData(OfficialConformanceCheckStatus.Failure, AssuranceApplicability.NotApplicable, false, true, true, AssuranceClaimOutcome.Violated)]
    [InlineData(OfficialConformanceCheckStatus.Info, AssuranceApplicability.NotApplicable, false, true, true, AssuranceClaimOutcome.NotApplicable)]
    [InlineData(OfficialConformanceCheckStatus.Info, AssuranceApplicability.NotApplicable, true, true, true, AssuranceClaimOutcome.Incomplete)]
    [InlineData(OfficialConformanceCheckStatus.Warning, AssuranceApplicability.Applicable, true, true, true, AssuranceClaimOutcome.Incomplete)]
    [InlineData(OfficialConformanceCheckStatus.Skipped, AssuranceApplicability.Applicable, false, true, true, AssuranceClaimOutcome.Incomplete)]
    [InlineData(OfficialConformanceCheckStatus.Info, AssuranceApplicability.Applicable, false, true, true, AssuranceClaimOutcome.ReviewRequired)]
    [InlineData(OfficialConformanceCheckStatus.Success, AssuranceApplicability.Applicable, false, false, true, AssuranceClaimOutcome.Incomplete)]
    public void Project_OfficialCheck_ShouldRetainAuthorityAndApplicablePrerequisiteDebt(OfficialConformanceCheckStatus status,
        AssuranceApplicability applicability, bool missing, bool verified, bool required, AssuranceClaimOutcome expected)
    {
        var assessment = CreateOfficialConformance(verified ? OfficialConformanceVerificationStatus.Verified : OfficialConformanceVerificationStatus.Unverified,
        [new OfficialConformanceScenarioAssessment
        {
            ScenarioId = "owned.scenario", ScoringDisposition = required ? OfficialConformanceScoringDisposition.Required : OfficialConformanceScoringDisposition.NotScored,
            Checks = [new OfficialConformanceCheckAssessment { Id = "original-id", SourceIndex = 3, Name = "Original check", Description = "Recorded official observation",
                Status = status, Applicability = applicability, PrerequisiteMissing = missing }]
        }]);
        var result = new ValidationResult();
        result.Assessments.OfficialConformance = assessment;
        var claim = AssuranceClaimProjector.Project(result).Should().ContainSingle().Subject;
        claim.ClaimId.Should().Be("official:owned-scenario:check:3");
        claim.Subject.Should().Be("owned.scenario");
        claim.Dimension.Should().Be(AssuranceDimension.ExternalConformance);
        claim.RequirementLevel.Should().Be(required ? AssuranceRequirementLevel.Must : AssuranceRequirementLevel.May);
        claim.Authority.Should().Be(AssuranceAuthority.OfficialConformance);
        claim.Outcome.Should().Be(expected);
        claim.Applicability.Should().Be(status == OfficialConformanceCheckStatus.Info && !missing ? applicability : AssuranceApplicability.Applicable);
        claim.Confidence.Should().Be(verified ? EvidenceConfidenceLevel.High : EvidenceConfidenceLevel.None);
        claim.Source.Revision.Should().Be(assessment.RequirementSet.Revision);
        claim.Source.Sha256.Should().Be(assessment.RequirementSet.Sha256);
        claim.EvidenceIds.Should().Equal("official-conformance/owned.scenario/check/3");
        claim.Summary.Should().Be("Recorded official observation");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Project_OfficialChecks_ShouldRejectAmbiguousInMemoryIdentities(bool indexed)
    {
        var result = new ValidationResult();
        result.Assessments.OfficialConformance = CreateOfficialConformance(OfficialConformanceVerificationStatus.Verified,
        [new OfficialConformanceScenarioAssessment
        {
            ScenarioId = "required", Checks =
            [
                new() { Id = "rule-a", SourceIndex = indexed ? 0 : null, Name = "First", Description = "Success", Status = OfficialConformanceCheckStatus.Success },
                new() { Id = "rule_a", SourceIndex = indexed ? 0 : null, Name = "Second", Description = "Failure", Status = OfficialConformanceCheckStatus.Failure }
            ]
        }]);
        var project = () => AssuranceClaimProjector.Project(result);
        project.Should().Throw<System.Text.Json.JsonException>();
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void Project_OfficialDecision_ShouldRetainConflictProvenance(bool verified, bool reject)
    {
        var assessment = CreateOfficialConformance(verified ? OfficialConformanceVerificationStatus.Verified : OfficialConformanceVerificationStatus.Unverified);
        assessment.Decisions.Add(new DecisionRecord
        {
            DecisionId = "conflict.1", RuleId = "original-rule", RuleRevision = "decision-revision", Component = "owned-scenario",
            Lane = EvaluationLane.ExternalConformance, Authority = ValidationRuleSource.Spec, Origin = EvidenceOrigin.ExternalConformanceCheck,
            Gate = reject ? GateOutcome.Reject : GateOutcome.ReviewRequired, Severity = ValidationFindingSeverity.High,
            Category = "EvidenceConflict", Summary = "Conflicting observed evidence", SpecReference = "https://example.test/official-spec",
            RelatedEvidenceIds = ["native-observation", "official-observation"], Metadata = new() { ["nativeClaimId"] = "native-claim" }
        });
        var result = new ValidationResult();
        result.Assessments.OfficialConformance = assessment;
        var claim = AssuranceClaimProjector.Project(result).Single(claim => claim.ClaimId == "official-decision:conflict-1");
        claim.Subject.Should().Be("owned-scenario");
        claim.Dimension.Should().Be(AssuranceDimension.ExternalConformance);
        claim.RequirementLevel.Should().Be(AssuranceRequirementLevel.Must);
        claim.Authority.Should().Be(AssuranceAuthority.OfficialConformance);
        claim.Outcome.Should().Be(reject ? AssuranceClaimOutcome.Violated : AssuranceClaimOutcome.ReviewRequired);
        claim.Confidence.Should().Be(verified ? EvidenceConfidenceLevel.High : EvidenceConfidenceLevel.None);
        claim.Source.Uri.Should().Be("https://example.test/official-spec");
        claim.Source.Revision.Should().Be(assessment.RequirementSet.Revision);
        claim.Source.Sha256.Should().Be(assessment.RequirementSet.Sha256);
        claim.EvidenceIds.Should().Equal("native-observation", "official-observation");
        claim.Summary.Should().Be("Conflicting observed evidence");
        claim.Metadata.Should().BeEquivalentTo(new Dictionary<string, string> { ["nativeClaimId"] = "native-claim", ["category"] = "EvidenceConflict" });
    }

    [Theory]
    [InlineData("clean", AssuranceClaimOutcome.Satisfied, "satisfies")]
    [InlineData("review", AssuranceClaimOutcome.ReviewRequired, "review")]
    [InlineData("spec-failure", AssuranceClaimOutcome.Violated, "failures")]
    [InlineData("coverage-failure", AssuranceClaimOutcome.Violated, "failures")]
    [InlineData("coverage-error", AssuranceClaimOutcome.Error, "failures")]
    [InlineData("mixed-debt", AssuranceClaimOutcome.Incomplete, "incomplete")]
    [InlineData("summary-debt", AssuranceClaimOutcome.Incomplete, "incomplete")]
    [InlineData("not-applicable", AssuranceClaimOutcome.Satisfied, "satisfies")]
    [InlineData("empty", AssuranceClaimOutcome.Incomplete, "incomplete")]
    public void Project_ExtensionAssessment_ShouldKeepFailureDebtAndProvenance(string variant, AssuranceClaimOutcome expected, string explanation)
    {
        var outcome = variant switch
        {
            "coverage-failure" => ValidationOutcome.Failed, "coverage-error" => ValidationOutcome.Error,
            "not-applicable" => ValidationOutcome.NotApplicable, _ => ValidationOutcome.Succeeded
        };
        var coverage = new List<ValidationCoverageDeclaration>();
        if (variant != "empty") coverage.Add(ValidationCoverageFactory.FromOutcome("extension", "first", outcome));
        if (variant == "mixed-debt") coverage.Add(ValidationCoverageFactory.FromOutcome("extension", "second", ValidationOutcome.Unavailable));
        var summary = ValidationEvidenceSummarizer.Summarize(coverage);
        if (variant == "summary-debt") summary = new EvidenceCoverageSummary { ApplicableDeclarations = 2, Covered = 1, ConfidenceLevel = EvidenceConfidenceLevel.Medium };
        var result = new ValidationResult();
        result.Assessments.Extensions.Add(new ExtensionAssessment
        {
            ExtensionId = "example.test/extension", Stage = ExtensionLifecycleStage.Beta,
            SpecificationRevision = "extension-revision", SpecificationSha256 = new string('a', 64),
            Coverage = coverage, EvidenceSummary = summary,
            Findings = variant is "review" or "spec-failure" ? [new ValidationFinding
            {
                RuleId = "extension-rule", Component = "owned-tool", Source = variant == "review" ? ValidationRuleSource.Heuristic : ValidationRuleSource.Spec,
                Severity = ValidationFindingSeverity.High, Summary = "Observed extension issue", Recommendation = "Repair extension control", SpecReference = "https://example.test/extension-spec"
            }] : []
        });
        var claims = AssuranceClaimProjector.Project(result);
        var assessment = claims.Single(claim => claim.ClaimId.EndsWith(":assessment", StringComparison.Ordinal));
        assessment.ClaimId.Should().Be("extension:example-test-extension:assessment");
        assessment.Subject.Should().Be("example.test/extension");
        assessment.Dimension.Should().Be(AssuranceDimension.Extension);
        assessment.RequirementLevel.Should().Be(AssuranceRequirementLevel.Policy);
        assessment.Authority.Should().Be(AssuranceAuthority.ProviderPolicy);
        assessment.Source.Revision.Should().Be("extension-revision");
        assessment.Source.Sha256.Should().Be(new string('a', 64));
        assessment.Confidence.Should().Be(summary.ConfidenceLevel);
        assessment.Outcome.Should().Be(expected);
        assessment.Summary.Should().Contain(explanation);
        if (variant is "review" or "spec-failure")
        {
            var finding = claims.Single(claim => claim != assessment);
            var normative = variant == "spec-failure";
            finding.ClaimId.Should().Be("extension:example-test-extension:extension-rule:owned-tool");
            finding.Subject.Should().Be("example.test/extension");
            finding.Dimension.Should().Be(AssuranceDimension.Extension);
            finding.RequirementLevel.Should().Be(normative ? AssuranceRequirementLevel.Must : AssuranceRequirementLevel.SecurityControl);
            finding.Authority.Should().Be(normative ? AssuranceAuthority.OfficialSpecification : AssuranceAuthority.Heuristic);
            finding.Outcome.Should().Be(normative ? AssuranceClaimOutcome.Violated : AssuranceClaimOutcome.ReviewRequired);
            finding.Confidence.Should().Be(normative ? EvidenceConfidenceLevel.High : EvidenceConfidenceLevel.Medium);
            finding.Source.Uri.Should().Be("https://example.test/extension-spec");
            finding.Source.Revision.Should().Be("extension-revision");
            finding.Source.Sha256.Should().Be(new string('a', 64));
            finding.Summary.Should().Be("Observed extension issue");
            finding.Remediation.Should().Be("Repair extension control");
        }
    }

    [Theory]
    [InlineData(ClientProfileCompatibilityStatus.Compatible, AssuranceClaimOutcome.Satisfied)]
    [InlineData(ClientProfileCompatibilityStatus.Incompatible, AssuranceClaimOutcome.Violated)]
    [InlineData(ClientProfileCompatibilityStatus.CompatibleWithWarnings, AssuranceClaimOutcome.ReviewRequired)]
    public void Project_ClientCompatibility_ShouldRetainInterpretedLane(ClientProfileCompatibilityStatus status, AssuranceClaimOutcome expected)
    {
        var result = new ValidationResult { ClientCompatibility = new ClientCompatibilityReport { Assessments = [new ClientProfileAssessment
        {
            ProfileId = "owned-client", Revision = "client-revision", DocumentationUrl = "https://example.test/client",
            Status = status, Summary = "Interpreted client requirements"
        }] }};
        var claim = AssuranceClaimProjector.Project(result).Should().ContainSingle().Subject;
        claim.ClaimId.Should().Be("client:owned-client");
        claim.Subject.Should().Be("owned-client");
        claim.Dimension.Should().Be(AssuranceDimension.ClientCompatibility);
        claim.RequirementLevel.Should().Be(AssuranceRequirementLevel.Policy);
        claim.Authority.Should().Be(AssuranceAuthority.ProviderPolicy);
        claim.Outcome.Should().Be(expected);
        claim.Confidence.Should().Be(EvidenceConfidenceLevel.Medium);
        claim.Source.Uri.Should().Be("https://example.test/client");
        claim.Source.Revision.Should().Be("client-revision");
        claim.Summary.Should().Be("Interpreted client requirements");
    }

    [Theory]
    [InlineData("SeCuRiTy", null, AssuranceDimension.Security)]
    [InlineData("PeRfOrMaNcE", null, AssuranceDimension.Operations)]
    [InlineData("ErRoR", null, AssuranceDimension.Operations)]
    [InlineData("protocol", null, AssuranceDimension.CoreProtocol)]
    [InlineData("security", EvaluationLane.ControlledApplication, AssuranceDimension.ControlledApplication)]
    [InlineData("security", EvaluationLane.ExtensionReadiness, AssuranceDimension.Extension)]
    [InlineData("security", EvaluationLane.ClientCompatibility, AssuranceDimension.ClientCompatibility)]
    [InlineData("security", EvaluationLane.ExternalConformance, AssuranceDimension.ExternalConformance)]
    public void Project_NotApplicableCoverage_ShouldPreserveLaneAndReason(string layer, EvaluationLane? lane, AssuranceDimension dimension)
    {
        var result = new ValidationResult();
        var coverage = new ValidationCoverageDeclaration
        {
            LayerId = layer, Scope = "owned-check", Lane = lane, Status = ValidationCoverageStatus.NotApplicable,
            Reason = "Not defined for this transport", Confidence = EvidenceConfidenceLevel.High
        };
        result.Evidence.Coverage.Add(coverage);
        var claim = AssuranceClaimProjector.Project(result).Should().ContainSingle().Subject;
        claim.Dimension.Should().Be(dimension);
        claim.RequirementLevel.Should().Be(dimension == AssuranceDimension.Security ? AssuranceRequirementLevel.SecurityControl :
            dimension == AssuranceDimension.Operations ? AssuranceRequirementLevel.OperationalObjective : AssuranceRequirementLevel.Policy);
        claim.Applicability.Should().Be(AssuranceApplicability.NotApplicable);
        claim.ApplicabilityReason.Should().Be(coverage.Reason);
        claim.Outcome.Should().Be(AssuranceClaimOutcome.NotApplicable);
        claim.Summary.Should().Be(coverage.Reason);
        claim.EvidenceIds.Should().Equal(ValidationEvidenceIdBuilder.ForCoverage(coverage));
    }

    [Theory]
    [InlineData("ordinary", null, AssuranceDimension.CapabilityContract, AssuranceRequirementLevel.Must)]
    [InlineData("SeCuRiTy", null, AssuranceDimension.Security, AssuranceRequirementLevel.SecurityControl)]
    [InlineData("AuTh", null, AssuranceDimension.Security, AssuranceRequirementLevel.SecurityControl)]
    [InlineData("security", ImpactArea.OperationalResilience, AssuranceDimension.Operations, AssuranceRequirementLevel.OperationalObjective)]
    [InlineData("ordinary", ImpactArea.AuthenticationBoundary, AssuranceDimension.Security, AssuranceRequirementLevel.SecurityControl)]
    [InlineData("ordinary", ImpactArea.DataExposure, AssuranceDimension.Security, AssuranceRequirementLevel.SecurityControl)]
    [InlineData("ordinary", ImpactArea.UnsafeAutonomy, AssuranceDimension.Security, AssuranceRequirementLevel.SecurityControl)]
    [InlineData("ordinary", ImpactArea.ProtocolInteroperability, AssuranceDimension.CoreProtocol, AssuranceRequirementLevel.Must)]
    [InlineData("ordinary", ImpactArea.CapabilityContract, AssuranceDimension.CapabilityContract, AssuranceRequirementLevel.Must)]
    public void Project_Finding_ShouldPreserveDeclaredScopeAndProvenance(string category, ImpactArea? impact,
        AssuranceDimension dimension, AssuranceRequirementLevel requirement)
    {
        var finding = new ValidationFinding
        {
            RuleId = "RULE.ONE", Component = "owned-tool", Category = category, Source = ValidationRuleSource.Spec,
            Severity = ValidationFindingSeverity.High, Summary = "Observed contract failure", Recommendation = "Repair the declared contract",
            SpecReference = "https://example.test/dated-spec", ImpactAreas = impact.HasValue ? [impact.Value] : null,
            Metadata = new() { ["ruleRevision"] = "fixture-revision", ["evidenceId"] = "observed-evidence" }
        };
        var result = new ValidationResult { ToolValidation = new ToolTestResult { Findings = [finding] } };

        var claim = AssuranceClaimProjector.Project(result).Should().ContainSingle().Subject;

        claim.ClaimId.Should().Be("finding:rule-one:owned-tool");
        claim.Subject.Should().Be("owned-tool");
        claim.Dimension.Should().Be(dimension);
        claim.RequirementLevel.Should().Be(requirement);
        claim.Authority.Should().Be(AssuranceAuthority.OfficialSpecification);
        claim.Outcome.Should().Be(AssuranceClaimOutcome.Violated);
        claim.Confidence.Should().Be(EvidenceConfidenceLevel.High);
        claim.Source.Uri.Should().Be(finding.SpecReference);
        claim.Source.Revision.Should().Be("fixture-revision");
        claim.EvidenceIds.Should().Equal("observed-evidence");
        claim.Summary.Should().Be(finding.Summary);
        claim.Remediation.Should().Be(finding.Recommendation);
        claim.Metadata.Should().BeEquivalentTo(new Dictionary<string, string>(finding.Metadata)
        { ["ruleId"] = "RULE.ONE", ["severity"] = "High" });
    }

    [Theory]
    [InlineData("mAy", AssuranceRequirementLevel.May)]
    [InlineData("unknown", AssuranceRequirementLevel.Must)]
    [InlineData("999", AssuranceRequirementLevel.Must)]
    public void Project_Finding_ShouldAcceptOnlyDefinedRequirementOverrides(string value, AssuranceRequirementLevel expected)
    {
        var result = new ValidationResult { ToolValidation = new ToolTestResult { Findings = [new ValidationFinding
        {
            RuleId = "", Component = "", Category = "Input Contract", Source = ValidationRuleSource.Spec,
            Severity = ValidationFindingSeverity.High, Metadata = new() { ["requirementLevel"] = value }
        }] }};

        var claim = AssuranceClaimProjector.Project(result).Should().ContainSingle().Subject;

        claim.RequirementLevel.Should().Be(expected);
        claim.ClaimId.Should().Be("finding:unidentified-input-contract-unknown:unknown");
        claim.Subject.Should().Be(nameof(AssuranceDimension.CapabilityContract));
        claim.Metadata["ruleId"].Should().Be("unidentified:input-contract:unknown");
        claim.Source.Revision.Should().Be(ValidationFindingRuleIds.CatalogVersion);
        claim.EvidenceIds.Should().BeEmpty();
    }

    public static IEnumerable<object[]> FindingProjectionCases()
    {
        foreach (var source in new[] { ValidationRuleSource.Spec, ValidationRuleSource.Guideline, ValidationRuleSource.Heuristic })
            foreach (var severity in new[] { ValidationFindingSeverity.Medium, ValidationFindingSeverity.High })
                foreach (var gate in new GateOutcome?[] { null, GateOutcome.Reject, GateOutcome.CoverageDebt })
                    yield return [source, severity, gate!];
    }

    [Theory]
    [MemberData(nameof(FindingProjectionCases))]
    public void Project_Finding_ShouldSeparateNormativeFailureFromAdvisoryEvidence(ValidationRuleSource source, ValidationFindingSeverity severity, GateOutcome? gate)
    {
        var result = new ValidationResult { ToolValidation = new ToolTestResult { Findings = [new ValidationFinding
        {
            RuleId = "source-rule", Component = "tool", Source = source, Severity = severity, GateOverride = gate
        }] }};
        var claim = AssuranceClaimProjector.Project(result).Should().ContainSingle().Subject;
        var specification = source == ValidationRuleSource.Spec;
        var mandatory = gate == GateOutcome.Reject || severity == ValidationFindingSeverity.High;

        claim.Authority.Should().Be(specification ? AssuranceAuthority.OfficialSpecification :
            source == ValidationRuleSource.Heuristic ? AssuranceAuthority.Heuristic : AssuranceAuthority.ProviderPolicy);
        claim.Confidence.Should().Be(specification ? EvidenceConfidenceLevel.High : EvidenceConfidenceLevel.Medium);
        claim.RequirementLevel.Should().Be(specification ? mandatory ? AssuranceRequirementLevel.Must : AssuranceRequirementLevel.Should : AssuranceRequirementLevel.Policy);
        claim.Outcome.Should().Be(gate == GateOutcome.CoverageDebt ? AssuranceClaimOutcome.Incomplete :
            specification && mandatory ? AssuranceClaimOutcome.Violated : AssuranceClaimOutcome.ReviewRequired);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Project_ProtocolViolation_ShouldRetainIdentityAndProvenance(bool identified)
    {
        var violation = new ComplianceViolation
        {
            CheckId = identified ? "Case.One" : null!,
            Category = identified ? "JSON-RPC" : "",
            Rule = identified ? "Required identifier" : null!,
            Description = "Observed failure", Recommendation = "Supply the required identifier",
            SpecReference = "https://example.test/spec", Severity = ViolationSeverity.High
        };
        var result = new ValidationResult
        {
            ProtocolVersion = identified ? "2026-07-28" : null,
            ProtocolCompliance = new ComplianceTestResult { Violations = [violation] }
        };

        var claim = AssuranceClaimProjector.Project(result).Should().ContainSingle().Subject;

        claim.ClaimId.Should().Be(identified ? "protocol:case-one" : "protocol:unknown:observed-failure");
        claim.Subject.Should().Be(identified ? "JSON-RPC" : "protocol");
        claim.Dimension.Should().Be(AssuranceDimension.CoreProtocol);
        claim.RequirementLevel.Should().Be(AssuranceRequirementLevel.Must);
        claim.Authority.Should().Be(AssuranceAuthority.OfficialSpecification);
        claim.Outcome.Should().Be(AssuranceClaimOutcome.Violated);
        claim.Confidence.Should().Be(EvidenceConfidenceLevel.High);
        claim.Source.Uri.Should().Be(violation.SpecReference);
        claim.Source.Revision.Should().Be(identified ? "2026-07-28" : "unknown");
        claim.EvidenceIds.Should().Equal(identified ? ["protocol/Case.One"] : Array.Empty<string>());
        claim.Summary.Should().Be(violation.Description);
        claim.Remediation.Should().Be(violation.Recommendation);
        claim.Metadata.Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["ruleId"] = identified ? "Case.One" : "protocol:unknown:observed-failure", ["severity"] = "High"
        });
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void Project_Vulnerability_ShouldSeparateExploitationFromHeuristicAdvice(bool exploitable, bool identified)
    {
        var result = new ValidationResult { SecurityTesting = new SecurityTestResult
        {
            Vulnerabilities = [new()
            {
                Id = identified ? "SEC.ONE" : "", Category = "Authorization", Name = "Object policy",
                AffectedComponent = identified ? "owned-tool" : "", IsExploitable = exploitable,
                Description = "Observed authorization evidence", Remediation = "Enforce object policy"
            }]
        }};
        var source = result.SecurityTesting.Vulnerabilities[0];

        var claim = AssuranceClaimProjector.Project(result).Should().ContainSingle().Subject;

        claim.ClaimId.Should().Be(identified ? "vulnerability:sec-one:owned-tool" : "vulnerability:security-authorization-object-policy:unknown");
        claim.Subject.Should().Be(identified ? "owned-tool" : "security");
        claim.Dimension.Should().Be(AssuranceDimension.Security);
        claim.RequirementLevel.Should().Be(AssuranceRequirementLevel.SecurityControl);
        claim.Authority.Should().Be(exploitable ? AssuranceAuthority.DeterministicSecurity : AssuranceAuthority.Heuristic);
        claim.Outcome.Should().Be(exploitable ? AssuranceClaimOutcome.Violated : AssuranceClaimOutcome.ReviewRequired);
        claim.Confidence.Should().Be(exploitable ? EvidenceConfidenceLevel.High : EvidenceConfidenceLevel.Medium);
        claim.Source.Revision.Should().Be(ValidationFindingRuleIds.CatalogVersion);
        claim.Summary.Should().Be(source.Description);
        claim.Remediation.Should().Be(source.Remediation);
        claim.Metadata["ruleId"].Should().Be(identified ? "SEC.ONE" : "security:authorization:object-policy");
        claim.Metadata["severity"].Should().Be(source.Severity.ToString());
        claim.Metadata["exploitable"].Should().Be(exploitable.ToString());
    }

    public static IEnumerable<object[]> AttackProjectionCases()
    {
        foreach (var authority in new[] { ValidationRuleSource.Spec, ValidationRuleSource.Guideline, ValidationRuleSource.Heuristic })
            foreach (var entry in new[]
            {
                (ValidationOutcome.Succeeded, AssuranceClaimOutcome.Satisfied),
                (ValidationOutcome.Failed, AssuranceClaimOutcome.Violated),
                (ValidationOutcome.Error, AssuranceClaimOutcome.Error),
                (ValidationOutcome.Inconclusive, AssuranceClaimOutcome.Incomplete),
                (ValidationOutcome.Unavailable, AssuranceClaimOutcome.Incomplete),
                (ValidationOutcome.Blocked, AssuranceClaimOutcome.Incomplete),
                (ValidationOutcome.Skipped, AssuranceClaimOutcome.ReviewRequired),
                (ValidationOutcome.NotApplicable, AssuranceClaimOutcome.ReviewRequired)
            }) yield return [authority, entry.Item1, authority == ValidationRuleSource.Heuristic && entry.Item1 == ValidationOutcome.Failed
                ? AssuranceClaimOutcome.ReviewRequired : entry.Item2];
    }

    [Theory]
    [MemberData(nameof(AttackProjectionCases))]
    public void Project_Attack_ShouldPreserveAuthorityOutcomeAndEvidence(ValidationRuleSource authority, ValidationOutcome outcome, AssuranceClaimOutcome expected)
    {
        var result = new ValidationResult { SecurityTesting = new SecurityTestResult
        {
            AttackSimulations = [new AttackSimulationResult
            {
                AttackVector = "owned-control", Description = "Recorded control observation", Authority = authority, Outcome = outcome,
                ProbeContexts = [new() { ProbeId = "observed-probe" }, new() { ProbeId = " " }, new() { ProbeId = "" }, new() { ProbeId = null! }]
            }]
        }};
        var claims = AssuranceClaimProjector.Project(result);
        if (authority == ValidationRuleSource.Heuristic && outcome == ValidationOutcome.Succeeded)
        {
            claims.Should().BeEmpty();
            return;
        }
        var claim = claims.Should().ContainSingle().Subject;
        claim.ClaimId.Should().Be("attack:owned-control");
        claim.Subject.Should().Be("owned-control");
        claim.Summary.Should().Be("Recorded control observation");
        claim.Dimension.Should().Be(AssuranceDimension.Security);
        claim.RequirementLevel.Should().Be(AssuranceRequirementLevel.SecurityControl);
        claim.Authority.Should().Be(authority == ValidationRuleSource.Spec ? AssuranceAuthority.OfficialSpecification :
            authority == ValidationRuleSource.Heuristic ? AssuranceAuthority.Heuristic : AssuranceAuthority.DeterministicSecurity);
        claim.Outcome.Should().Be(expected);
        claim.Confidence.Should().Be(authority == ValidationRuleSource.Heuristic ? EvidenceConfidenceLevel.Medium : EvidenceConfidenceLevel.High);
        claim.Source.Revision.Should().Be(ValidationFindingRuleIds.CatalogVersion);
        claim.EvidenceIds.Should().Equal("observed-probe");
    }

    [Theory]
    [InlineData(OfficialConformanceScoringDisposition.Required, AssuranceExitCodeMapper.Incomplete)]
    [InlineData(OfficialConformanceScoringDisposition.NotScored, AssuranceExitCodeMapper.Satisfied)]
    public void ComprehensiveUntestableOfficialCheck_ShouldGateOnlyRequiredEvidence(OfficialConformanceScoringDisposition disposition, int expected)
    {
        var result = CreateCompleteResult();
        ControlledIdentityEvaluatorTests.AddSecurityBaseline(result);
        var passed = CreateOfficialConformance(OfficialConformanceVerificationStatus.Verified);
        result.Assessments.OfficialConformance = CreateOfficialConformance(OfficialConformanceVerificationStatus.Verified,
            passed.Scenarios.Concat([new OfficialConformanceScenarioAssessment
            {
                ScenarioId = "unavailable-fixture", ScoringDisposition = disposition,
                Checks = [new OfficialConformanceCheckAssessment
                {
                    Id = "missing-prerequisite", Name = "Missing prerequisite", Description = "Required fixture was unavailable.",
                    Status = OfficialConformanceCheckStatus.Failure, PrerequisiteMissing = true
                }]
            }]).ToArray(), failed: 1);
        var evaluation = AssuranceAssessmentEngine.Evaluate(result, AssuranceProfileCatalog.Comprehensive);

        evaluation.Assessments.ExternalConformance.Status.Should().Be(ExternalConformanceStatus.Incomplete);
        evaluation.Claims.Should().Contain(claim => claim.Subject == "unavailable-fixture" && claim.Outcome == AssuranceClaimOutcome.Incomplete);
        AssuranceExitCodeMapper.Map(evaluation, null).Should().Be(expected);
    }

    [Fact]
    public void ComprehensiveWithoutSelectedIdentityBaseline_ShouldNotAcceptGenericSecurityClaims()
    {
        var result = CreateCompleteResult();
        var evaluation = AssuranceAssessmentEngine.Evaluate(result, AssuranceProfileCatalog.Comprehensive);

        evaluation.Assessments.SecurityAssurance.Status.Should().Be(SecurityAssuranceStatus.Incomplete);
        evaluation.Claims.Where(claim => claim.ClaimId.StartsWith("security-baseline:", StringComparison.Ordinal))
            .Should().HaveCount(3).And.OnlyContain(claim => claim.Outcome == AssuranceClaimOutcome.Incomplete);
        AssuranceExitCodeMapper.Map(evaluation, null).Should().NotBe(AssuranceExitCodeMapper.Satisfied);
    }

    [Theory]
    [InlineData(true, ValidationOutcome.Succeeded, AssuranceExitCodeMapper.Incomplete)]
    [InlineData(false, ValidationOutcome.NotEvaluated, AssuranceExitCodeMapper.Incomplete)]
    [InlineData(false, ValidationOutcome.Failed, AssuranceExitCodeMapper.Failed)]
    [InlineData(false, ValidationOutcome.Error, AssuranceExitCodeMapper.Failed)]
    [InlineData(false, ValidationOutcome.Succeeded, AssuranceExitCodeMapper.Satisfied)]
    public void SelectedExtension_ShouldRequireActualCoverageAndPreserveRecordedFailures(bool empty, ValidationOutcome outcome, int expected)
    {
        var result = CreateCompleteResult();
        ControlledIdentityEvaluatorTests.AddSecurityBaseline(result);
        result.Assessments.OfficialConformance = CreateOfficialConformance(OfficialConformanceVerificationStatus.Verified);
        const string extensionId = "example.test/required";
        var coverage = empty ? Array.Empty<ValidationCoverageDeclaration>() : [new ValidationCoverageDeclaration
        {
            LayerId = "extension-fixture", Scope = "required-check", Lane = EvaluationLane.ExtensionReadiness,
            Status = ValidationCoverageStatus.Covered, ObservedOutcome = outcome
        }];
        var summary = ValidationEvidenceSummarizer.Summarize(coverage);
        result.Assessments.Extensions.Add(new ExtensionAssessment
        {
            ExtensionId = extensionId, Stage = ExtensionLifecycleStage.Stable,
            SpecificationRevision = "fixture-v1", SpecificationSha256 = new string('a', 64),
            Coverage = coverage, EvidenceSummary = summary
        });
        result.Assurance = AssuranceAssessmentEngine.Evaluate(result, AssuranceProfileCatalog.Comprehensive,
            new AssuranceEvaluationContext { RequiredExtensionIds = [extensionId] });

        AssuranceExitCodeMapper.Map(result.Assurance, null).Should().Be(expected);
        if (outcome == ValidationOutcome.NotEvaluated)
        {
            var clone = () => result.CloneWithoutSecrets();
            clone.Should().Throw<InvalidOperationException>();
        }
        else AssuranceExitCodeMapper.Map(result.CloneWithoutSecrets().Assurance, null).Should().Be(expected);
        if (outcome == ValidationOutcome.Failed)
            result.Assurance.Claims.Should().Contain(claim => claim.Subject == extensionId && claim.Outcome == AssuranceClaimOutcome.Violated);
    }

    [Theory]
    [InlineData(OfficialConformanceScoringDisposition.Required, AssuranceExitCodeMapper.Failed)]
    [InlineData(OfficialConformanceScoringDisposition.NotScored, AssuranceExitCodeMapper.Satisfied)]
    public void ComprehensiveOfficialFailure_ShouldGateRequiredChecksWithoutHidingUnscoredFailures(
        OfficialConformanceScoringDisposition disposition, int expected)
    {
        var result = CreateCompleteResult();
        ControlledIdentityEvaluatorTests.AddSecurityBaseline(result);
        var passed = CreateOfficialConformance(OfficialConformanceVerificationStatus.Verified);
        var scenarios = passed.Scenarios.Concat([new OfficialConformanceScenarioAssessment
        {
            ScenarioId = "optional-fixture", ScoringDisposition = disposition,
            Checks = [new OfficialConformanceCheckAssessment
            {
                Id = "failure", Name = "Failure", Description = "Recorded official failure.", Status = OfficialConformanceCheckStatus.Failure
            }]
        }]).ToArray();
        result.Assessments.OfficialConformance = CreateOfficialConformance(OfficialConformanceVerificationStatus.Verified, scenarios, failed: 1);
        var evaluation = AssuranceAssessmentEngine.Evaluate(result, AssuranceProfileCatalog.Comprehensive);

        evaluation.Assessments.ExternalConformance.Status.Should().Be(ExternalConformanceStatus.Nonconformant);
        evaluation.Claims.Should().Contain(claim => claim.Subject == "optional-fixture" && claim.Outcome == AssuranceClaimOutcome.Violated);
        AssuranceExitCodeMapper.Map(evaluation, null).Should().Be(expected);
    }

    [Theory]
    [InlineData(true, AssuranceExitCodeMapper.Failed)]
    [InlineData(false, AssuranceExitCodeMapper.Satisfied)]
    public void ComprehensiveExtensionScope_ShouldSurviveCloneAndNotGateUnselectedFindings(bool required, int expected)
    {
        var result = CreateCompleteResult();
        ControlledIdentityEvaluatorTests.AddSecurityBaseline(result);
        result.Assessments.OfficialConformance = CreateOfficialConformance(OfficialConformanceVerificationStatus.Verified);
        const string extensionId = "example.test/optional";
        result.Assessments.Extensions.Add(new ExtensionAssessment
        {
            ExtensionId = extensionId, Stage = ExtensionLifecycleStage.Stable,
            SpecificationRevision = "fixture-v1", SpecificationSha256 = new string('a', 64),
            Findings = [new ValidationFinding
            {
                RuleId = "EXT.FIXTURE", Source = ValidationRuleSource.Spec, Component = "fixture",
                Severity = ValidationFindingSeverity.High, Summary = "Fixture extension failed."
            }]
        });
        result.Assurance = AssuranceAssessmentEngine.Evaluate(result, AssuranceProfileCatalog.Comprehensive,
            new AssuranceEvaluationContext { RequiredExtensionIds = required ? [extensionId] : [] });

        foreach (var evaluation in new[] { result.Assurance, result.CloneWithoutSecrets().Assurance })
        {
            evaluation.RequiredExtensionIds.Should().Equal(required ? [extensionId] : Array.Empty<string>());
            evaluation.Claims.Should().Contain(claim => claim.Subject == extensionId && claim.Outcome == AssuranceClaimOutcome.Violated);
            AssuranceExitCodeMapper.Map(evaluation, null).Should().Be(expected);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Evaluate_Comprehensive_ShouldDeclareMissingSelectedExtensionIndependentlyOfObservedRows(bool required)
    {
        var result = CreateCompleteResult();
        const string extensionId = "example.test/required";
        var evaluation = AssuranceAssessmentEngine.Evaluate(result, AssuranceProfileCatalog.Comprehensive,
            new AssuranceEvaluationContext { RequiredExtensionIds = required ? [extensionId, extensionId] : [] });

        if (required)
        {
            evaluation.Claims.Should().ContainSingle(claim => claim.Subject == extensionId &&
                claim.Outcome == AssuranceClaimOutcome.Incomplete && claim.RequirementLevel == AssuranceRequirementLevel.Must &&
                claim.Authority == AssuranceAuthority.ProviderPolicy && claim.EvidenceIds.Count == 0);
            AssuranceExitCodeMapper.Map(evaluation, null).Should().Be(AssuranceExitCodeMapper.Incomplete);
        }
        else
            evaluation.Claims.Should().NotContain(claim => claim.Subject == extensionId);
    }

    [Fact]
    public void Evaluate_NullArguments_ShouldThrow()
    {
        var nullResult = () => AssuranceAssessmentEngine.Evaluate(null!, AssuranceProfileCatalog.Inventory);
        var nullProfile = () => AssuranceAssessmentEngine.Evaluate(new ValidationResult(), null!);

        nullResult.Should().Throw<ArgumentNullException>();
        nullProfile.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData(CoreConformanceStatus.Conformant, SecurityAssuranceStatus.Verified, OperationalReadinessStatus.Ready, false, ValidationStatus.Passed)]
    [InlineData(CoreConformanceStatus.Nonconformant, SecurityAssuranceStatus.Verified, OperationalReadinessStatus.Ready, false, ValidationStatus.Failed)]
    [InlineData(CoreConformanceStatus.Conformant, SecurityAssuranceStatus.IssuesFound, OperationalReadinessStatus.Ready, false, ValidationStatus.Failed)]
    [InlineData(CoreConformanceStatus.Conformant, SecurityAssuranceStatus.Verified, OperationalReadinessStatus.NotReady, false, ValidationStatus.Failed)]
    [InlineData(CoreConformanceStatus.Incomplete, SecurityAssuranceStatus.Verified, OperationalReadinessStatus.Ready, false, ValidationStatus.PartiallyCompleted)]
    [InlineData(CoreConformanceStatus.Conformant, SecurityAssuranceStatus.Incomplete, OperationalReadinessStatus.Ready, false, ValidationStatus.PartiallyCompleted)]
    [InlineData(CoreConformanceStatus.Conformant, SecurityAssuranceStatus.Verified, OperationalReadinessStatus.Incomplete, false, ValidationStatus.PartiallyCompleted)]
    [InlineData(CoreConformanceStatus.Conformant, SecurityAssuranceStatus.Verified, OperationalReadinessStatus.Ready, true, ValidationStatus.Error)]
    public void DetermineValidationStatus_ShouldRespectIndependentAssessmentPrecedence(
        CoreConformanceStatus core,
        SecurityAssuranceStatus security,
        OperationalReadinessStatus operations,
        bool hasCriticalErrors,
        ValidationStatus expected)
    {
        var evaluation = new AssuranceEvaluation
        {
            Profile = AssuranceProfileCatalog.Inventory,
            Assessments = new AssuranceAssessmentSet
            {
                CoreConformance = new CoreConformanceAssessment { Status = core },
                SecurityAssurance = new SecurityAssuranceAssessment { Status = security },
                OperationalReadiness = new OperationalReadinessAssessment { Status = operations }
            },
            CertificateEligibility = new CertificateEligibilityAssessment
            {
                ProfileId = AssuranceProfileCatalog.Inventory.ProfileId,
                Status = CertificateEligibilityStatus.Ineligible
            }
        };

        AssuranceAssessmentEngine.DetermineValidationStatus(evaluation, hasCriticalErrors).Should().Be(expected);
    }

    [Fact]
    public void Evaluate_HeuristicReject_ShouldRequireReviewWithoutCoreNonconformance()
    {
        var result = CreateResultWithCoveredBaseline();
        result.ToolValidation = new ToolTestResult();
        result.ToolValidation.AiReadinessFindings.Add(new ValidationFinding
        {
            RuleId = "MCP.CONTENT.SystemImpact",
            Source = ValidationRuleSource.Heuristic,
            GateOverride = GateOutcome.Reject,
            Severity = ValidationFindingSeverity.High,
            Category = "ContentSafety.SystemImpact",
            Component = "delete_file",
            Summary = "Name suggests system impact.",
            ImpactAreas = [ImpactArea.UnsafeAutonomy]
        });

        var evaluation = AssuranceAssessmentEngine.Evaluate(result, AssuranceProfileCatalog.Inventory);

        evaluation.Claims.Should().ContainSingle(claim =>
            claim.Metadata.GetValueOrDefault("ruleId") == "MCP.CONTENT.SystemImpact" &&
            claim.Authority == AssuranceAuthority.Heuristic &&
            claim.Outcome == AssuranceClaimOutcome.ReviewRequired);
        evaluation.Assessments.CoreConformance.Status.Should().Be(CoreConformanceStatus.Incomplete);
        evaluation.CertificateEligibility.Status.Should().Be(CertificateEligibilityStatus.Ineligible);
    }

    [Fact]
    public void Evaluate_MustViolation_ShouldMakeCoreNonconformant()
    {
        var result = CreateResultWithCoveredBaseline();
        result.ProtocolCompliance = new ComplianceTestResult
        {
            Violations =
            [
                new ComplianceViolation
                {
                    CheckId = "MCP.JSONRPC.REQUEST_ID",
                    Category = "JSON-RPC",
                    Description = "Requests must include a non-null ID.",
                    Severity = ViolationSeverity.High
                }
            ]
        };

        var evaluation = AssuranceAssessmentEngine.Evaluate(result, AssuranceProfileCatalog.Inventory);

        evaluation.Assessments.CoreConformance.Status.Should().Be(CoreConformanceStatus.Nonconformant);
        evaluation.Assessments.CoreConformance.BlockingClaimIds.Should().ContainSingle();
    }

    [Fact]
    public void Evaluate_IncompleteMust_ShouldMakeCoreIncomplete()
    {
        var result = CreateResultWithCoveredBaseline();
        result.Assessments.OfficialConformance = CreateOfficialConformance(
            OfficialConformanceVerificationStatus.Incomplete,
            [
                new OfficialConformanceScenarioAssessment
                {
                    ScenarioId = "required",
                    ScoringDisposition = OfficialConformanceScoringDisposition.Required,
                    Checks =
                    [
                        new OfficialConformanceCheckAssessment
                        {
                            Id = "check",
                            Name = "check",
                            Description = "Required check.",
                            Status = OfficialConformanceCheckStatus.Skipped
                        }
                    ]
                }
            ]);

        var evaluation = AssuranceAssessmentEngine.Evaluate(result, AssuranceProfileCatalog.Inventory);

        evaluation.Assessments.CoreConformance.Status.Should().Be(CoreConformanceStatus.Incomplete);
        evaluation.Assessments.CoreConformance.RequiredClaims.Should().Be(1);
        evaluation.Assessments.CoreConformance.IncompleteClaimIds.Should().ContainSingle();
    }

    [Fact]
    public void Evaluate_ShouldAndExtensionClaims_ShouldNotCountAsRequiredCoreClaims()
    {
        var result = CreateCompleteResult();
        result.ProtocolCompliance!.Findings.Add(new ValidationFinding
        {
            RuleId = "MCP.SHOULD.DESCRIPTION",
            Source = ValidationRuleSource.Spec,
            Component = "optional",
            Severity = ValidationFindingSeverity.Medium,
            Summary = "Recommended behavior is absent.",
            Metadata = new Dictionary<string, string> { ["requirementLevel"] = "Should" }
        });
        result.Assessments.Extensions.Add(new ExtensionAssessment
        {
            ExtensionId = "example/extension",
            Stage = ExtensionLifecycleStage.Stable,
            SpecificationRevision = "2026-01-01",
            SpecificationSha256 = new string('e', 64),
            Verdict = ValidationVerdict.Reject,
            Findings =
            [
                new ValidationFinding
                {
                    RuleId = "EXT.MUST",
                    Source = ValidationRuleSource.Spec,
                    Component = "extension",
                    Severity = ValidationFindingSeverity.High,
                    Summary = "Extension requirement failed."
                }
            ]
        });

        var evaluation = AssuranceAssessmentEngine.Evaluate(result, AssuranceProfileCatalog.Inventory);

        evaluation.Assessments.CoreConformance.RequiredClaims.Should().Be(1);
        evaluation.Assessments.CoreConformance.Status.Should().Be(CoreConformanceStatus.Conformant);
    }

    [Fact]
    public void Evaluate_ProviderProfile_ShouldFailClosedWhenEvidencePrerequisitesAreMissing()
    {
        var result = CreateResultWithCoveredBaseline();

        var evaluation = AssuranceAssessmentEngine.Evaluate(result, AssuranceProfileCatalog.Provider, new AssuranceEvaluationContext
        {
            ExecutionMode = ExecutionMode.Standard
        });

        evaluation.CertificateEligibility.Status.Should().Be(CertificateEligibilityStatus.ReviewRequired);
        evaluation.CertificateEligibility.Reasons.Should().Contain(reason => reason.Contains("official conformance", StringComparison.OrdinalIgnoreCase));
        evaluation.CertificateEligibility.Reasons.Should().Contain(reason => reason.Contains("signed attestation", StringComparison.OrdinalIgnoreCase));
        evaluation.CertificateEligibility.Reasons.Should().Contain(reason => reason.Contains("Repeated-run", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Evaluate_ClientCompatibilityFailure_ShouldRemainSeparateFromCoreAssessment()
    {
        var result = CreateResultWithCoveredBaseline();
        result.Evidence.Coverage.Add(new ValidationCoverageDeclaration
        {
            LayerId = "client-profiles",
            Scope = "incompatible-client",
            Status = ValidationCoverageStatus.Covered,
            ObservedOutcome = ValidationOutcome.Failed,
            Lane = EvaluationLane.ClientCompatibility,
            Confidence = EvidenceConfidenceLevel.High
        });

        var evaluation = AssuranceAssessmentEngine.Evaluate(result, AssuranceProfileCatalog.Inventory);

        evaluation.EvidenceSummary.TotalDeclarations.Should().Be(1);
        evaluation.Claims.Should().Contain(claim =>
            claim.Dimension == AssuranceDimension.ClientCompatibility &&
            claim.Outcome == AssuranceClaimOutcome.Violated);
    }

    [Fact]
    public void Evaluate_SatisfiedMustAndDeterministicControls_ShouldProduceIndependentSuccesses()
    {
        var result = CreateCompleteResult();

        var evaluation = AssuranceAssessmentEngine.Evaluate(result, AssuranceProfileCatalog.Inventory);

        evaluation.Assessments.CoreConformance.Status.Should().Be(CoreConformanceStatus.Conformant);
        evaluation.Assessments.CoreConformance.RequiredClaims.Should().Be(1);
        evaluation.Assessments.CoreConformance.SatisfiedRequiredClaims.Should().Be(1);
        evaluation.Assessments.SecurityAssurance.Status.Should().Be(SecurityAssuranceStatus.Verified);
        evaluation.Assessments.SecurityAssurance.DeterministicControlClaims.Should().Be(1);
        evaluation.Assessments.OperationalReadiness.Status.Should().Be(OperationalReadinessStatus.Ready);
        evaluation.Assessments.OperationalReadiness.ObjectiveClaims.Should().Be(1);
    }

    [Theory]
    [InlineData(GateOutcome.Reject, SecurityAssuranceStatus.IssuesFound)]
    [InlineData(GateOutcome.ReviewRequired, SecurityAssuranceStatus.Incomplete)]
    [InlineData(GateOutcome.CoverageDebt, SecurityAssuranceStatus.Incomplete)]
    public void Evaluate_DeterministicSecurityOutcomes_ShouldReduceExactly(
        GateOutcome gate,
        SecurityAssuranceStatus expected)
    {
        var result = CreateCompleteResult();
        result.SecurityTesting!.AttackSimulations.Clear();
        result.SecurityTesting.AttackSimulations.Add(new AttackSimulationResult
        {
            AttackVector = "security-control",
            Description = "Security control outcome.",
            Authority = ValidationRuleSource.Guideline,
            Gate = gate,
            Outcome = gate == GateOutcome.Reject ? ValidationOutcome.Failed : ValidationOutcome.Inconclusive,
            Severity = gate == GateOutcome.Reject ? ValidationFindingSeverity.High : ValidationFindingSeverity.Medium
        });

        var evaluation = AssuranceAssessmentEngine.Evaluate(result, AssuranceProfileCatalog.Inventory);

        evaluation.Assessments.SecurityAssurance.Status.Should().Be(expected);
        if (expected == SecurityAssuranceStatus.IssuesFound)
            evaluation.Assessments.SecurityAssurance.IssueClaimIds.Should().ContainSingle("attack:security-control");
        else
            evaluation.Assessments.SecurityAssurance.IncompleteClaimIds.Should().ContainSingle("attack:security-control");
    }

    [Theory]
    [InlineData(GateOutcome.Reject, OperationalReadinessStatus.NotReady)]
    [InlineData(GateOutcome.ReviewRequired, OperationalReadinessStatus.Incomplete)]
    [InlineData(GateOutcome.CoverageDebt, OperationalReadinessStatus.Incomplete)]
    public void Evaluate_OperationalOutcomes_ShouldReduceExactly(
        GateOutcome gate,
        OperationalReadinessStatus expected)
    {
        var result = CreateCompleteResult();
        result.PerformanceTesting!.Findings.Add(new ValidationFinding
        {
            RuleId = "OPERATIONS.OBJECTIVE",
            Source = ValidationRuleSource.Spec,
            Component = "operations-objective",
            Severity = gate == GateOutcome.Reject ? ValidationFindingSeverity.High : ValidationFindingSeverity.Medium,
            GateOverride = gate,
            ImpactAreas = [ImpactArea.OperationalResilience],
            Summary = "Operational objective outcome."
        });

        var evaluation = AssuranceAssessmentEngine.Evaluate(result, AssuranceProfileCatalog.Inventory);

        evaluation.Assessments.OperationalReadiness.Status.Should().Be(expected);
    }

    [Fact]
    public void Evaluate_SecurityCoverageDebt_ShouldPreventVerifiedStatus()
    {
        var result = CreateCompleteResult();
        result.Evidence.Coverage.RemoveAll(item => item.LayerId == "security-controls");
        result.Evidence.Coverage.Add(new ValidationCoverageDeclaration
        {
            LayerId = "security-controls",
            Scope = "authentication",
            Status = ValidationCoverageStatus.Unavailable,
            ObservedOutcome = ValidationOutcome.Unavailable,
            Lane = EvaluationLane.Baseline,
            Confidence = EvidenceConfidenceLevel.None
        });

        var evaluation = AssuranceAssessmentEngine.Evaluate(result, AssuranceProfileCatalog.Inventory);

        evaluation.Assessments.SecurityAssurance.Status.Should().Be(SecurityAssuranceStatus.Incomplete);
    }

    [Fact]
    public void Evaluate_OperationsCoverageDebt_ShouldAddSyntheticIncompleteClaim()
    {
        var result = CreateCompleteResult();
        result.Evidence.Coverage.RemoveAll(item => item.LayerId == "performance");
        result.Evidence.Coverage.Add(new ValidationCoverageDeclaration
        {
            LayerId = "performance",
            Scope = "repeated-measurements",
            Status = ValidationCoverageStatus.Blocked,
            ObservedOutcome = ValidationOutcome.Blocked,
            Lane = EvaluationLane.Baseline,
            Confidence = EvidenceConfidenceLevel.None
        });

        var evaluation = AssuranceAssessmentEngine.Evaluate(result, AssuranceProfileCatalog.Inventory);

        evaluation.Assessments.OperationalReadiness.Status.Should().Be(OperationalReadinessStatus.Incomplete);
        evaluation.Assessments.OperationalReadiness.IncompleteClaimIds.Should().Contain("coverage:performance:repeated-measurements");
    }

    [Fact]
    public void Evaluate_HeuristicReview_ShouldNotDowngradeVerifiedDeterministicSecurity()
    {
        var result = CreateCompleteResult();
        result.ToolValidation = new ToolTestResult();
        result.ToolValidation.AiReadinessFindings.Add(new ValidationFinding
        {
            RuleId = "HEURISTIC.REVIEW",
            Source = ValidationRuleSource.Heuristic,
            Severity = ValidationFindingSeverity.Medium,
            Category = "Security",
            Component = "tool",
            Summary = "Review metadata.",
            ImpactAreas = [ImpactArea.UnsafeAutonomy]
        });

        var evaluation = AssuranceAssessmentEngine.Evaluate(result, AssuranceProfileCatalog.Inventory);

        evaluation.Assessments.SecurityAssurance.Status.Should().Be(SecurityAssuranceStatus.Verified);
        evaluation.Assessments.SecurityAssurance.ReviewClaimCount.Should().Be(1);
    }

    [Fact]
    public void Evaluate_UnrelatedCoverageDebt_ShouldNotDowngradeSecurityOrOperations()
    {
        var result = CreateCompleteResult();
        result.Evidence.Coverage.Add(new ValidationCoverageDeclaration
        {
            LayerId = "resource-catalog",
            Scope = "resources/list",
            Status = ValidationCoverageStatus.Unavailable,
            ObservedOutcome = ValidationOutcome.Unavailable,
            Lane = EvaluationLane.Baseline,
            Confidence = EvidenceConfidenceLevel.None
        });

        var evaluation = AssuranceAssessmentEngine.Evaluate(result, AssuranceProfileCatalog.Inventory);

        evaluation.Assessments.SecurityAssurance.Status.Should().Be(SecurityAssuranceStatus.Verified);
        evaluation.Assessments.OperationalReadiness.Status.Should().Be(OperationalReadinessStatus.Ready);
    }

    [Fact]
    public void Evaluate_OperationalRequirementOutsideOperationsDimension_ShouldNotAffectReadiness()
    {
        var result = CreateCompleteResult();
        result.SecurityTesting!.Findings.Add(new ValidationFinding
        {
            RuleId = "SECURITY.OPERATIONAL_LABEL",
            Source = ValidationRuleSource.Spec,
            Component = "security-control",
            Severity = ValidationFindingSeverity.High,
            ImpactAreas = [ImpactArea.AuthenticationBoundary],
            Summary = "Security failure carrying an operational requirement label.",
            Metadata = new Dictionary<string, string> { ["requirementLevel"] = "OperationalObjective" }
        });

        var evaluation = AssuranceAssessmentEngine.Evaluate(result, AssuranceProfileCatalog.Inventory);

        evaluation.Assessments.SecurityAssurance.Status.Should().Be(SecurityAssuranceStatus.IssuesFound);
        evaluation.Assessments.OperationalReadiness.Status.Should().Be(OperationalReadinessStatus.Ready);
    }

    [Fact]
    public void Evaluate_ExtensionAndClientAssessments_ShouldBeOrdinallyStable()
    {
        var result = CreateCompleteResult();
        result.Assessments.Extensions.Add(CreateExtension("z.extension"));
        var extension = CreateExtension("a.extension",
        [
            new ValidationFinding
            {
                RuleId = "Z.RULE",
                Source = ValidationRuleSource.Heuristic,
                Component = "z-component",
                Summary = "Z review finding."
            },
            new ValidationFinding
            {
                RuleId = "A.RULE",
                Source = ValidationRuleSource.Heuristic,
                Component = "a-component",
                Summary = "A review finding."
            }
        ]);
        result.Assessments.Extensions.Add(extension);
        result.ClientCompatibility = new ClientCompatibilityReport
        {
            Assessments =
            [
                new ClientProfileAssessment { ProfileId = "z-client", Revision = "1", Summary = "Z" },
                new ClientProfileAssessment { ProfileId = "a-client", Revision = "1", Summary = "A" }
            ]
        };

        var evaluation = AssuranceAssessmentEngine.Evaluate(result, AssuranceProfileCatalog.Inventory);

        evaluation.Assessments.ExtensionReadiness.Select(item => item.ExtensionId).Should().Equal("a.extension", "z.extension");
        evaluation.Assessments.ExtensionReadiness[0].NonSuccessClaimIds.Should().BeInAscendingOrder(StringComparer.Ordinal);
        evaluation.Assessments.ClientCompatibility.Select(item => item.ProfileId).Should().Equal("a-client", "z-client");
        evaluation.Assessments.ClientCompatibility.Should().OnlyContain(item => item.Verification == ClientCompatibilityVerification.Interpreted);
    }

    [Theory]
    [InlineData(0, ExternalConformanceStatus.Verified)]
    [InlineData(1, ExternalConformanceStatus.Nonconformant)]
    public void Evaluate_VerifiedExternalConformance_ShouldHonorFailureBoundary(
        int failed,
        ExternalConformanceStatus expected)
    {
        var result = CreateCompleteResult();
        result.Assessments.OfficialConformance = CreateOfficialConformance(
            OfficialConformanceVerificationStatus.Verified,
            failed: failed);

        var evaluation = AssuranceAssessmentEngine.Evaluate(result, AssuranceProfileCatalog.Inventory);

        evaluation.Assessments.ExternalConformance.Status.Should().Be(expected);
        evaluation.Assessments.ExternalConformance.Failed.Should().Be(failed);
    }

    [Fact]
    public void Evaluate_ErrorHandlingCoverageDebt_ShouldDowngradeOperationsOnly()
    {
        var result = CreateCompleteResult();
        result.Evidence.Coverage.Add(new ValidationCoverageDeclaration
        {
            LayerId = "error-handling",
            Scope = "invalid-request",
            Status = ValidationCoverageStatus.Inconclusive,
            ObservedOutcome = ValidationOutcome.Inconclusive,
            Lane = EvaluationLane.Baseline,
            Confidence = EvidenceConfidenceLevel.Low
        });

        var evaluation = AssuranceAssessmentEngine.Evaluate(result, AssuranceProfileCatalog.Inventory);

        evaluation.Assessments.SecurityAssurance.Status.Should().Be(SecurityAssuranceStatus.Verified);
        evaluation.Assessments.OperationalReadiness.Status.Should().Be(OperationalReadinessStatus.Incomplete);
    }

    [Fact]
    public void Evaluate_CertificateThresholds_ShouldTreatExactBoundaryAsSatisfied()
    {
        var result = CreateCompleteResult();
        var profile = CertificateProfile(minimumCoverage: 1, minimumConfidence: 1);

        var evaluation = AssuranceAssessmentEngine.Evaluate(result, profile, new AssuranceEvaluationContext
        {
            ExecutionMode = ExecutionMode.Standard
        });

        evaluation.CertificateEligibility.Status.Should().Be(CertificateEligibilityStatus.Eligible);
    }

    [Fact]
    public void Evaluate_CertificateCoverageAndConfidenceDebt_ShouldNameBothReasons()
    {
        var result = CreateCompleteResult();
        result.Evidence.Coverage.Add(new ValidationCoverageDeclaration
        {
            LayerId = "resource-catalog",
            Scope = "resources/list",
            Status = ValidationCoverageStatus.Inconclusive,
            ObservedOutcome = ValidationOutcome.Inconclusive,
            Lane = EvaluationLane.Baseline,
            Confidence = EvidenceConfidenceLevel.Low
        });
        var profile = CertificateProfile(minimumCoverage: 1, minimumConfidence: 1);

        var evaluation = AssuranceAssessmentEngine.Evaluate(result, profile, new AssuranceEvaluationContext
        {
            ExecutionMode = ExecutionMode.Standard
        });

        evaluation.CertificateEligibility.Status.Should().Be(CertificateEligibilityStatus.ReviewRequired);
        evaluation.CertificateEligibility.Reasons.Should().Contain(reason => reason.StartsWith("Evidence coverage", StringComparison.Ordinal));
        evaluation.CertificateEligibility.Reasons.Should().Contain(reason => reason.StartsWith("Evidence confidence", StringComparison.Ordinal));
    }

    [Fact]
    public void Evaluate_CertificateReasons_ShouldBeCultureInvariant()
    {
        var result = CreateCompleteResult();
        result.Evidence.Coverage.Add(new ValidationCoverageDeclaration
        {
            LayerId = "resource-catalog",
            Scope = "resources/list",
            Status = ValidationCoverageStatus.Inconclusive,
            ObservedOutcome = ValidationOutcome.Inconclusive,
            Lane = EvaluationLane.Baseline,
            Confidence = EvidenceConfidenceLevel.Low
        });
        var profile = CertificateProfile(minimumCoverage: 1, minimumConfidence: 1);
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");

            var evaluation = AssuranceAssessmentEngine.Evaluate(result, profile, new AssuranceEvaluationContext
            {
                ExecutionMode = ExecutionMode.Standard
            });

            evaluation.CertificateEligibility.Reasons.Should().Contain("Evidence coverage 75.0% is below 100.0%.");
            evaluation.CertificateEligibility.Reasons.Should().Contain("Evidence confidence 83.8% is below 100.0%.");
            evaluation.CertificateEligibility.Reasons.Should().OnlyContain(reason => !reason.Contains(" %", StringComparison.Ordinal));
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Fact]
    public void Evaluate_RequiredAttestation_ShouldNameMissingAttestationOnly()
    {
        var result = CreateCompleteResult();
        var profile = CertificateProfile(requiresAttestation: true);

        var evaluation = AssuranceAssessmentEngine.Evaluate(result, profile, new AssuranceEvaluationContext
        {
            ExecutionMode = ExecutionMode.Standard
        });

        evaluation.CertificateEligibility.Status.Should().Be(CertificateEligibilityStatus.ReviewRequired);
        evaluation.CertificateEligibility.Reasons.Should().ContainSingle("A verified signed attestation is required.");
    }

    [Fact]
    public void Evaluate_HarnessConsistency_ShouldTakePrecedenceOverLegacyScalar()
    {
        var result = CreateCompleteResult();
        var profile = CertificateProfile(requiresRepeatedRuns: true, minimumConsistency: 0.99);
        var context = new AssuranceEvaluationContext
        {
            ExecutionMode = ExecutionMode.Standard,
            RepeatedRunConsistency = 1,
            ProviderHarness = new ProviderHarnessAssessment
            {
                Status = ProviderHarnessStatus.Verified,
                AuthorizationVerified = true,
                TargetIdentitySha256 = new string('a', 64),
                AcceptedRunCount = 3,
                RepeatedRunConsistency = 0.98
            }
        };

        var evaluation = AssuranceAssessmentEngine.Evaluate(result, profile, context);

        evaluation.CertificateEligibility.Status.Should().Be(CertificateEligibilityStatus.ReviewRequired);
        evaluation.CertificateEligibility.Reasons.Should().Contain(reason => reason.StartsWith("Repeated-run consistency", StringComparison.Ordinal));
    }

    [Fact]
    public void Evaluate_LegacyConsistencyAtExactThreshold_ShouldSatisfyThresholdButStillRequireHarness()
    {
        var result = CreateCompleteResult();
        var profile = CertificateProfile(requiresRepeatedRuns: true, minimumConsistency: 0.99);

        var evaluation = AssuranceAssessmentEngine.Evaluate(result, profile, new AssuranceEvaluationContext
        {
            ExecutionMode = ExecutionMode.Standard,
            RepeatedRunConsistency = 0.99
        });

        evaluation.CertificateEligibility.Reasons.Should().ContainSingle(reason => reason.Contains("provider harness", StringComparison.Ordinal));
    }

    [Fact]
    public void Evaluate_CompleteProviderEvidence_ShouldBeEligible()
    {
        var result = CreateCompleteResult();
        result.Assessments.OfficialConformance = CreateOfficialConformance(OfficialConformanceVerificationStatus.Verified);
        var context = new AssuranceEvaluationContext
        {
            ExecutionMode = ExecutionMode.Standard,
            HasSignedAttestation = true,
            ProviderHarness = new ProviderHarnessAssessment
            {
                Status = ProviderHarnessStatus.Verified,
                AuthorizationVerified = true,
                TargetIdentitySha256 = new string('a', 64),
                AcceptedRunCount = 3,
                RepeatedRunConsistency = 1
            }
        };

        var evaluation = AssuranceAssessmentEngine.Evaluate(result, AssuranceProfileCatalog.Provider, context);

        evaluation.CertificateEligibility.Status.Should().Be(CertificateEligibilityStatus.Eligible);
        evaluation.CertificateEligibility.Reasons.Should().BeEmpty();
    }

    [Theory]
    [InlineData(OfficialConformanceVerificationStatus.Conflict)]
    [InlineData(OfficialConformanceVerificationStatus.Incomplete)]
    public void Evaluate_NonVerifiedOfficialConformance_ShouldNotBeEligible(
        OfficialConformanceVerificationStatus verificationStatus)
    {
        var result = CreateCompleteResult();
        result.Assessments.OfficialConformance = CreateOfficialConformance(verificationStatus);
        var context = CompleteProviderContext();

        var evaluation = AssuranceAssessmentEngine.Evaluate(result, AssuranceProfileCatalog.Provider, context);

        evaluation.CertificateEligibility.Status.Should().Be(
            verificationStatus == OfficialConformanceVerificationStatus.Conflict
                ? CertificateEligibilityStatus.Ineligible
                : CertificateEligibilityStatus.ReviewRequired);
        if (verificationStatus == OfficialConformanceVerificationStatus.Conflict)
        {
            evaluation.CertificateEligibility.Reasons.Should().Contain(new[]
            {
                "Verified official conformance evidence is required.",
                "Official and native evidence conflicts remain unresolved."
            });
        }
        else
        {
            evaluation.CertificateEligibility.Reasons.Should().Contain("Verified official conformance evidence is required.");
        }
    }

    [Fact]
    public void Evaluate_InvalidProviderHarness_ShouldBeIneligible()
    {
        var result = CreateCompleteResult();
        result.Assessments.OfficialConformance = CreateOfficialConformance(OfficialConformanceVerificationStatus.Verified);
        var context = CompleteProviderContext(ProviderHarnessStatus.Invalid);

        var evaluation = AssuranceAssessmentEngine.Evaluate(result, AssuranceProfileCatalog.Provider, context);

        evaluation.CertificateEligibility.Status.Should().Be(CertificateEligibilityStatus.Ineligible);
    }

    [Fact]
    public void Evaluate_MissingRequiredExtension_ShouldRequireReview()
    {
        var result = CreateCompleteResult();
        var evaluation = AssuranceAssessmentEngine.Evaluate(
            result,
            CertificateProfile(),
            new AssuranceEvaluationContext
            {
                ExecutionMode = ExecutionMode.Standard,
                RequiredExtensionIds = ["io.modelcontextprotocol/ui"]
            });

        evaluation.CertificateEligibility.Status.Should().Be(CertificateEligibilityStatus.ReviewRequired);
        evaluation.CertificateEligibility.Reasons.Should().ContainSingle()
            .Which.Should().Be("Required extension 'io.modelcontextprotocol/ui' was not assessed.");
    }

    [Theory]
    [InlineData(ValidationVerdict.Reject, CertificateEligibilityStatus.Ineligible)]
    [InlineData(ValidationVerdict.ReviewRequired, CertificateEligibilityStatus.ReviewRequired)]
    [InlineData(ValidationVerdict.Trusted, CertificateEligibilityStatus.Eligible)]
    public void Evaluate_RequiredExtensionOutcome_ShouldAffectCertificate(
        ValidationVerdict extensionVerdict,
        CertificateEligibilityStatus expected)
    {
        var result = CreateCompleteResult();
        result.Assessments.Extensions.Add(new ExtensionAssessment
        {
            ExtensionId = "io.modelcontextprotocol/ui",
            Stage = ExtensionLifecycleStage.Stable,
            SpecificationRevision = "2026-01-26",
            SpecificationSha256 = new string('e', 64),
            Verdict = extensionVerdict,
            Coverage = [ValidationCoverageFactory.FromTestStatus("extension-fixture", "required-extension", extensionVerdict switch
            {
                ValidationVerdict.Trusted => TestStatus.Passed,
                ValidationVerdict.Reject => TestStatus.Failed,
                _ => TestStatus.Inconclusive
            })],
            Findings = extensionVerdict switch
            {
                ValidationVerdict.Reject =>
                [
                    new ValidationFinding
                    {
                        RuleId = "EXT.REQUIRED",
                        Source = ValidationRuleSource.Spec,
                        Component = "extension",
                        Severity = ValidationFindingSeverity.High,
                        Summary = "Required extension behavior failed."
                    }
                ],
                ValidationVerdict.ReviewRequired =>
                [
                    new ValidationFinding
                    {
                        RuleId = "EXT.REVIEW",
                        Source = ValidationRuleSource.Heuristic,
                        Component = "extension",
                        Severity = ValidationFindingSeverity.Medium,
                        Summary = "Extension behavior requires review."
                    }
                ],
                _ => []
            }
        });

        var evaluation = AssuranceAssessmentEngine.Evaluate(
            result,
            CertificateProfile(),
            new AssuranceEvaluationContext
            {
                ExecutionMode = ExecutionMode.Standard,
                RequiredExtensionIds = ["io.modelcontextprotocol/ui"]
            });

        evaluation.CertificateEligibility.Status.Should().Be(expected);
        if (extensionVerdict == ValidationVerdict.Trusted)
        {
            evaluation.CertificateEligibility.Reasons.Should().BeEmpty();
            return;
        }

        evaluation.CertificateEligibility.Reasons.Should().ContainSingle().Which.Should().Be(extensionVerdict switch
        {
            ValidationVerdict.Reject => "Required extension 'io.modelcontextprotocol/ui' was rejected.",
            ValidationVerdict.ReviewRequired => "Required extension 'io.modelcontextprotocol/ui' is Incomplete.",
            _ => throw new InvalidOperationException("Unexpected extension verdict.")
        }, "non-ready extension outcomes must retain stable machine-readable reasons");
    }

    [Theory]
    [InlineData(1, 0, CertificateEligibilityStatus.ReviewRequired)]
    [InlineData(0, 1, CertificateEligibilityStatus.Ineligible)]
    public void Evaluate_Waivers_ShouldPreventProviderEligibility(
        int activeWaivers,
        int invalidWaivers,
        CertificateEligibilityStatus expected)
    {
        var evaluation = AssuranceAssessmentEngine.Evaluate(
            CreateCompleteResult(),
            CertificateProfile(),
            new AssuranceEvaluationContext
            {
                ExecutionMode = ExecutionMode.Standard,
                ActiveWaiverCount = activeWaivers,
                InvalidWaiverCount = invalidWaivers
            });

        evaluation.CertificateEligibility.Status.Should().Be(expected);
        evaluation.CertificateEligibility.Reasons.Should().ContainSingle().Which.Should().Be(
            activeWaivers > 0
                ? $"Provider certification does not permit {activeWaivers} applied waiver(s)."
                : $"Provider certification found {invalidWaivers} expired or invalid waiver(s).");
    }

    [Fact]
    public void Evaluate_SafeModeForProvider_ShouldRequireReview()
    {
        var result = CreateCompleteResult();
        result.Assessments.OfficialConformance = CreateOfficialConformance(OfficialConformanceVerificationStatus.Verified);
        var context = CompleteProviderContext();
        context = new AssuranceEvaluationContext
        {
            ExecutionMode = ExecutionMode.Safe,
            HasSignedAttestation = context.HasSignedAttestation,
            ProviderHarness = context.ProviderHarness
        };

        var evaluation = AssuranceAssessmentEngine.Evaluate(result, AssuranceProfileCatalog.Provider, context);

        evaluation.CertificateEligibility.Status.Should().Be(CertificateEligibilityStatus.ReviewRequired);
        evaluation.CertificateEligibility.Reasons.Should().Contain("Execution mode Safe is below required mode Standard.");
    }

    private static ValidationResult CreateResultWithCoveredBaseline()
    {
        var result = new ValidationResult();
        result.Evidence.Coverage.Add(new ValidationCoverageDeclaration
        {
            LayerId = "protocol-core",
            Scope = "json-rpc",
            Status = ValidationCoverageStatus.Covered,
            ObservedOutcome = ValidationOutcome.Succeeded,
            Lane = EvaluationLane.Baseline,
            Confidence = EvidenceConfidenceLevel.High
        });
        return result;
    }

    private static ValidationResult CreateCompleteResult()
    {
        var result = new ValidationResult
        {
            ProtocolCompliance = new ComplianceTestResult(),
            SecurityTesting = new SecurityTestResult
            {
                AttackSimulations =
                [
                    new AttackSimulationResult
                    {
                        AttackVector = "security-control",
                        Description = "Deterministic security control passed.",
                        Authority = ValidationRuleSource.Guideline,
                        Outcome = ValidationOutcome.Succeeded,
                        Gate = GateOutcome.Note,
                        Severity = ValidationFindingSeverity.Info,
                        DefenseSuccessful = true
                    }
                ]
            },
            PerformanceTesting = new PerformanceTestResult()
        };
        result.Assessments.OfficialConformance = CreateOfficialConformance(OfficialConformanceVerificationStatus.Verified);
        AddCovered(result, "protocol-core", "json-rpc");
        AddCovered(result, "security-controls", "authentication");
        AddCovered(result, "performance", "repeated-measurements");
        return result;
    }

    private static void AddCovered(ValidationResult result, string layer, string scope) =>
        result.Evidence.Coverage.Add(new ValidationCoverageDeclaration
        {
            LayerId = layer,
            Scope = scope,
            Status = ValidationCoverageStatus.Covered,
            ObservedOutcome = ValidationOutcome.Succeeded,
            Lane = EvaluationLane.Baseline,
            Confidence = EvidenceConfidenceLevel.High
        });

    private static ExtensionAssessment CreateExtension(
        string extensionId,
        IReadOnlyList<ValidationFinding>? findings = null) => new()
    {
        ExtensionId = extensionId,
        Stage = ExtensionLifecycleStage.Stable,
        SpecificationRevision = "1",
        SpecificationSha256 = new string('e', 64),
        Verdict = ValidationVerdict.Trusted,
        Findings = findings ?? [],
        EvidenceSummary = new EvidenceCoverageSummary
        {
            TotalDeclarations = 1,
            ApplicableDeclarations = 1,
            Covered = 1,
            ConfidenceLevel = EvidenceConfidenceLevel.High
        }
    };

    private static OfficialConformanceAssessment CreateOfficialConformance(
        OfficialConformanceVerificationStatus status,
        IReadOnlyList<OfficialConformanceScenarioAssessment>? scenarios = null,
        int failed = 0) => new()
    {
        VerificationStatus = status,
        Runner = new OfficialConformanceRunnerIdentity
        {
            PackageName = "official",
            Version = "1.0.0",
            SourceCommit = new string('a', 40),
            PackageSha256 = new string('b', 64)
        },
        RequirementSet = new OfficialConformanceRequirementSet
        {
            Revision = "2026-07-28",
            Sha256 = new string('c', 64)
        },
        Target = new OfficialConformanceTargetIdentity
        {
            IdentitySha256 = new string('d', 64),
            Transport = "http",
            ProtocolVersion = "2026-07-28"
        },
        Failed = failed,
        Scenarios = scenarios ??
        [
            new OfficialConformanceScenarioAssessment
            {
                ScenarioId = "required-core",
                ScoringDisposition = OfficialConformanceScoringDisposition.Required,
                Checks =
                [
                    new OfficialConformanceCheckAssessment
                    {
                        Id = "required-success",
                        Name = "required-success",
                        Description = "Required official check passed.",
                        Status = OfficialConformanceCheckStatus.Success
                    }
                ]
            }
        ]
    };

    private static AssuranceEvaluationContext CompleteProviderContext(
        ProviderHarnessStatus harnessStatus = ProviderHarnessStatus.Verified) => new()
    {
        ExecutionMode = ExecutionMode.Standard,
        HasSignedAttestation = true,
        ProviderHarness = new ProviderHarnessAssessment
        {
            Status = harnessStatus,
            AuthorizationVerified = harnessStatus == ProviderHarnessStatus.Verified,
            TargetIdentitySha256 = new string('a', 64),
            AcceptedRunCount = 3,
            RepeatedRunConsistency = 1
        }
    };

    private static AssuranceProfileManifest CertificateProfile(
        double minimumCoverage = 0,
        double minimumConfidence = 0,
        bool requiresAttestation = false,
        bool requiresRepeatedRuns = false,
        double minimumConsistency = 0) => new()
    {
        ProfileId = "test-certificate",
        Revision = "test",
        MinimumExecutionMode = ExecutionMode.Standard,
        MinimumEvidenceCoverageRatio = minimumCoverage,
        MinimumEvidenceConfidenceRatio = minimumConfidence,
        MinimumRepeatedRunConsistency = minimumConsistency,
        RequiresSignedAttestation = requiresAttestation,
        RequiresRepeatedRuns = requiresRepeatedRuns,
        CertificateEligible = true
    };
}