using System.Collections.ObjectModel;
using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.Core.Services;

/// <summary>
/// Aggregates structured validation findings into reportable coverage summaries.
/// </summary>
public static class ValidationFindingAggregator
{
    public static int GetToolCatalogSize(ToolTestResult? toolValidation)
    {
        if (toolValidation == null)
        {
            return 0;
        }

        if (toolValidation.ToolsDiscovered > 0)
        {
            return toolValidation.ToolsDiscovered;
        }

        return toolValidation.ToolResults
            .Select(tool => tool.ToolName)
            .Where(name => !string.IsNullOrWhiteSpace(name) && !name.Contains("auth", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
    }

    public static double CalculateCoverageRatio(int affectedComponents, int totalComponents)
    {
        if (affectedComponents <= 0)
        {
            return 0.0;
        }

        if (totalComponents <= 0)
        {
            return 1.0;
        }

        return Math.Clamp((double)affectedComponents / totalComponents, 0.0, 1.0);
    }

    public static IReadOnlyList<ValidationFindingCoverage> SummarizeFindingsByRule(IEnumerable<ValidationFinding>? findings, int totalComponents = 0)
    {
        if (findings == null)
        {
            return Array.Empty<ValidationFindingCoverage>();
        }

        var distinctFindings = findings
            .Where(finding => !string.IsNullOrWhiteSpace(finding.RuleId))
            .GroupBy(
                finding => $"{finding.RuleId}|{finding.Category}|{finding.Component}|{finding.Summary}",
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();

        if (distinctFindings.Count == 0)
        {
            return Array.Empty<ValidationFindingCoverage>();
        }

        return distinctFindings
            .GroupBy(
                finding => $"{finding.RuleId}|{finding.Category}|{finding.EffectiveSourceLabel}",
                StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var ordered = group
                    .OrderByDescending(finding => finding.Severity)
                    .ThenBy(finding => finding.Component, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                var representative = ordered[0];
                var components = ordered
                    .Select(finding => string.IsNullOrWhiteSpace(finding.Component) ? "unknown" : finding.Component)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                var affectedComponents = components.Count;
                var denominator = totalComponents > 0 ? Math.Max(totalComponents, affectedComponents) : affectedComponents;

                return new ValidationFindingCoverage
                {
                    RuleId = representative.RuleId,
                    Category = string.IsNullOrWhiteSpace(representative.Category) ? "Validation" : representative.Category,
                    SourceLabel = representative.EffectiveSourceLabel,
                    Severity = ordered.Max(finding => finding.Severity),
                    Summary = representative.Summary,
                    Recommendation = representative.Recommendation,
                    AffectedComponents = affectedComponents,
                    TotalComponents = denominator,
                    CoverageRatio = CalculateCoverageRatio(affectedComponents, denominator),
                    ExampleComponents = new ReadOnlyCollection<string>(components.Take(3).ToList())
                };
            })
            .OrderByDescending(rollup => rollup.Severity)
            .ThenByDescending(rollup => rollup.CoverageRatio)
            .ThenBy(rollup => rollup.RuleId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}

public sealed class ValidationFindingCoverage
{
    public string RuleId { get; init; } = string.Empty;

    public string Category { get; init; } = string.Empty;

    public string SourceLabel { get; init; } = string.Empty;

    public ValidationFindingSeverity Severity { get; init; } = ValidationFindingSeverity.Low;

    public string Summary { get; init; } = string.Empty;

    public string? Recommendation { get; init; }

    public int AffectedComponents { get; init; }

    public int TotalComponents { get; init; }

    public double CoverageRatio { get; init; }

    public IReadOnlyList<string> ExampleComponents { get; init; } = Array.Empty<string>();
}