using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Services;

namespace Mcp.Benchmark.Tests.Unit.Services;

public sealed class ValidationBaselineEvaluatorTests
{
    [Fact]
    public void Compare_NewBlockingDecision_ShouldReportRegression()
    {
        var baseline = CreateResult("baseline", 90, ValidationVerdict.Trusted);
        var current = CreateResult("current", 80, ValidationVerdict.Reject, "decision:new");

        var comparison = ValidationBaselineEvaluator.Compare(baseline, current);

        comparison.IsRegression.Should().BeTrue();
        comparison.ScoreDelta.Should().Be(-10);
        comparison.NewBlockingDecisions.Should().ContainSingle(change => change.DecisionId == "decision:new");
    }

    [Fact]
    public void Compare_ResolvedDecisionAndImprovedScore_ShouldNotReportRegression()
    {
        var baseline = CreateResult("baseline", 70, ValidationVerdict.Reject, "decision:old");
        var current = CreateResult("current", 90, ValidationVerdict.Trusted);

        var comparison = ValidationBaselineEvaluator.Compare(baseline, current);

        comparison.IsRegression.Should().BeFalse();
        comparison.ResolvedBlockingDecisionIds.Should().Equal("decision:old");
    }

    private static ValidationResult CreateResult(
        string id,
        double score,
        ValidationVerdict verdict,
        string? decisionId = null)
    {
        var assessment = new VerdictAssessment { BaselineVerdict = verdict };
        if (decisionId != null)
        {
            assessment.BlockingDecisions.Add(new DecisionRecord
            {
                DecisionId = decisionId,
                RuleId = "TEST.RULE",
                Lane = EvaluationLane.Baseline,
                Authority = ValidationRuleSource.Heuristic,
                Origin = EvidenceOrigin.DeterministicAggregation,
                Gate = GateOutcome.Reject,
                Severity = ValidationFindingSeverity.Critical,
                Category = "Test",
                Component = "component",
                Summary = "Test blocker."
            });
        }

        return new ValidationResult
        {
            ValidationId = id,
            ComplianceScore = score,
            VerdictAssessment = assessment
        };
    }
}
