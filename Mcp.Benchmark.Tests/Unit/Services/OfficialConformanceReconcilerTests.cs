using FluentAssertions;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Services;

namespace Mcp.Benchmark.Tests.Unit.Services;

public sealed class OfficialConformanceReconcilerTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Reconcile_ContradictoryStableIdentity_ShouldRequireReviewInExternalLane(bool indexed)
    {
        var assessment = CreateAssessment(OfficialConformanceCheckStatus.Success, indexed, references: true);
        var native = new List<AssuranceClaim>
        {
            new()
            {
                ClaimId = "protocol:mcp-required-1",
                Subject = "initialize",
                Dimension = AssuranceDimension.CoreProtocol,
                RequirementLevel = AssuranceRequirementLevel.Must,
                Authority = AssuranceAuthority.OfficialSpecification,
                Source = new AssuranceSourceReference { Revision = "2026-07-28" },
                Outcome = AssuranceClaimOutcome.Violated,
                EvidenceIds = ["native/evidence", "Z-native", "native/evidence"],
                Summary = "Native validation failed.",
                Metadata = new Dictionary<string, string> { ["ruleId"] = "MCP-REQUIRED-1" }
            }
        };

        var result = OfficialConformanceReconciler.Reconcile(assessment, native);

        result.Conflicts.Should().HaveCount(indexed ? 2 : 1);
        result.Conflicts.Select(conflict => conflict.ConflictId).Should().OnlyHaveUniqueItems();
        result.Decisions.Select(decision => decision.DecisionId).Should().OnlyHaveUniqueItems();
        result.Decisions.Should().HaveCount(indexed ? 2 : 1).And.OnlyContain(decision =>
            decision.Lane == EvaluationLane.ExternalConformance &&
            decision.Gate == GateOutcome.ReviewRequired &&
            decision.Category == "EvidenceConflict" &&
            decision.Origin == EvidenceOrigin.ExternalConformanceCheck);
        result.Conflicts.SelectMany(conflict => conflict.EvidenceIds).Distinct().Should().HaveCount(indexed ? 4 : 3);
        if (!indexed) result.Conflicts[0].ConflictId.Should().Be("evidence-conflict/initialize/MCP-REQUIRED-1");
        for (var index = 0; index < result.Decisions.Count; index++)
        {
            var decision = result.Decisions[index];
            var conflict = result.Conflicts[index];
            var evidenceId = indexed ? $"official-conformance/initialize/check/{index}" : "official-conformance/initialize/MCP-REQUIRED-1";
            conflict.EvidenceIds.Should().Equal("Z-native", "native/evidence", evidenceId);
            conflict.Component.Should().Be("initialize");
            conflict.Summary.Should().Contain("MCP-REQUIRED-1").And.Contain("protocol:mcp-required-1");
            decision.DecisionId.Should().Be($"external:{conflict.ConflictId}");
            decision.RelatedEvidenceIds.Should().Equal(conflict.EvidenceIds);
            decision.RuleId.Should().Be("MCP-REQUIRED-1");
            decision.RuleRevision.Should().Be("2026-07-28");
            decision.Authority.Should().Be(ValidationRuleSource.Spec);
            decision.Severity.Should().Be(ValidationFindingSeverity.High);
            decision.Component.Should().Be("initialize");
            decision.Summary.Should().Contain("disagree").And.Contain("review");
            decision.SpecReference.Should().Be("https://example.test/first-spec");
            decision.ImpactAreas.Should().BeEquivalentTo(new[] { ImpactArea.CoverageIntegrity, ImpactArea.ProtocolInteroperability });
            decision.Metadata.Should().BeEquivalentTo(new Dictionary<string, string>
            { ["officialCheckId"] = "MCP-REQUIRED-1", ["nativeClaimId"] = "protocol:mcp-required-1" });
        }
    }

    [Theory]
    [InlineData(AssuranceClaimOutcome.Violated, true)]
    [InlineData(AssuranceClaimOutcome.Error, true)]
    [InlineData(AssuranceClaimOutcome.ReviewRequired, true)]
    [InlineData(AssuranceClaimOutcome.Satisfied, false)]
    [InlineData(AssuranceClaimOutcome.Incomplete, false)]
    [InlineData(AssuranceClaimOutcome.NotApplicable, false)]
    public void Reconcile_ShouldDistinguishNativeOutcomes(AssuranceClaimOutcome outcome, bool conflict)
    {
        var native = new AssuranceClaim
        {
            ClaimId = "native", Subject = "initialize", Dimension = AssuranceDimension.CoreProtocol,
            RequirementLevel = AssuranceRequirementLevel.Must, Authority = AssuranceAuthority.OfficialSpecification,
            Source = new() { Revision = "2026-07-28" }, Outcome = outcome, Summary = "Native observation", Metadata = new() { ["ruleId"] = "MCP-REQUIRED-1" }
        };
        var result = OfficialConformanceReconciler.Reconcile(CreateAssessment(OfficialConformanceCheckStatus.Success), [native]);
        result.Conflicts.Should().HaveCount(conflict ? 1 : 0);
        result.Decisions.Should().HaveCount(conflict ? 1 : 0);
        if (conflict) result.Decisions[0].SpecReference.Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("mcp-required-1")]
    [InlineData("unrelated")]
    public void Reconcile_ShouldRequireExactNonblankNativeRuleIdentity(string? ruleId)
    {
        var native = new AssuranceClaim
        {
            ClaimId = "native", Subject = "initialize", Dimension = AssuranceDimension.CoreProtocol,
            RequirementLevel = AssuranceRequirementLevel.Must, Authority = AssuranceAuthority.OfficialSpecification,
            Source = new() { Revision = "2026-07-28" }, Outcome = AssuranceClaimOutcome.Violated, Summary = "Native observation"
        };
        if (ruleId != null) native.Metadata["ruleId"] = ruleId;
        var assessment = CreateAssessment(OfficialConformanceCheckStatus.Success);
        var result = OfficialConformanceReconciler.Reconcile(assessment, [native]);
        result.Conflicts.Should().BeEmpty();
        result.Decisions.Should().BeEmpty();
        OfficialConformanceReconciler.Reconcile(assessment, null).Decisions.Should().BeEmpty();
        OfficialConformanceReconciler.Reconcile(assessment, []).Conflicts.Should().BeEmpty();
    }

    [Theory]
    [InlineData(OfficialConformanceCheckStatus.Failure)]
    [InlineData(OfficialConformanceCheckStatus.Warning)]
    [InlineData(OfficialConformanceCheckStatus.Skipped)]
    [InlineData(OfficialConformanceCheckStatus.Info)]
    public void Reconcile_NonSuccessOfficialCheck_ShouldNotInventSuccessConflict(OfficialConformanceCheckStatus status)
    {
        var assessment = CreateAssessment(status);
        var native = new List<AssuranceClaim>
        {
            new()
            {
                ClaimId = "protocol:other-rule",
                Subject = "initialize",
                Dimension = AssuranceDimension.CoreProtocol,
                RequirementLevel = AssuranceRequirementLevel.Must,
                Authority = AssuranceAuthority.OfficialSpecification,
                Source = new AssuranceSourceReference { Revision = "2026-07-28" },
                Outcome = AssuranceClaimOutcome.Violated,
                Summary = "Other failure.",
                Metadata = new Dictionary<string, string> { ["ruleId"] = "MCP-REQUIRED-1" }
            }
        };

        var result = OfficialConformanceReconciler.Reconcile(assessment, native);
        result.Conflicts.Should().BeEmpty();
        result.Decisions.Should().BeEmpty();
    }

    private static OfficialConformanceAssessment CreateAssessment(OfficialConformanceCheckStatus status, bool indexed = false, bool references = false) => new()
    {
        VerificationStatus = OfficialConformanceVerificationStatus.Verified,
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
        Scenarios =
        [
            new OfficialConformanceScenarioAssessment
            {
                ScenarioId = "initialize",
                Checks = Enumerable.Range(0, indexed ? 2 : 1).Select(index =>
                    new OfficialConformanceCheckAssessment
                    {
                        Id = "MCP-REQUIRED-1",
                        SourceIndex = indexed ? index : null,
                        Name = "Required check",
                        Description = "Required behavior.",
                        SpecReferences = references ?
                        [
                            new() { Id = "absent", Url = null }, new() { Id = "empty", Url = "" }, new() { Id = "blank", Url = " " },
                            new() { Id = "first", Url = "https://example.test/first-spec" }, new() { Id = "second", Url = "https://example.test/second-spec" }
                        ] : [],
                        Status = status
                    }).ToArray()
            }
        ]
    };
}