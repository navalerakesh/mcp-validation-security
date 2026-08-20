using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.Core.Services;

public static class ValidationBaselineEvaluator
{
    public static ValidationBaselineComparison Compare(ValidationResult baseline, ValidationResult current)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(current);

        var baselineDecisions = GetBlockingDecisions(baseline)
            .ToDictionary(decision => decision.DecisionId, StringComparer.Ordinal);
        var currentDecisions = GetBlockingDecisions(current)
            .ToDictionary(decision => decision.DecisionId, StringComparer.Ordinal);

        var newDecisions = currentDecisions
            .Where(pair => !baselineDecisions.ContainsKey(pair.Key))
            .Select(pair => new BaselineDecisionChange(
                pair.Value.DecisionId,
                pair.Value.RuleId,
                pair.Value.Category,
                pair.Value.Component,
                pair.Value.Gate))
            .OrderBy(change => change.DecisionId, StringComparer.Ordinal)
            .ToArray();
        var resolvedDecisionIds = baselineDecisions.Keys
            .Where(id => !currentDecisions.ContainsKey(id))
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

        var baselineVerdict = baseline.VerdictAssessment?.BaselineVerdict ?? ValidationVerdict.Unknown;
        var currentVerdict = current.VerdictAssessment?.BaselineVerdict ?? ValidationVerdict.Unknown;
    var scoreDelta = Math.Round(current.ComplianceScore - baseline.ComplianceScore, 2);

        return new ValidationBaselineComparison
        {
            BaselineValidationId = baseline.ValidationId,
            BaselineDocumentSchemaVersion = baseline.DocumentSchemaVersion,
            BaselineScore = baseline.ComplianceScore,
            CurrentScore = current.ComplianceScore,
            ScoreDelta = scoreDelta,
            BaselineVerdictBefore = baselineVerdict,
            BaselineVerdictAfter = currentVerdict,
            IsRegression = newDecisions.Length > 0 || IsWorse(currentVerdict, baselineVerdict) || scoreDelta < 0,
            NewBlockingDecisions = newDecisions,
            ResolvedBlockingDecisionIds = resolvedDecisionIds
        };
    }

    private static IEnumerable<DecisionRecord> GetBlockingDecisions(ValidationResult result) =>
        result.VerdictAssessment?.BlockingDecisions
            .Where(decision => decision.Gate is GateOutcome.Reject or GateOutcome.ReviewRequired or GateOutcome.CoverageDebt)
        ?? Enumerable.Empty<DecisionRecord>();

    private static bool IsWorse(ValidationVerdict current, ValidationVerdict baseline) =>
        Rank(current) < Rank(baseline);

    private static int Rank(ValidationVerdict verdict) => verdict switch
    {
        ValidationVerdict.Reject => 0,
        ValidationVerdict.Unknown => 1,
        ValidationVerdict.ReviewRequired => 2,
        ValidationVerdict.ConditionallyAcceptable => 3,
        ValidationVerdict.Trusted => 4,
        _ => throw new ArgumentOutOfRangeException(nameof(verdict), verdict, "Unknown validation verdict.")
    };
}
