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
                EvidenceIds = ["native/evidence"],
                Summary = "Native validation failed.",
                Metadata = new Dictionary<string, string> { ["ruleId"] = "MCP-REQUIRED-1" }
            }
        };

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
                Metadata = new Dictionary<string, string> { ["ruleId"] = "OTHER-RULE" }
            }
        };

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