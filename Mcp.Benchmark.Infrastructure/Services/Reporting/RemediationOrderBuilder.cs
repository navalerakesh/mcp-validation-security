using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Services;

namespace Mcp.Benchmark.Infrastructure.Services.Reporting;

internal static class RemediationOrderBuilder
{
    private const int MaxItemsPerGroup = 5;

    internal static IReadOnlyList<RemediationOrderGroup> Build(ValidationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var candidates = new List<RemediationCandidate>();
        var protocolRuleIds = AddProtocolViolations(result, candidates);
        var vulnerabilityRuleIds = AddSecurityVulnerabilities(result, candidates);
        AddDecisions(result, candidates, protocolRuleIds, vulnerabilityRuleIds);
        AddPerformanceFindings(result, candidates);

        return candidates
            .Select(candidate => new { Candidate = candidate, Priority = ResolvePriority(candidate) })
            .GroupBy(item => item.Priority)
            .OrderBy(group => group.Key)
            .Select(group => BuildGroup(group.Key, group.Select(item => item.Candidate)))
            .Where(group => group.Items.Count > 0)
            .ToList();
    }

    private static HashSet<string> AddProtocolViolations(ValidationResult result, List<RemediationCandidate> candidates)
    {
        var ruleIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (result.ProtocolCompliance?.Violations is not { Count: > 0 } violations)
        {
            return ruleIds;
        }

        foreach (var violation in violations)
        {
            if (!string.IsNullOrWhiteSpace(violation.CheckId))
            {
                ruleIds.Add(violation.CheckId);
            }

            candidates.Add(new RemediationCandidate(
                Issue: violation.Description,
                Remediation: violation.Recommendation,
                Category: violation.Category,
                Component: string.IsNullOrWhiteSpace(violation.Category) ? "protocol" : violation.Category,
                RuleId: violation.CheckId,
                SpecReference: violation.SpecReference,
                Severity: MapSeverity(violation.Severity),
                Gate: GateForViolation(violation.Severity),
                Authority: ValidationRuleSourceClassifier.GetSource(violation),
                ImpactAreas: [ImpactArea.ProtocolInteroperability, ImpactArea.CapabilityContract]));
        }

        return ruleIds;
    }

    private static HashSet<string> AddSecurityVulnerabilities(ValidationResult result, List<RemediationCandidate> candidates)
    {
        var ruleIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (result.SecurityTesting?.Vulnerabilities is not { Count: > 0 } vulnerabilities)
        {
            return ruleIds;
        }

        foreach (var vulnerability in vulnerabilities)
        {
            if (!string.IsNullOrWhiteSpace(vulnerability.Id))
            {
                ruleIds.Add(vulnerability.Id);
            }

            candidates.Add(new RemediationCandidate(
                Issue: vulnerability.Description,
                Remediation: vulnerability.Remediation,
                Category: vulnerability.Category,
                Component: vulnerability.AffectedComponent,
                RuleId: vulnerability.Id,
                SpecReference: null,
                Severity: MapSeverity(vulnerability.Severity),
                Gate: vulnerability.Severity is VulnerabilitySeverity.Critical or VulnerabilitySeverity.High
                    ? GateOutcome.Reject
                    : GateOutcome.ReviewRequired,
                Authority: ValidationRuleSourceClassifier.GetSource(vulnerability),
                ImpactAreas: ResolveImpactAreas(vulnerability.Category, vulnerability.AffectedComponent, vulnerability.Name, vulnerability.Description)));
        }

        return ruleIds;
    }

    private static void AddDecisions(
        ValidationResult result,
        List<RemediationCandidate> candidates,
        HashSet<string> protocolRuleIds,
        HashSet<string> vulnerabilityRuleIds)
    {
        var decisions = (result.VerdictAssessment?.BlockingDecisions ?? Enumerable.Empty<DecisionRecord>())
            .Concat(result.VerdictAssessment?.TriggeredDecisions ?? Enumerable.Empty<DecisionRecord>())
            .Where(decision => decision.Gate >= GateOutcome.CoverageDebt)
            .Where(decision => string.IsNullOrWhiteSpace(decision.RuleId) ||
                (!protocolRuleIds.Contains(decision.RuleId) && !vulnerabilityRuleIds.Contains(decision.RuleId)));

        foreach (var decision in decisions)
        {
            candidates.Add(new RemediationCandidate(
                Issue: decision.Summary,
                Remediation: ResolveDecisionRemediation(decision),
                Category: decision.Category,
                Component: decision.Component,
                RuleId: decision.RuleId,
                SpecReference: decision.SpecReference,
                Severity: decision.Severity,
                Gate: decision.Gate,
                Authority: decision.Authority,
                ImpactAreas: decision.ImpactAreas));
        }
    }

