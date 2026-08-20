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
    public void CurrentDecisionPolicy_ShouldExposeEveryDecisionInputDeterministically()
    {
        var policy = DecisionPolicyManifest.CreateCurrent(
            Array.Empty<DecisionRecord>(),
            new[]
            {
                CreatePack("protocol-feature-pack"),
                CreatePack("protocol-rule-pack"),
                CreatePack("observed-surface-scenario-pack"),
                CreatePack("built-in-client-profile-pack")
            });

        policy.Version.Should().Be(DecisionPolicyManifest.CurrentVersion);
        policy.OutcomeTaxonomyVersion.Should().Be(ValidationOutcomeTaxonomy.Version);
        policy.RuleCatalogVersion.Should().Be(ValidationFindingRuleIds.CatalogVersion);
        policy.ScoringPolicyVersion.Should().Be(Mcp.Benchmark.Core.Constants.ScoringConstants.PolicyVersion);
        policy.PackRevisions.Keys.Should().BeInAscendingOrder(StringComparer.Ordinal);
        policy.RuleRevisions.Keys.Should().BeInAscendingOrder(StringComparer.Ordinal);
        policy.RuleRevisions.Should().NotBeEmpty();
        policy.RuleRevisions.Values.Should().OnlyContain(version =>
            version == ValidationFindingRuleIds.CatalogVersion ||
            version == ValidationOutcomeTaxonomy.Version ||
            version == DecisionPolicyManifest.CurrentVersion);
        policy.Weights.Keys.Should().BeInAscendingOrder(StringComparer.Ordinal);
        policy.Thresholds.Keys.Should().BeInAscendingOrder(StringComparer.Ordinal);
        policy.Caps.Keys.Should().BeInAscendingOrder(StringComparer.Ordinal);
        policy.Parameters.Keys.Should().BeInAscendingOrder(StringComparer.Ordinal);
        policy.PatternSets.Keys.Should().BeInAscendingOrder(StringComparer.Ordinal);
        policy.PatternSets["promptInjection"].Should().NotBeEmpty();
        policy.Parameters.Keys.Should().Contain(nameof(Mcp.Benchmark.Core.Constants.ScoringConstants.ExfiltrationPenaltyMax));
        policy.Parameters.Keys.Should().Contain(nameof(Mcp.Benchmark.Core.Constants.ScoringConstants.CoverageConfidenceInconclusive));
        policy.PackRevisions.Keys.Should().Contain(new[]
        {
            "protocol-feature-pack",
            "protocol-rule-pack",
            "observed-surface-scenario-pack",
            "built-in-client-profile-pack",
            "verdict-policy"
        });
        policy.Weights.Where(pair => pair.Key.StartsWith("trust.", StringComparison.Ordinal)).Sum(pair => pair.Value).Should().BeApproximately(1.0, 0.000001);
        policy.Weights.Where(pair => pair.Key.StartsWith("aggregate.", StringComparison.Ordinal)).Sum(pair => pair.Value).Should().BeApproximately(1.0, 0.000001);
    }

    private static ValidationPackDescriptor CreatePack(string key) => new()
    {
        Key = new ValidationDescriptorKey(key),
        Kind = ValidationPackKind.RulePack,
        Revision = new ValidationRevision("2026-04"),
        DisplayName = key,
        Stability = ValidationStability.Stable
    };

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