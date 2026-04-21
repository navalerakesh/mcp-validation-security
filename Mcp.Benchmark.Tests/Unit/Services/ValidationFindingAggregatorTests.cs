using FluentAssertions;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Services;

namespace Mcp.Benchmark.Tests.Unit.Services;

public class ValidationFindingAggregatorTests
{
    [Fact]
    public void GetToolCatalogSize_ShouldIgnoreSyntheticAuthenticationEntries()
    {
        var toolValidation = new ToolTestResult
        {
            ToolResults = new List<IndividualToolResult>
            {
                new() { ToolName = "Authentication Discovery" },
                new() { ToolName = "tools/list (Auth Check)" },
                new() { ToolName = "search" },
                new() { ToolName = "search" }
            }
        };

        var toolCatalogSize = ValidationFindingAggregator.GetToolCatalogSize(toolValidation);

        toolCatalogSize.Should().Be(1);
    }

    [Fact]
    public void SummarizeFindingsByRule_ShouldDeduplicateFindingsAndComputeCoverage()
    {
        var findings = new List<ValidationFinding>
        {
            new()
            {
                RuleId = ValidationFindingRuleIds.ToolGuidelineReadOnlyHintMissing,
                Category = "McpGuideline",
                Component = "search",
                Severity = ValidationFindingSeverity.High,
                Summary = "Tool is missing a read-only hint.",
                Recommendation = "Annotate read-only tools."
            },
            new()
            {
                RuleId = ValidationFindingRuleIds.ToolGuidelineReadOnlyHintMissing,
                Category = "McpGuideline",
                Component = "search",
                Severity = ValidationFindingSeverity.High,
                Summary = "Tool is missing a read-only hint.",
                Recommendation = "Annotate read-only tools."
            },
            new()
            {
                RuleId = ValidationFindingRuleIds.ToolGuidelineReadOnlyHintMissing,
                Category = "McpGuideline",
                Component = "lookup",
                Severity = ValidationFindingSeverity.Low,
                Summary = "Tool is missing a read-only hint.",
                Recommendation = "Annotate read-only tools."
            }
        };

        var rollup = ValidationFindingAggregator.SummarizeFindingsByRule(findings, totalComponents: 4)
            .Should().ContainSingle().Which;

        rollup.RuleId.Should().Be(ValidationFindingRuleIds.ToolGuidelineReadOnlyHintMissing);
        rollup.Severity.Should().Be(ValidationFindingSeverity.High);
        rollup.AffectedComponents.Should().Be(2);
        rollup.TotalComponents.Should().Be(4);
        rollup.CoverageRatio.Should().Be(0.5);
        rollup.ExampleComponents.Should().Contain(new[] { "search", "lookup" });
    }
}