    private static void AddPerformanceFindings(ValidationResult result, List<RemediationCandidate> candidates)
    {
        var performance = result.PerformanceTesting;
        if (performance is null)
        {
            return;
        }

        foreach (var finding in performance.Findings)
        {
            candidates.Add(new RemediationCandidate(
                Issue: finding.Summary,
                Remediation: finding.Recommendation,
                Category: finding.Category,
                Component: finding.Component,
                RuleId: finding.RuleId,
                SpecReference: finding.EffectiveSpecReference,
                Severity: finding.Severity,
                Gate: GateForFinding(finding.Severity),
                Authority: finding.EffectiveSource,
                ImpactAreas: [ImpactArea.OperationalResilience]));
        }

        foreach (var error in performance.CriticalErrors.Where(error => !string.IsNullOrWhiteSpace(error)))
        {
            candidates.Add(new RemediationCandidate(
                Issue: error,
                Remediation: "Resolve the performance probe error, then rerun measurement to restore trustworthy latency and load evidence.",
                Category: "Performance",
                Component: "performance",
                RuleId: null,
                SpecReference: null,
                Severity: ValidationFindingSeverity.High,
                Gate: GateOutcome.ReviewRequired,
                Authority: ValidationRuleSource.Unspecified,
                ImpactAreas: [ImpactArea.OperationalResilience]));
        }
    }

    private static RemediationOrderGroup BuildGroup(int priority, IEnumerable<RemediationCandidate> candidates)
    {
        var definition = GetPriorityDefinition(priority);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var items = candidates
            .OrderByDescending(candidate => candidate.Gate)
            .ThenByDescending(candidate => candidate.Severity)
            .ThenBy(candidate => ValidationAuthorityHierarchy.GetSortOrder(candidate.Authority))
            .ThenBy(candidate => candidate.Issue, StringComparer.OrdinalIgnoreCase)
            .Select(candidate => BuildItem(candidate, definition))
            .Where(item => seen.Add(BuildDedupeKey(priority, item)))
            .Take(MaxItemsPerGroup)
            .ToList();

        return new RemediationOrderGroup(
            Priority: priority,
            Title: definition.Title,
            Impact: definition.Impact,
            Items: items);
    }

    private static RemediationOrderItem BuildItem(RemediationCandidate candidate, RemediationPriorityDefinition definition)
    {
        var remediation = !string.IsNullOrWhiteSpace(candidate.Remediation)
            ? candidate.Remediation.Trim()
            : definition.DefaultRemediation;

        var evidence = !string.IsNullOrWhiteSpace(candidate.RuleId)
            ? candidate.RuleId
            : candidate.Category;

        return new RemediationOrderItem(
            Issue: Compact(candidate.Issue),
            Remediation: Compact(remediation),
            Component: string.IsNullOrWhiteSpace(candidate.Component) ? "validation" : candidate.Component,
            Severity: candidate.Severity.ToString(),
            Authority: ValidationAuthorityHierarchy.FormatTag(candidate.Authority),
            Evidence: string.IsNullOrWhiteSpace(evidence) ? "validation evidence" : evidence,
            SpecReference: candidate.SpecReference);
    }

    private static int ResolvePriority(RemediationCandidate candidate)
    {
        var haystack = BuildSearchText(candidate);

        if (ContainsAny(haystack, "bootstrap", "initialize", "initialization", "handshake", "lifecycle", "protocol version", "version negotiation", "json-rpc", "transport", "streamable", "stdio", "session"))
        {
            return 1;
        }

        if (candidate.ImpactAreas.Contains(ImpactArea.AuthenticationBoundary) ||
            ContainsAny(haystack, "auth", "oauth", "authorization", "authentication", "bearer", "token", "audience", "resource metadata", "protected resource"))
        {
            return 2;
        }

        if (candidate.ImpactAreas.Contains(ImpactArea.CapabilityContract) ||
            ContainsAny(haystack, "capability", "capabilities", "advertised", "declared", "not advertised", "unadvertised", "coverage", "tools/list", "tools/call", "resources/list", "prompts/list", "sampling", "roots", "elicitation", "tasks"))
        {
            return 3;
        }

        return 4;
    }

    private static RemediationPriorityDefinition GetPriorityDefinition(int priority)
    {
        return priority switch
        {
            1 => new RemediationPriorityDefinition(
                "Bootstrap & Protocol Version",
                "Lifecycle, version, transport, and JSON-RPC fixes make downstream tool, resource, prompt, and auth probes trustworthy.",
                "Fix the lifecycle or protocol contract first, then rerun validation before interpreting downstream surface results."),
            2 => new RemediationPriorityDefinition(
                "Authentication Boundary",
                "Auth fixes let protected-surface failures distinguish real product behavior from missing, rejected, or mis-scoped credentials.",
                "Fix authentication and token-boundary behavior before judging protected tool, resource, and prompt behavior."),
            3 => new RemediationPriorityDefinition(
                "Advertised Capabilities",
                "Capability-contract fixes make skipped and executed tool, resource, prompt, and task checks align with what the server declared.",
                "Align advertised capabilities with implemented surfaces so downstream validation probes run only where applicable."),
            _ => new RemediationPriorityDefinition(
                "AI Safety, Security, And Performance",
                "After protocol, auth, and capability gates are stable, safety and performance evidence can be prioritized without masking core contract failures.",
                "Address advisory safety, security, and performance evidence after the core validation contract is trustworthy.")
        };
    }

