using System.Text.Json;

namespace Mcp.Benchmark.Core.Models;

public sealed class ToolAiReadinessTarget
{
    public string Name { get; init; } = string.Empty;

    public JsonElement InputSchema { get; init; }
}

public sealed class ToolAiReadinessAnalysis
{
    public double Score { get; init; } = -1;

    public long EstimatedTokenCount { get; init; }

    public string? SummaryIssue { get; init; }

    public IReadOnlyList<ValidationFinding> Findings { get; init; } = Array.Empty<ValidationFinding>();
}

public static class AiReadinessEvidenceKinds
{
    public const string MetadataKey = "evidenceKind";

    public const string ModelEvaluationImpactKey = "modelEvaluationImpact";

    public const string NotMeasuredModelImpact = "not-measured";

    public const string DeterministicSchemaHeuristic = "deterministic-schema-heuristic";

    public const string DeterministicErrorHeuristic = "deterministic-error-heuristic";

    public const string DeterministicPayloadHeuristic = "deterministic-payload-heuristic";

    public const string ModelEvaluation = "model-evaluation";

    public static string Infer(string? evidenceKind, string? ruleId)
    {
        if (!string.IsNullOrWhiteSpace(evidenceKind))
        {
            return evidenceKind.Trim();
        }

        if (string.Equals(ruleId, ValidationFindingRuleIds.ToolLlmFriendliness, StringComparison.OrdinalIgnoreCase))
        {
            return DeterministicErrorHeuristic;
        }

        if (string.Equals(ruleId, ValidationFindingRuleIds.AiReadinessTokenBudgetExceeded, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(ruleId, ValidationFindingRuleIds.AiReadinessTokenBudgetWarning, StringComparison.OrdinalIgnoreCase))
        {
            return DeterministicPayloadHeuristic;
        }

        if (!string.IsNullOrWhiteSpace(ruleId) && ruleId.StartsWith("AI.TOOL.SCHEMA.", StringComparison.OrdinalIgnoreCase))
        {
            return DeterministicSchemaHeuristic;
        }

        return DeterministicSchemaHeuristic;
    }

    public static string ToDisplayLabel(string? evidenceKind, string? ruleId = null)
    {
        return Infer(evidenceKind, ruleId) switch
        {
            DeterministicSchemaHeuristic => "Deterministic schema heuristic",
            DeterministicErrorHeuristic => "Deterministic error heuristic",
            DeterministicPayloadHeuristic => "Deterministic payload heuristic",
            ModelEvaluation => "Measured model evaluation",
            _ => "Deterministic heuristic"
        };
    }
}

public sealed class ToolErrorAiReadinessAssessment
{
    public ValidationFinding Finding { get; init; } = new();

    public IReadOnlyList<string> SupportingIssues { get; init; } = Array.Empty<string>();
}