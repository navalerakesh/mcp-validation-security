using FluentAssertions;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Infrastructure.Scenarios;

namespace Mcp.Benchmark.Tests.Unit.Scenarios;

public sealed class SkillsCatalogAnalyzerTests
{
    [Fact]
    public void Analyze_CompliantManifest_ShouldPassWithoutFindings()
    {
        var analysis = SkillsCatalogAnalyzer.Analyze([CreatePage("""
            {
              "uri": "skill://pdf-processing/SKILL.md",
              "frontmatter": { "name": "pdf-processing", "description": "Process PDF documents" },
              "resources": [
                {
                  "uri": "skill://pdf-processing/SKILL.md",
                  "digest": "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                  "size": 151
                }
              ]
            }
            """)]);

        analysis.Status.Should().Be(TestStatus.Passed);
        analysis.SkillCount.Should().Be(1);
        analysis.Findings.Should().BeEmpty();
        analysis.Observations.Should().ContainSingle();
    }

    [Fact]
    public void Analyze_RiskyAndMalformedCatalog_ShouldFailSpecAndRetainAdvisories()
    {
        var analysis = SkillsCatalogAnalyzer.Analyze([CreatePage("""
            {
              "uri": "skill://team/not-matching/SKILL.md",
              "frontmatter": {
                "name": "expected-name",
                "description": "Run local automation",
                "allowed-tools": ["shell"]
              },
              "resources": [
                {
                  "uri": "https://other.example/file.md",
                  "digest": "not-a-digest",
                  "size": -1
                }
              ]
            }
            """)]);

        analysis.Status.Should().Be(TestStatus.Failed);
        analysis.Findings.Select(finding => finding.RuleId).Should().Contain([
            Mcp.Benchmark.Core.Models.ValidationFindingRuleIds.SkillsNameUriMismatch,
            Mcp.Benchmark.Core.Models.ValidationFindingRuleIds.SkillsManifestInvalid,
            Mcp.Benchmark.Core.Models.ValidationFindingRuleIds.SkillsRiskBearingFrontmatter
        ]);
        analysis.Findings.Should().OnlyContain(finding => finding.ExcludedFromAggregate);
    }

    [Fact]
    public void Analyze_DynamicAndCollidingNames_ShouldRemainPreviewReviewEvidence()
    {
        var analysis = SkillsCatalogAnalyzer.Analyze([CreatePage("""
            {
              "uri": "skill://team-a/refunds/SKILL.md",
              "frontmatter": { "name": "refunds", "description": "Team A refunds" },
              "resources": "dynamic"
            },
            {
              "uri": "skill://team-b/refunds/SKILL.md",
              "frontmatter": { "name": "refunds", "description": "Team B refunds" },
              "resources": "dynamic"
            }
            """)]);

        analysis.Status.Should().Be(TestStatus.Passed);
        analysis.Findings.Select(finding => finding.RuleId).Should().Contain([
            Mcp.Benchmark.Core.Models.ValidationFindingRuleIds.SkillsDynamicManifestRisk,
            Mcp.Benchmark.Core.Models.ValidationFindingRuleIds.SkillsNameCollision
        ]);
    }

    private static string CreatePage(string entries) => $$"""
        {
          "jsonrpc": "2.0",
          "id": 1,
          "result": {
            "resultType": "complete",
            "skills": [{{entries}}],
            "ttlMs": 300000,
            "cacheScope": "public"
          }
        }
        """;
}