    private static string ResolveDecisionRemediation(DecisionRecord decision)
    {
        if (decision.Metadata.TryGetValue("recommendation", out var recommendation) && !string.IsNullOrWhiteSpace(recommendation))
        {
            return recommendation;
        }

        return decision.Gate == GateOutcome.CoverageDebt
            ? "Restore authoritative evidence for this validation surface or mark the surface intentionally out of scope."
            : string.Empty;
    }

    private static IReadOnlyList<ImpactArea> ResolveImpactAreas(params string?[] values)
    {
        var haystack = string.Join(" ", values.Where(value => !string.IsNullOrWhiteSpace(value)));
        var areas = new List<ImpactArea>();

        if (ContainsAny(haystack, "auth", "oauth", "token", "bearer", "authorization"))
        {
            areas.Add(ImpactArea.AuthenticationBoundary);
        }

        if (ContainsAny(haystack, "prompt", "injection", "tool", "destructive", "autonomy", "consent"))
        {
            areas.Add(ImpactArea.UnsafeAutonomy);
        }

        if (ContainsAny(haystack, "data", "secret", "credential", "resource", "exfiltration", "privacy"))
        {
            areas.Add(ImpactArea.DataExposure);
        }

        if (ContainsAny(haystack, "latency", "performance", "timeout", "load"))
        {
            areas.Add(ImpactArea.OperationalResilience);
        }

        return areas;
    }

    private static string BuildSearchText(RemediationCandidate candidate)
    {
        return string.Join(" ", new[]
        {
            candidate.Issue,
            candidate.Category,
            candidate.Component,
            candidate.RuleId,
            candidate.SpecReference,
            string.Join(" ", candidate.ImpactAreas)
        }.Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    private static bool ContainsAny(string haystack, params string[] needles)
    {
        return needles.Any(needle => haystack.Contains(needle, StringComparison.OrdinalIgnoreCase));
    }

    private static string BuildDedupeKey(int priority, RemediationOrderItem item)
    {
        var key = !string.IsNullOrWhiteSpace(item.Remediation) ? item.Remediation : item.Issue;
        return $"{priority}:{Compact(key).ToLowerInvariant()}";
    }

    private static ValidationFindingSeverity MapSeverity(ViolationSeverity severity)
    {
        return severity switch
        {
            ViolationSeverity.Critical => ValidationFindingSeverity.Critical,
            ViolationSeverity.High => ValidationFindingSeverity.High,
            ViolationSeverity.Medium => ValidationFindingSeverity.Medium,
            _ => ValidationFindingSeverity.Low
        };
    }

    private static ValidationFindingSeverity MapSeverity(VulnerabilitySeverity severity)
    {
        return severity switch
        {
            VulnerabilitySeverity.Critical => ValidationFindingSeverity.Critical,
            VulnerabilitySeverity.High => ValidationFindingSeverity.High,
            VulnerabilitySeverity.Medium => ValidationFindingSeverity.Medium,
            VulnerabilitySeverity.Low => ValidationFindingSeverity.Low,
            _ => ValidationFindingSeverity.Info
        };
    }

    private static GateOutcome GateForViolation(ViolationSeverity severity)
    {
        return severity switch
        {
            ViolationSeverity.Critical => GateOutcome.Reject,
            ViolationSeverity.High => GateOutcome.ReviewRequired,
            ViolationSeverity.Medium => GateOutcome.CoverageDebt,
            _ => GateOutcome.Note
        };
    }

    private static GateOutcome GateForFinding(ValidationFindingSeverity severity)
    {
        return severity switch
        {
            ValidationFindingSeverity.Critical => GateOutcome.Reject,
            ValidationFindingSeverity.High => GateOutcome.ReviewRequired,
            ValidationFindingSeverity.Medium => GateOutcome.CoverageDebt,
            _ => GateOutcome.Note
        };
    }

    private static string Compact(string text)
    {
        return string.Join(" ", (text ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private sealed record RemediationCandidate(
        string Issue,
        string? Remediation,
        string? Category,
        string? Component,
        string? RuleId,
        string? SpecReference,
        ValidationFindingSeverity Severity,
        GateOutcome Gate,
        ValidationRuleSource Authority,
        IReadOnlyList<ImpactArea> ImpactAreas);

    private sealed record RemediationPriorityDefinition(string Title, string Impact, string DefaultRemediation);
}

internal sealed record RemediationOrderGroup(
    int Priority,
    string Title,
    string Impact,
    IReadOnlyList<RemediationOrderItem> Items);

internal sealed record RemediationOrderItem(
    string Issue,
    string Remediation,
    string Component,
    string Severity,
    string Authority,
    string Evidence,
    string? SpecReference);
