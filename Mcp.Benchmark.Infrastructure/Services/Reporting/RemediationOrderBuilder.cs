using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Services;

namespace Mcp.Benchmark.Infrastructure.Services.Reporting;

internal static class RemediationOrderBuilder
{
    internal static IReadOnlyList<RemediationOrderGroup> Build(ValidationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var candidates = new List<Candidate>();
        var protocolIds = new HashSet<(string Rule, string Component)>();
        var vulnerabilityIds = new HashSet<(string Rule, string Component)>();
        foreach (var violation in result.ProtocolCompliance?.Violations ?? [])
        {
            var component = string.IsNullOrWhiteSpace(violation.Category) ? "protocol" : violation.Category;
            if (!string.IsNullOrWhiteSpace(violation.CheckId)) protocolIds.Add((violation.CheckId, component));
            candidates.Add(new(violation.Description, violation.Recommendation, component, violation.CheckId,
                violation.SpecReference, MapSeverity(violation.Severity), null, ValidationRuleSourceClassifier.GetSource(violation),
                AssuranceDimension.CoreProtocol, GateOutcome.ReviewRequired));
        }
        foreach (var vulnerability in result.SecurityTesting?.Vulnerabilities ?? [])
        {
            if (!string.IsNullOrWhiteSpace(vulnerability.Id)) vulnerabilityIds.Add((vulnerability.Id, vulnerability.AffectedComponent));
            candidates.Add(new(vulnerability.Description, vulnerability.Remediation, vulnerability.AffectedComponent,
                vulnerability.Id, null, MapSeverity(vulnerability.Severity), null, ValidationRuleSourceClassifier.GetSource(vulnerability),
                AssuranceDimension.Security, GateOutcome.ReviewRequired, SafetyFinding: true));
        }
        foreach (var claim in result.Assurance.Claims.Where(claim =>
            claim.Outcome is not (AssuranceClaimOutcome.Satisfied or AssuranceClaimOutcome.NotApplicable)))
        {
            claim.Metadata.TryGetValue("ruleId", out var ruleId);
            if (ruleId != null && (protocolIds.Contains((ruleId, claim.Subject)) || vulnerabilityIds.Contains((ruleId, claim.Subject))))
                continue;
            claim.Metadata.TryGetValue("severity", out var recordedSeverity);
            var severity = Enum.TryParse<ValidationFindingSeverity>(recordedSeverity, true, out var parsed) && Enum.IsDefined(parsed)
                ? parsed : (ValidationFindingSeverity?)null;
            var outside = ValidationPolicyEvaluator.IsOutsideSelectedScope(result.Assurance, claim);
            var gate = outside ? GateOutcome.Note : claim.Outcome switch
            {
                AssuranceClaimOutcome.Violated or AssuranceClaimOutcome.Error => GateOutcome.Reject,
                AssuranceClaimOutcome.Incomplete => GateOutcome.CoverageDebt,
                _ => GateOutcome.ReviewRequired
            };
            var authority = claim.Authority switch
            {
                AssuranceAuthority.OfficialSpecification or AssuranceAuthority.OfficialConformance => ValidationRuleSource.Spec,
                AssuranceAuthority.Heuristic or AssuranceAuthority.ModelAdvisory => ValidationRuleSource.Heuristic,
                _ => ValidationRuleSource.Guideline
            };
            candidates.Add(new(claim.Summary, claim.Remediation, claim.Subject, ruleId ?? claim.ClaimId, claim.Source.Uri,
                severity, recordedSeverity, authority, claim.Dimension, gate, claim.ClaimId, outside));
        }
        foreach (var finding in result.PerformanceTesting?.Findings ?? [])
        {
            if (result.Assurance.Claims.Any(claim => claim.Subject == finding.Component &&
                claim.Metadata.GetValueOrDefault("ruleId") == finding.RuleId)) continue;
            candidates.Add(new(finding.Summary, finding.Recommendation, finding.Component, finding.RuleId,
                finding.EffectiveSpecReference, finding.Severity, null, finding.EffectiveSource,
                AssuranceDimension.Operations, GateOutcome.ReviewRequired));
        }
        foreach (var error in result.PerformanceTesting?.CriticalErrors ?? [])
        {
            if (string.IsNullOrWhiteSpace(error)) continue;
            candidates.Add(new(error, "Investigate the interrupted performance measurement before drawing conclusions.",
                "performance", null, null, null, null, ValidationRuleSource.Unspecified, AssuranceDimension.Operations, GateOutcome.CoverageDebt));
        }

        return candidates.GroupBy(GetPriority).OrderBy(group => group.Key).Select(group =>
        {
            var (title, impact) = DescribePriority(group.Key);
            var items = group.OrderByDescending(candidate => candidate.Gate)
                .ThenByDescending(candidate => candidate.Severity)
                .ThenBy(candidate => ValidationAuthorityHierarchy.GetSortOrder(candidate.Authority))
                .ThenBy(candidate => candidate.Issue, StringComparer.Ordinal)
                .ThenBy(candidate => candidate.Component, StringComparer.Ordinal)
                .ThenBy(candidate => candidate.RuleId, StringComparer.Ordinal)
                .ThenBy(candidate => candidate.ClaimId, StringComparer.Ordinal)
                .Select(BuildItem).Distinct().ToArray();
            return new RemediationOrderGroup(group.Key, title, impact, items);
        }).ToArray();
    }

