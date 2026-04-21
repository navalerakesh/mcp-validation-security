using FluentAssertions;
using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.Tests.Unit.Models;

public class ValidationFindingTests
{
    [Fact]
    public void EffectiveSource_ForGuidelineRuleId_ShouldInferGuideline()
    {
        var finding = new ValidationFinding
        {
            RuleId = ValidationFindingRuleIds.ToolGuidelineReadOnlyHintMissing,
            Category = "McpGuideline"
        };

        finding.EffectiveSource.Should().Be(ValidationRuleSource.Guideline);
        finding.EffectiveSourceLabel.Should().Be("guideline");
    }

    [Fact]
    public void EffectiveSource_ForAiRuleId_ShouldInferHeuristic()
    {
        var finding = new ValidationFinding
        {
            RuleId = ValidationFindingRuleIds.AiReadinessTokenBudgetWarning,
            Category = "AiReadiness"
        };

        finding.EffectiveSource.Should().Be(ValidationRuleSource.Heuristic);
        finding.EffectiveSourceLabel.Should().Be("heuristic");
    }

    [Fact]
    public void EffectiveSource_ForMcpRuleId_ShouldInferSpec()
    {
        var finding = new ValidationFinding
        {
            RuleId = ValidationFindingRuleIds.ResourceReadMissingContentArray,
            Category = "ResourceValidation"
        };

        finding.EffectiveSource.Should().Be(ValidationRuleSource.Spec);
        finding.EffectiveSourceLabel.Should().Be("spec");
    }
}