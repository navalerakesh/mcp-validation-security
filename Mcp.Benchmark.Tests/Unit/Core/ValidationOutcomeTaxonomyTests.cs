using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.Tests.Unit.Core;

public sealed class ValidationOutcomeTaxonomyTests
{
    [Fact]
    public void EverySourceStatus_ShouldMapToClosedCanonicalOutcome()
    {
        Enum.GetValues<TestStatus>().Should().OnlyContain(status =>
            Enum.IsDefined(ValidationOutcomeTaxonomy.From(status)));
        Enum.GetValues<ValidationStatus>().Should().OnlyContain(status =>
            Enum.IsDefined(ValidationOutcomeTaxonomy.From(status)));
        Enum.GetValues<ValidationCoverageStatus>().Should().OnlyContain(status =>
            Enum.IsDefined(ValidationOutcomeTaxonomy.From(status)));
        Enum.GetValues<ProbeResponseClassification>().Should().OnlyContain(classification =>
            Enum.IsDefined(ValidationOutcomeTaxonomy.From(classification)));
    }

    [Theory]
    [InlineData(ValidationOutcome.NotEvaluated, false)]
    [InlineData(ValidationOutcome.Running, false)]
    [InlineData(ValidationOutcome.Succeeded, true)]
    [InlineData(ValidationOutcome.Failed, true)]
    [InlineData(ValidationOutcome.Cancelled, true)]
    public void IsTerminal_ShouldUseClosedOutcomeSemantics(ValidationOutcome outcome, bool expected)
    {
        ValidationOutcomeTaxonomy.IsTerminal(outcome).Should().Be(expected);
    }

    [Fact]
    public void StableFindingRules_ShouldEachHaveExactlyOneRevision()
    {
        var rules = ValidationFindingRuleIds.GetVersionedRules();

        rules.Keys.Should().OnlyHaveUniqueItems();
        rules.Keys.Should().OnlyContain(ruleId => !string.IsNullOrWhiteSpace(ruleId));
        rules.Values.Should().OnlyContain(revision => revision == ValidationFindingRuleIds.CatalogVersion);
    }

    [Fact]
    public void CoverageOutcome_ShouldDeriveFromStatusAndRejectContradictions()
    {
        new ValidationCoverageDeclaration
        {
            LayerId = "test",
            Scope = "covered",
            Status = ValidationCoverageStatus.Covered,
            ObservedOutcome = ValidationOutcome.Failed
        }.Outcome.Should().Be(ValidationOutcome.Failed);

        new ValidationCoverageDeclaration
        {
            LayerId = "test",
            Scope = "auth",
            Status = ValidationCoverageStatus.AuthRequired
        }.Outcome.Should().Be(ValidationOutcome.AuthRequired);

        var action = () => new ValidationCoverageDeclaration
        {
            LayerId = "test",
            Scope = "contradiction",
            Status = ValidationCoverageStatus.AuthRequired,
            ObservedOutcome = ValidationOutcome.Succeeded
        }.Outcome;
        action.Should().Throw<InvalidOperationException>();
    }
}