namespace Mcp.Benchmark.Core.Models;

public sealed class ValidationBaselineComparison
{
    public string BaselineValidationId { get; init; } = string.Empty;

    public string BaselineDocumentSchemaVersion { get; init; } = string.Empty;

    public double BaselineScore { get; init; }

    public double CurrentScore { get; init; }

    public double ScoreDelta { get; init; }

    public ValidationVerdict BaselineVerdictBefore { get; init; } = ValidationVerdict.Unknown;

    public ValidationVerdict BaselineVerdictAfter { get; init; } = ValidationVerdict.Unknown;

    public bool IsRegression { get; init; }

    public IReadOnlyList<BaselineDecisionChange> NewBlockingDecisions { get; init; } = Array.Empty<BaselineDecisionChange>();

    public IReadOnlyList<string> ResolvedBlockingDecisionIds { get; init; } = Array.Empty<string>();
}

public sealed record BaselineDecisionChange(
    string DecisionId,
    string? RuleId,
    string Category,
    string Component,
    GateOutcome Gate);
