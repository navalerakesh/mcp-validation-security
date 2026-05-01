using FluentAssertions;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Services;
using Xunit;

namespace Mcp.Benchmark.Tests.Unit.Core;

public class ReportSeverityNormalizerTests
{
    [Theory]
    [InlineData(ValidationFindingSeverity.Info, ReportSeverity.Info, "note")]
    [InlineData(ValidationFindingSeverity.Low, ReportSeverity.Low, "note")]
    [InlineData(ValidationFindingSeverity.Medium, ReportSeverity.Medium, "warning")]
    [InlineData(ValidationFindingSeverity.High, ReportSeverity.High, "error")]
    [InlineData(ValidationFindingSeverity.Critical, ReportSeverity.Critical, "error")]
    public void FromValidationFindingSeverity_ShouldMapToCanonicalSeverity(ValidationFindingSeverity severity, ReportSeverity expected, string expectedSarifLevel)
    {
        var normalized = ReportSeverityNormalizer.From(severity);

        normalized.Should().Be(expected);
        ReportSeverityNormalizer.ToSarifLevel(normalized).Should().Be(expectedSarifLevel);
    }

    [Theory]
    [InlineData(ViolationSeverity.Low, ReportSeverity.Low)]
    [InlineData(ViolationSeverity.Medium, ReportSeverity.Medium)]
    [InlineData(ViolationSeverity.High, ReportSeverity.High)]
    [InlineData(ViolationSeverity.Critical, ReportSeverity.Critical)]
    public void FromViolationSeverity_ShouldMapToCanonicalSeverity(ViolationSeverity severity, ReportSeverity expected)
    {
        ReportSeverityNormalizer.From(severity).Should().Be(expected);
    }

    [Theory]
    [InlineData(VulnerabilitySeverity.Informational, ReportSeverity.Info)]
    [InlineData(VulnerabilitySeverity.Low, ReportSeverity.Low)]
    [InlineData(VulnerabilitySeverity.Medium, ReportSeverity.Medium)]
    [InlineData(VulnerabilitySeverity.High, ReportSeverity.High)]
    [InlineData(VulnerabilitySeverity.Critical, ReportSeverity.Critical)]
    public void FromVulnerabilitySeverity_ShouldMapToCanonicalSeverity(VulnerabilitySeverity severity, ReportSeverity expected)
    {
        ReportSeverityNormalizer.From(severity).Should().Be(expected);
    }

    [Theory]
    [InlineData(ContentRiskLevel.None, ReportSeverity.Info)]
    [InlineData(ContentRiskLevel.Low, ReportSeverity.Low)]
    [InlineData(ContentRiskLevel.Medium, ReportSeverity.Medium)]
    [InlineData(ContentRiskLevel.High, ReportSeverity.High)]
    public void FromContentRiskLevel_ShouldMapToCanonicalSeverity(ContentRiskLevel riskLevel, ReportSeverity expected)
    {
        ReportSeverityNormalizer.From(riskLevel).Should().Be(expected);
    }

    [Theory]
    [InlineData(GateOutcome.Reject, ValidationFindingSeverity.High, ReportPriority.Critical)]
    [InlineData(GateOutcome.ReviewRequired, ValidationFindingSeverity.High, ReportPriority.High)]
    [InlineData(GateOutcome.CoverageDebt, ValidationFindingSeverity.Medium, ReportPriority.Medium)]
    [InlineData(GateOutcome.Note, ValidationFindingSeverity.Low, ReportPriority.Low)]
    [InlineData(GateOutcome.Note, ValidationFindingSeverity.Info, ReportPriority.Info)]
    public void PriorityFromDecision_ShouldCombineGateAndSeverity(GateOutcome gate, ValidationFindingSeverity severity, ReportPriority expected)
    {
        var decision = new DecisionRecord
        {
            DecisionId = "decision-1",
            Lane = EvaluationLane.Baseline,
            Authority = ValidationRuleSource.Spec,
            Origin = EvidenceOrigin.DeterministicObservation,
            Gate = gate,
            Severity = severity,
            Category = "Protocol",
            Component = "initialize",
            Summary = "Decision summary."
        };

        ReportSeverityNormalizer.PriorityFrom(decision).Should().Be(expected);
    }
}
