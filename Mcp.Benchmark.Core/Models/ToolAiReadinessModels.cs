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

public sealed class ToolErrorAiReadinessAssessment
{
    public ValidationFinding Finding { get; init; } = new();

    public IReadOnlyList<string> SupportingIssues { get; init; } = Array.Empty<string>();
}