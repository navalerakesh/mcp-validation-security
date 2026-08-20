using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.Core.Services;

internal static class VerdictReducer
{
    internal static ValidationVerdict DetermineBaselineVerdict(
        IEnumerable<DecisionRecord> decisions,
        IEnumerable<DecisionRecord> coverageDecisions)
    {
        var combined = decisions.Concat(coverageDecisions).ToList();
        if (combined.Any(decision => decision.Gate == GateOutcome.Reject))
        {
            return ValidationVerdict.Reject;
        }

        if (combined.Any(decision => decision.Gate is GateOutcome.ReviewRequired or GateOutcome.CoverageDebt))
        {
            return ValidationVerdict.ReviewRequired;
        }

        return combined.Count == 0
            ? ValidationVerdict.Trusted
            : ValidationVerdict.ConditionallyAcceptable;
    }

    internal static ValidationVerdict DetermineProtocolVerdict(
        ValidationOutcome? protocolOutcome,
        IEnumerable<DecisionRecord> decisions)
    {
        var protocolDecisions = decisions
            .Where(decision =>
                decision.Authority == ValidationRuleSource.Spec ||
                decision.ImpactAreas.Contains(ImpactArea.ProtocolInteroperability))
            .ToList();

        if (protocolDecisions.Any(decision => decision.Gate == GateOutcome.Reject))
        {
            return ValidationVerdict.Reject;
        }

        if (protocolDecisions.Any(decision => decision.Gate is GateOutcome.ReviewRequired or GateOutcome.CoverageDebt))
        {
            return ValidationVerdict.ReviewRequired;
        }

        if (protocolDecisions.Count == 0)
        {
            return protocolOutcome switch
            {
                ValidationOutcome.Succeeded => ValidationVerdict.Trusted,
                ValidationOutcome.Failed or ValidationOutcome.Error => ValidationVerdict.Reject,
                _ => ValidationVerdict.Unknown
            };
        }

        return ValidationVerdict.ConditionallyAcceptable;
    }

    internal static ValidationVerdict DetermineCoverageVerdict(
        bool hasCoverageDeclarations,
        IEnumerable<DecisionRecord> coverageDecisions)
    {
        return coverageDecisions.Any()
            ? ValidationVerdict.ReviewRequired
            : hasCoverageDeclarations
                ? ValidationVerdict.Trusted
                : ValidationVerdict.Unknown;
    }
}