using System.Text.Json.Nodes;
using System.Xml.Linq;
using FluentAssertions;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Infrastructure.Services.Reporting;

namespace Mcp.Benchmark.Tests.Unit.Reporting;

public sealed class OfficialConformanceConflictReportTests
{
    [Fact]
    public void Reports_ShouldRetainEvidenceConflictAsReviewRequiredExternalDecision()
    {
        var result = CreateResult();
        var renderer = new ValidationReportRenderer();

        new MarkdownReportGenerator().GenerateReport(result).Should().Contain("Evidence Conflicts").And.Contain("Review required");
        renderer.GenerateHtmlReport(result, result.ValidationConfig.Reporting, verbose: false).Should().Contain("Review required:");
        XDocument.Parse(renderer.GenerateXmlReport(result, verbose: false)).Descendants("Conflict").Should().ContainSingle();
        JsonNode.Parse(renderer.GenerateSarifReport(result))!.ToJsonString().Should().Contain("EvidenceConflict");
        XDocument.Parse(renderer.GenerateJunitReport(result)).Descendants("testcase")
            .Should().Contain(element => element.Attribute("classname") != null &&
                element.Attribute("classname")!.Value == "official-conformance.evidence-conflict");
    }

    private static ValidationResult CreateResult()
    {
        var decision = new DecisionRecord
        {
            DecisionId = "external:evidence-conflict/initialize/check-1",
            RelatedEvidenceIds = ["native/evidence", "official/evidence"],
            RuleId = "check-1",
            Lane = EvaluationLane.ExternalConformance,
            Authority = ValidationRuleSource.Spec,
            Origin = EvidenceOrigin.ExternalConformanceCheck,
            Gate = GateOutcome.ReviewRequired,
            Severity = ValidationFindingSeverity.High,
            Category = "EvidenceConflict",
            Component = "initialize",
            Summary = "Official and native evidence disagree."
        };
        var assessment = new OfficialConformanceAssessment
        {
            VerificationStatus = OfficialConformanceVerificationStatus.Conflict,
            SourceEvidenceSha256 = new string('a', 64),
            Runner = new OfficialConformanceRunnerIdentity
            {
                PackageName = "official",
                Version = "1.0.0",
                SourceCommit = new string('b', 40),
                PackageSha256 = new string('c', 64)
            },
            RequirementSet = new OfficialConformanceRequirementSet
            {
                Revision = "2026-07-28",
                Sha256 = new string('d', 64)
            },
            Target = new OfficialConformanceTargetIdentity
            {
                IdentitySha256 = new string('e', 64),
                Transport = "http",
                ProtocolVersion = "2026-07-28"
            }
        };
        assessment.Conflicts.Add(new OfficialConformanceConflict
        {
            ConflictId = "evidence-conflict/initialize/check-1",
            Component = "initialize",
            Summary = "Official and native evidence disagree.",
            EvidenceIds = decision.RelatedEvidenceIds
        });
        assessment.Decisions.Add(decision);
        var result = new ValidationResult();
        result.Assessments.OfficialConformance = assessment;
        return result;
    }
}