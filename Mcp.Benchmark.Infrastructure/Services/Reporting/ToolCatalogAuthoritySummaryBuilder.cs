using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Services;

namespace Mcp.Benchmark.Infrastructure.Services.Reporting;

internal static class ToolCatalogAuthoritySummaryBuilder
{
    private static readonly string[] AuthorityOrder = new[] { "spec", "guideline", "heuristic" };

    public static IReadOnlyList<ToolCatalogAuthoritySummary> Build(ToolTestResult? tools)
    {
        if (tools == null)
        {
            return Array.Empty<ToolCatalogAuthoritySummary>();
        }

        var totalComponents = ValidationFindingAggregator.GetToolCatalogSize(tools);
        var findings = CollectFindings(tools)
            .Where(finding => finding.Severity > ValidationFindingSeverity.Info && !string.IsNullOrWhiteSpace(finding.RuleId))
            .ToList();

        if (totalComponents <= 0 && findings.Count == 0)
        {
            return Array.Empty<ToolCatalogAuthoritySummary>();
        }

        var rollupsByAuthority = ValidationFindingAggregator.SummarizeFindingsByRule(findings, totalComponents)
            .GroupBy(rollup => NormalizeAuthority(rollup.SourceLabel), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

        var componentsByAuthority = findings
            .GroupBy(finding => NormalizeAuthority(finding.EffectiveSourceLabel), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group
                    .Select(finding => finding.Component)
                    .Where(component => !string.IsNullOrWhiteSpace(component))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count(),
                StringComparer.OrdinalIgnoreCase);

        return AuthorityOrder
            .Select(authority => BuildSummary(
                authority,
                totalComponents,
                rollupsByAuthority.TryGetValue(authority, out var rollups) ? rollups : null,
                componentsByAuthority.TryGetValue(authority, out var affectedComponents) ? affectedComponents : 0))
            .ToList();
    }

    private static IEnumerable<ValidationFinding> CollectFindings(ToolTestResult tools)
    {
        return tools.ToolResults.SelectMany(tool => tool.Findings)
            .Concat(tools.AiReadinessFindings ?? Enumerable.Empty<ValidationFinding>())
            .Concat(tools.Findings ?? Enumerable.Empty<ValidationFinding>());
    }

    private static ToolCatalogAuthoritySummary BuildSummary(
        string authority,
        int totalComponents,
        List<ValidationFindingCoverage>? rollups,
        int affectedComponents)
    {
        rollups ??= new List<ValidationFindingCoverage>();

        if (affectedComponents == 0 && rollups.Count > 0)
        {
            affectedComponents = rollups.Max(rollup => rollup.AffectedComponents);
        }

        var highlights = rollups
            .OrderByDescending(rollup => rollup.Severity)
            .ThenByDescending(rollup => rollup.CoverageRatio)
            .Select(rollup => rollup.Summary)
            .Take(2)
            .ToList();

        if (highlights.Count == 0)
        {
            highlights.Add("No current catalog-wide tool advisories.");
        }

        return new ToolCatalogAuthoritySummary
        {
            SourceLabel = authority,
            ActiveRuleCount = rollups.Count,
            AffectedComponents = affectedComponents,
            TotalComponents = totalComponents,
            HighestSeverity = rollups.Count > 0 ? rollups.Max(rollup => rollup.Severity) : null,
            Highlights = highlights
        };
    }

    private static string NormalizeAuthority(string? sourceLabel)
    {
        return string.IsNullOrWhiteSpace(sourceLabel)
            ? "unspecified"
            : sourceLabel.Trim().ToLowerInvariant();
    }
}

internal sealed class ToolCatalogAuthoritySummary
{
    public string SourceLabel { get; init; } = string.Empty;

    public int ActiveRuleCount { get; init; }

    public int AffectedComponents { get; init; }

    public int TotalComponents { get; init; }

    public ValidationFindingSeverity? HighestSeverity { get; init; }

    public IReadOnlyList<string> Highlights { get; init; } = Array.Empty<string>();
}