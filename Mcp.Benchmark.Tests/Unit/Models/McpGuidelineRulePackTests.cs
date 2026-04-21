using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.Tests.Unit;

public class McpGuidelineRulePackTests
{
    [Fact]
    public void GetAll_ShouldExposeKnownGuidelineRules()
    {
        var ruleIds = McpGuidelineRulePack.GetAll()
            .Select(rule => rule.RuleId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        ruleIds.Should().Contain(ValidationFindingRuleIds.ToolGuidelineDisplayTitleMissing);
        ruleIds.Should().Contain(ValidationFindingRuleIds.ResourceGuidelineMimeTypeMissing);
        ruleIds.Should().Contain(ValidationFindingRuleIds.PromptGuidelineDescriptionMissing);
        ruleIds.Should().Contain(ValidationFindingRuleIds.OptionalCapabilityLoggingDeclaredButUnsupported);
        ruleIds.Should().Contain(ValidationFindingRuleIds.OptionalCapabilityCompletionsSupportedButUndeclared);
    }

    [Fact]
    public void ValidationRuleCatalog_ShouldClassifyPackRulesAsGuidelines()
    {
        foreach (var rule in McpGuidelineRulePack.GetAll())
        {
            var descriptor = ValidationRuleCatalog.Find(rule.RuleId);

            descriptor.Should().NotBeNull();
            descriptor!.Source.Should().Be(ValidationRuleSource.Guideline);
        }
    }
}