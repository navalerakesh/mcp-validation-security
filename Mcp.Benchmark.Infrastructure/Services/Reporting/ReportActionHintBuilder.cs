using System.Text.RegularExpressions;
using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.Infrastructure.Services.Reporting;

internal static partial class ReportActionHintBuilder
{
    private const int MaxHints = 4;
    private const int MaxHintLength = 150;

    public static IReadOnlyList<string> Build(ValidationResult result)
    {
        var hints = new List<string>();

        if (result.PolicyOutcome is { Passed: false } policyOutcome)
        {
            hints.Add(Compact(policyOutcome.Summary));
        }

        if (result.ProtocolCompliance?.Violations?.Count > 0)
        {
            foreach (var violation in result.ProtocolCompliance.Violations
                         .OrderByDescending(item => item.Severity)
                         .Take(2))
            {
                hints.Add(BuildHint("Protocol", violation.Recommendation, violation.Description));
            }
        }

        if (result.SecurityTesting?.Vulnerabilities?.Count > 0)
        {
            foreach (var vulnerability in result.SecurityTesting.Vulnerabilities
                         .OrderByDescending(item => item.Severity)
                         .Take(2))
            {
                hints.Add(BuildHint("Security", vulnerability.Remediation, vulnerability.Description));
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
