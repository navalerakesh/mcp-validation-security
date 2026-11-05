using System.Text.Json.Nodes;
using System.Xml.Linq;
using FluentAssertions;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Infrastructure.Services.Reporting;

namespace Mcp.Benchmark.Tests.Unit.Services;

public sealed class OfficialConformanceReportParityTests
{
    [Fact]
    public void Reports_ShouldProjectOfficialConformanceAndExtensionReadinessFromCanonicalResult()
    {
        var result = CreateResult();
        var renderer = new ValidationReportRenderer();

        var markdown = new MarkdownReportGenerator().GenerateReport(result);
        var html = renderer.GenerateHtmlReport(result, result.ValidationConfig.Reporting, verbose: false);
        var xml = XDocument.Parse(renderer.GenerateXmlReport(result, verbose: false));
        var sarif = JsonNode.Parse(renderer.GenerateSarifReport(result))!;
        var junit = XDocument.Parse(renderer.GenerateJunitReport(result));

        markdown.Should().Contain("Official Conformance").And.Contain("Extension Readiness");
        markdown.Should().Contain("0.2.0-alpha.12").And.Contain("io.modelcontextprotocol/apps");
        html.Should().Contain("id=\"official-conformance-title\"").And.Contain("id=\"extension-readiness-title\"");
        html.Should().Contain("0.2.0-alpha.12").And.Contain("io.modelcontextprotocol/apps");
        xml.Root!.Element("OfficialConformance")!.Attribute("verificationStatus")!.Value.Should().Be("Verified");
        xml.Root.Element("ExtensionReadiness")!.Element("Extension")!.Attribute("id")!.Value.Should().Be("io.modelcontextprotocol/apps");

        var sarifText = sarif.ToJsonString();
        sarifText.Should().Contain("required-failure");
        sarifText.Should().NotContain("required-success");
        junit.Descendants("testsuite").Select(element => element.Attribute("name")?.Value)
            .Should().Contain(["official-conformance", "extension-readiness"]);
        junit.Descendants("testsuite").Single(element => element.Attribute("name")?.Value == "official-conformance")
            .Attribute("failures")!.Value.Should().Be("1");
    }

    private static ValidationResult CreateResult()
    {
        var result = new ValidationResult
        {
            ValidationId = "report-parity",
            StartTime = DateTime.Parse("2026-11-04T10:00:00Z").ToUniversalTime(),
            EndTime = DateTime.Parse("2026-11-04T10:00:01Z").ToUniversalTime(),
            OverallStatus = ValidationStatus.Passed,
            ServerConfig = new McpServerConfig { Endpoint = "https://example.test/mcp", Transport = "http" }
        };
        result.Assessments.OfficialConformance = new OfficialConformanceAssessment
        {
            VerificationStatus = OfficialConformanceVerificationStatus.Verified,
            SourceEvidenceSha256 = new string('f', 64),
            Runner = new OfficialConformanceRunnerIdentity
            {
                PackageName = "@modelcontextprotocol/conformance",
                Version = "0.2.0-alpha.12",
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
            RunState = OfficialConformanceRunState.Completed,
            Passed = 1,
            Failed = 1,
            EvidenceSummary = new EvidenceCoverageSummary
            {
                TotalDeclarations = 2,
                ApplicableDeclarations = 2,
                Covered = 2,
                EvidenceCoverageRatio = 1,
                EvidenceConfidenceRatio = 1,
                ConfidenceLevel = EvidenceConfidenceLevel.High
            },
            Scenarios =
            [
                new OfficialConformanceScenarioAssessment
                {
                    ScenarioId = "server-initialize",
                    ScoringDisposition = OfficialConformanceScoringDisposition.Required,
                    Passed = 1,
                    Failed = 1,
                    Checks =
                    [
                        CreateCheck("required-success", OfficialConformanceCheckStatus.Success),
                        CreateCheck("required-failure", OfficialConformanceCheckStatus.Failure)
                    ]
                }
            ]
        };
        result.Assessments.Extensions.Add(new ExtensionAssessment
        {
            ExtensionId = "io.modelcontextprotocol/apps",
            Stage = ExtensionLifecycleStage.Stable,
            SpecificationRevision = "2026-01-26",
            SpecificationSha256 = new string('e', 64),
            Verdict = ValidationVerdict.ReviewRequired,
            EvidenceSummary = new EvidenceCoverageSummary
            {
                TotalDeclarations = 1,
                ApplicableDeclarations = 1,
                Inconclusive = 1,
                EvidenceCoverageRatio = 0,
                EvidenceConfidenceRatio = 0,
                ConfidenceLevel = EvidenceConfidenceLevel.Low
            }
        });
        return result;
    }

    private static OfficialConformanceCheckAssessment CreateCheck(
        string id,
        OfficialConformanceCheckStatus status) => new()
        {
            Id = id,
            Name = id,
            Description = $"Official check {id}",
            Status = status
        };
}