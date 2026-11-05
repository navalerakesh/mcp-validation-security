using FluentAssertions;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Services;

namespace Mcp.Benchmark.Tests.Unit.Services;

public sealed class OfficialConformanceReconcilerTests
{
    [Fact]
    public void Reconcile_ContradictoryStableIdentity_ShouldRequireReviewInExternalLane()
    {
        var assessment = CreateAssessment(OfficialConformanceCheckStatus.Success);
        var native = new VerdictAssessment();
        native.TriggeredDecisions.Add(new DecisionRecord
        {
            DecisionId = "baseline:native-failure",
            RelatedEvidenceIds = ["native/evidence"],
            RuleId = "MCP-REQUIRED-1",
            Lane = EvaluationLane.Baseline,
            Authority = ValidationRuleSource.Spec,
            Origin = EvidenceOrigin.DeterministicObservation,
            Gate = GateOutcome.Reject,
            Severity = ValidationFindingSeverity.High,
            Category = "Protocol",
            Component = "initialize",
            Summary = "Native validation failed."
        });

        var result = OfficialConformanceReconciler.Reconcile(assessment, native);

        result.Conflicts.Should().ContainSingle();
        result.Decisions.Should().ContainSingle().Which.Should().Match<DecisionRecord>(decision =>
            decision.Lane == EvaluationLane.ExternalConformance &&
            decision.Gate == GateOutcome.ReviewRequired &&
            decision.Category == "EvidenceConflict" &&
            decision.Origin == EvidenceOrigin.ExternalConformanceCheck);
    }

    [Fact]
    public void Reconcile_AgreementOrUnrelatedIdentity_ShouldNotInventConflict()
    {
        var assessment = CreateAssessment(OfficialConformanceCheckStatus.Failure);
        var native = new VerdictAssessment();
        native.TriggeredDecisions.Add(new DecisionRecord
        {
            DecisionId = "baseline:other",
            RuleId = "OTHER-RULE",
            Lane = EvaluationLane.Baseline,
            Authority = ValidationRuleSource.Spec,
            Origin = EvidenceOrigin.DeterministicObservation,
            Gate = GateOutcome.Reject,
            Severity = ValidationFindingSeverity.High,
            Category = "Protocol",
            Component = "initialize",
            Summary = "Other failure."
        });

        OfficialConformanceReconciler.Reconcile(assessment, native).Conflicts.Should().BeEmpty();
    }

    private static OfficialConformanceAssessment CreateAssessment(OfficialConformanceCheckStatus status) => new()
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
                Checks =
                [
                    new OfficialConformanceCheckAssessment
                    {
                        Id = "MCP-REQUIRED-1",
                        Name = "Required check",
                        Description = "Required behavior.",
                        Status = status
                    }
                ]
            }
        ]
    };
}