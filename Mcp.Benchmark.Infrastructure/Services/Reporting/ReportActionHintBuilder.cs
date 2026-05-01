using System.Text.RegularExpressions;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Services;

namespace Mcp.Benchmark.Infrastructure.Services.Reporting;

internal static partial class ReportActionHintBuilder
{
    private const int MaxHints = 4;
    private const int MaxHintLength = 150;

    public static IReadOnlyList<string> Build(ValidationResult result)
    {
        var hints = new List<string>();

        if (result.ProtocolCompliance?.Violations?.Count > 0)
        {
            foreach (var violation in result.ProtocolCompliance.Violations
                         .OrderByDescending(item => item.Severity)
                         .Take(1))
            {
                hints.Add(BuildHint("[Spec] Protocol", violation.Recommendation, violation.Description));
            }
        }

        var guidelineDecision = SelectActionableDecision(result, ValidationRuleSource.Guideline);
        if (guidelineDecision != null)
        {
            hints.Add(Compact($"{ValidationAuthorityHierarchy.FormatTag(guidelineDecision.Authority)} {guidelineDecision.Category}: {guidelineDecision.Summary}"));
        }

        var guidelineSkipHint = ExecutiveFindingSummaryBuilder.BuildGuidelineSkipHint(result);
        if (!string.IsNullOrWhiteSpace(guidelineSkipHint))
        {
            hints.Add(Compact(guidelineSkipHint));
        }

        if (result.SecurityTesting?.Vulnerabilities?.Count > 0)
        {
            foreach (var vulnerability in result.SecurityTesting.Vulnerabilities
                         .OrderByDescending(item => item.Severity)
                         .Take(1))
            {
                hints.Add(BuildHint("[Heuristic] Security", vulnerability.Remediation, vulnerability.Description));
            }
        }

        if (hints.Count == 0 && result.PolicyOutcome is { Passed: false })
        {
            foreach (var decision in result.VerdictAssessment?.BlockingDecisions
                         .OrderBy(item => ValidationAuthorityHierarchy.GetSortOrder(item.Authority))
                         .ThenByDescending(item => item.Gate)
                         .ThenByDescending(item => item.Severity)
                         .Where(item => !string.IsNullOrWhiteSpace(item.Summary))
                         .Take(2)
                         ?? Enumerable.Empty<DecisionRecord>())
            {
                hints.Add(Compact($"{ValidationAuthorityHierarchy.FormatTag(decision.Authority)} {decision.Category}: {decision.Summary}"));
            }
        }

        if (result.ClientCompatibility?.Assessments.Count > 0)
        {
            foreach (var assessment in result.ClientCompatibility.Assessments
                         .Where(item => item.Status == ClientProfileCompatibilityStatus.Incompatible)
                         .Take(1))
            {
                hints.Add(Compact($"Client compatibility: {assessment.DisplayName} - {assessment.Summary}"));
            }
        }

        foreach (var recommendation in result.Recommendations.Take(MaxHints))
        {
            hints.Add(Compact(recommendation));
        }

        if (hints.Count == 0 && result.OverallStatus == ValidationStatus.Passed)
        {
            hints.Add("No immediate remediation required; maintain current protocol and security posture.");
        }

        return hints
            .Where(hint => !string.IsNullOrWhiteSpace(hint))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxHints)
            .ToList();
    }

    private static DecisionRecord? SelectActionableDecision(ValidationResult result, ValidationRuleSource authority)
    {
        return (result.VerdictAssessment?.BlockingDecisions ?? Enumerable.Empty<DecisionRecord>())
            .Concat(result.VerdictAssessment?.TriggeredDecisions ?? Enumerable.Empty<DecisionRecord>())
            .Where(item => item.Authority == authority && !string.IsNullOrWhiteSpace(item.Summary))
            .OrderByDescending(item => item.Gate)
            .ThenByDescending(item => item.Severity)
            .FirstOrDefault();
    }

    private static string BuildHint(string prefix, string? recommendation, string? fallback)
    {
        var message = !string.IsNullOrWhiteSpace(recommendation)
            ? recommendation
            : fallback;

        if (string.IsNullOrWhiteSpace(message))
        {
            return string.Empty;
        }

        return Compact($"{prefix}: {message}");
    }

    private static string Compact(string text)
    {
        var normalized = WhitespaceRegex().Replace(text, " ").Trim().TrimEnd('.');
        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        if (normalized.Length > MaxHintLength)
        {
            normalized = normalized[..(MaxHintLength - 3)].TrimEnd() + "...";
        }

        return char.ToUpperInvariant(normalized[0]) + normalized[1..] + ".";
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
