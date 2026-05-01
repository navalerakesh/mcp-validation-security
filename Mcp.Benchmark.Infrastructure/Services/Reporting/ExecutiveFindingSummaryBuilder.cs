using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Services;

namespace Mcp.Benchmark.Infrastructure.Services.Reporting;

internal static class ExecutiveFindingSummaryBuilder
{
    private const int MaxPriorityFindings = 5;

    public static IReadOnlyList<string> BuildPriorityFindings(ValidationResult result)
    {
        var findings = new List<string>();

        AddIfPresent(findings, BuildSpecSummary(result));
        AddIfPresent(findings, BuildGuidelineSummary(result));
        AddIfPresent(findings, BuildGuidelineSkipSummary(result));
        AddIfPresent(findings, BuildHeuristicSummary(result));
        AddIfPresent(findings, BuildCompatibilitySummary(result));
        AddIfPresent(findings, BuildOperationalSummary(result));

        return findings
            .Where(finding => !string.IsNullOrWhiteSpace(finding))
            .Distinct(StringComparer.Ordinal)
            .Take(MaxPriorityFindings)
            .ToList();
    }

    public static string FormatAuthorityTag(ValidationRuleSource source)
    {
        return ValidationAuthorityHierarchy.FormatTag(source);
    }

    public static string? BuildGuidelineSkipHint(ValidationResult result)
    {
        var skippedScopes = result.Evidence.Coverage
            .Where(item => item.Status == ValidationCoverageStatus.Skipped)
            .ToList();

        if (skippedScopes.Count == 0)
        {
            return null;
        }

        var representative = skippedScopes[0];
        return $"[Guideline/Skip] {FormatScopeLabel(representative)} was skipped: {FormatReason(representative.Reason)}";
    }

    private static string? BuildSpecSummary(ValidationResult result)
    {
        var specViolations = result.ProtocolCompliance?.Violations?
            .OrderByDescending(item => item.Severity)
            .ToList();

        if (specViolations is { Count: > 0 })
        {
            var representative = specViolations[0];
            return $"[Spec] {specViolations.Count} protocol violation(s), led by {representative.CheckId}: {representative.Description}";
        }

        var specDecisions = GetRepresentativeDecisions(result, ValidationRuleSource.Spec, blockingOnly: false);
        if (specDecisions.Count == 0)
        {
            return null;
        }

        var decision = specDecisions[0];
        return $"[Spec] {specDecisions.Count} spec signal(s), led by {FormatDecisionIdentity(decision)}: {decision.Summary}";
    }

    private static string? BuildGuidelineSummary(ValidationResult result)
    {
        var guidelineDecisions = GetRepresentativeDecisions(result, ValidationRuleSource.Guideline, blockingOnly: false);
        if (guidelineDecisions.Count == 0)
        {
            return null;
        }

        var decision = guidelineDecisions[0];
        return $"[Guideline] {guidelineDecisions.Count} guidance signal(s), led by {FormatDecisionIdentity(decision)}: {decision.Summary}";
    }

    private static string? BuildGuidelineSkipSummary(ValidationResult result)
    {
        var skippedScopes = result.Evidence.Coverage
            .Where(item => item.Status == ValidationCoverageStatus.Skipped)
            .ToList();

        if (skippedScopes.Count == 0)
        {
            return null;
        }

        var representative = skippedScopes[0];
        return $"[Guideline/Skip] {skippedScopes.Count} scope(s) skipped by validator design, led by {FormatScopeLabel(representative)}: {FormatReason(representative.Reason)}";
    }

    private static string? BuildHeuristicSummary(ValidationResult result)
    {
        var vulnerabilities = result.SecurityTesting?.Vulnerabilities?
            .OrderByDescending(item => item.Severity)
            .ToList();

        if (vulnerabilities is { Count: > 0 })
        {
            var representative = vulnerabilities[0];
            return $"[Heuristic] {vulnerabilities.Count} non-spec security or advisory signal(s), led by {representative.Id}: {representative.Description}";
        }

        var aiReadinessFindings = result.ToolValidation?.AiReadinessFindings?
            .Where(item => !string.IsNullOrWhiteSpace(item.Summary))
            .OrderByDescending(item => item.Severity)
            .ToList();

        if (aiReadinessFindings is { Count: > 0 })
        {
            var representative = aiReadinessFindings[0];
            return $"[Heuristic] {aiReadinessFindings.Count} deterministic AI-readiness advisory signal(s), led by {representative.RuleId}: {representative.Summary}";
        }

        var heuristicDecisions = GetRepresentativeDecisions(result, ValidationRuleSource.Heuristic, blockingOnly: false);
        if (heuristicDecisions.Count == 0)
        {
            return null;
        }

        var decision = heuristicDecisions[0];
        return $"[Heuristic] {heuristicDecisions.Count} non-spec signal(s), led by {FormatDecisionIdentity(decision)}: {decision.Summary}";
    }

    private static string? BuildCompatibilitySummary(ValidationResult result)
    {
        var incompatibleAssessments = result.ClientCompatibility?.Assessments
            .Where(item => item.Status == ClientProfileCompatibilityStatus.Incompatible)
            .ToList();

        if (incompatibleAssessments is not { Count: > 0 })
        {
            return null;
        }

        var representative = incompatibleAssessments[0];
        return $"[Compatibility] {incompatibleAssessments.Count} incompatible profile(s), led by {representative.DisplayName}: {representative.Summary}";
    }

    private static string? BuildOperationalSummary(ValidationResult result)
    {
        if (result.CriticalErrors.Count == 0)
        {
            return null;
        }

        return $"[Operational] {result.CriticalErrors.Count} critical execution error(s), led by: {result.CriticalErrors[0]}";
    }

    private static List<DecisionRecord> GetRepresentativeDecisions(ValidationResult result, ValidationRuleSource authority, bool blockingOnly)
    {
        var source = blockingOnly
            ? result.VerdictAssessment?.BlockingDecisions
            : result.VerdictAssessment?.BlockingDecisions?.Concat(result.VerdictAssessment.TriggeredDecisions)
                ?? result.VerdictAssessment?.TriggeredDecisions;

        return source?
            .Where(item => item.Authority == authority && !string.IsNullOrWhiteSpace(item.Summary))
            .OrderByDescending(item => item.Gate)
            .ThenByDescending(item => item.Severity)
            .GroupBy(item => item.RuleId ?? item.Category, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList()
            ?? new List<DecisionRecord>();
    }

    private static string FormatDecisionIdentity(DecisionRecord decision)
    {
        if (!string.IsNullOrWhiteSpace(decision.RuleId))
        {
            return decision.RuleId!;
        }

        return decision.Category;
    }

    private static string FormatScopeLabel(ValidationCoverageDeclaration declaration)
    {
        if (!string.IsNullOrWhiteSpace(declaration.Scope))
        {
            return declaration.Scope;
        }

        return declaration.LayerId;
    }

    private static string FormatReason(string? reason)
    {
        return string.IsNullOrWhiteSpace(reason)
            ? "No explicit skip reason was recorded."
            : reason.Trim();
    }

    private static void AddIfPresent(List<string> findings, string? summary)
    {
        if (!string.IsNullOrWhiteSpace(summary))
        {
            findings.Add(summary);
        }
    }
}