    private static int GetPriority(Candidate candidate)
    {
        if (candidate.OutsideSelectedScope) return 5;
        if (candidate.Authority == ValidationRuleSource.Heuristic || candidate.SafetyFinding) return 4;
        if (candidate.Dimension == AssuranceDimension.CoreProtocol && candidate.Authority != ValidationRuleSource.Spec &&
            candidate.Gate == GateOutcome.ReviewRequired) return 4;
        return candidate.Dimension switch
        {
            AssuranceDimension.CoreProtocol or AssuranceDimension.ExecutionIntegrity or AssuranceDimension.ExternalConformance => 1,
            AssuranceDimension.Security or AssuranceDimension.ControlledApplication => 2,
            AssuranceDimension.CapabilityContract or AssuranceDimension.Extension or AssuranceDimension.ClientCompatibility => 3,
            _ => 4
        };
    }

    private static RemediationOrderItem BuildItem(Candidate candidate)
    {
        var remediation = !string.IsNullOrWhiteSpace(candidate.Remediation) ? candidate.Remediation
            : candidate.OutsideSelectedScope ? "Review this optional evidence separately from the selected assessment gate."
            : candidate.Gate == GateOutcome.CoverageDebt ? "Collect the missing admitted evidence; absence is not proof of a server defect."
            : "Review the recorded finding and its source before deciding on a correction.";
        var severity = candidate.Severity?.ToString() ?? (string.IsNullOrWhiteSpace(candidate.RecordedSeverity)
            ? "Not recorded" : $"Unrecognized recorded severity: {candidate.RecordedSeverity}");
        return new(Compact(candidate.Issue), Compact(remediation), candidate.Component, severity,
            ValidationAuthorityHierarchy.FormatTag(candidate.Authority), candidate.RuleId ?? "No rule ID recorded",
            candidate.SpecReference, candidate.ClaimId, candidate.OutsideSelectedScope);
    }

    private static (string Title, string Impact) DescribePriority(int priority) => priority switch
    {
        1 => ("Bootstrap & Protocol Version", "Establish trustworthy protocol and execution evidence before interpreting dependent results."),
        2 => ("Security & Authorization Boundaries", "Establish the recorded identity, authorization and security-control boundaries."),
        3 => ("Advertised Capabilities", "Align the recorded capability contracts and client expectations; preserve the distinction between discovery and execution."),
        4 => ("AI Safety, Security, And Performance", "Review advisory safety findings and measured operational evidence within their declared scope."),
        _ => ("Optional And Outside-Selected-Scope Evidence", "Retain optional findings without treating them as failures of the selected assessment.")
    };

    private static ValidationFindingSeverity MapSeverity(ViolationSeverity severity) => severity switch
    {
        ViolationSeverity.Critical => ValidationFindingSeverity.Critical,
        ViolationSeverity.High => ValidationFindingSeverity.High,
        ViolationSeverity.Medium => ValidationFindingSeverity.Medium,
        ViolationSeverity.Low => ValidationFindingSeverity.Low,
        _ => ValidationFindingSeverity.Info
    };

    private static ValidationFindingSeverity MapSeverity(VulnerabilitySeverity severity) => severity switch
    {
        VulnerabilitySeverity.Critical => ValidationFindingSeverity.Critical,
        VulnerabilitySeverity.High => ValidationFindingSeverity.High,
        VulnerabilitySeverity.Medium => ValidationFindingSeverity.Medium,
        VulnerabilitySeverity.Low => ValidationFindingSeverity.Low,
        _ => ValidationFindingSeverity.Info
    };

    private static string Compact(string? text) =>
        string.Join(" ", (text ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private sealed record Candidate(
        string Issue, string? Remediation, string Component, string? RuleId, string? SpecReference,
        ValidationFindingSeverity? Severity, string? RecordedSeverity, ValidationRuleSource Authority,
        AssuranceDimension Dimension, GateOutcome Gate, string? ClaimId = null,
        bool OutsideSelectedScope = false, bool SafetyFinding = false);
}

internal sealed record RemediationOrderGroup(int Priority, string Title, string Impact, IReadOnlyList<RemediationOrderItem> Items);

internal sealed record RemediationOrderItem(
    string Issue, string Remediation, string Component, string Severity, string Authority, string Evidence,
    string? SpecReference, string? ClaimId = null, bool OutsideSelectedScope = false);
