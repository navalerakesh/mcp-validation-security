using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.Core.Services;

public static class ValidationEvidenceIdBuilder
{
    public static string ForExecutionError(string? message) => $"execution-error:{NormalizeToken(message)}";

    public static string ForTierCheck(ComplianceTierCheck check)
    {
        ArgumentNullException.ThrowIfNull(check);

        return $"tier-check:{NormalizeToken(check.Tier)}:{NormalizeToken(check.Component)}:{NormalizeToken(check.Requirement)}";
    }

    public static string ForComplianceViolation(ComplianceViolation violation)
    {
        ArgumentNullException.ThrowIfNull(violation);

        return $"protocol-violation:{NormalizeToken(FirstNonEmpty(violation.CheckId, violation.Rule))}:{NormalizeToken(violation.Category)}:{NormalizeToken(violation.Description)}";
    }

    public static string ForSecurityVulnerability(SecurityVulnerability vulnerability)
    {
        ArgumentNullException.ThrowIfNull(vulnerability);

        return $"security-vulnerability:{NormalizeToken(vulnerability.Id)}:{NormalizeToken(vulnerability.AffectedComponent)}:{NormalizeToken(vulnerability.Description)}";
    }

    public static string ForAttackSimulation(AttackSimulationResult attack)
    {
        ArgumentNullException.ThrowIfNull(attack);

        return $"attack-simulation:{NormalizeToken(attack.AttackVector)}:{NormalizeToken(attack.Description)}";
    }

    public static string ForBoundaryFinding(AiBoundaryFinding finding)
    {
        ArgumentNullException.ThrowIfNull(finding);

        return $"boundary-finding:{NormalizeToken(finding.Category)}:{NormalizeToken(finding.Component)}:{NormalizeToken(finding.Description)}";
    }

    public static string ForFinding(ValidationFinding finding)
    {
        ArgumentNullException.ThrowIfNull(finding);

        return $"structured-finding:{NormalizeToken(finding.RuleId)}:{NormalizeToken(finding.Component)}:{NormalizeToken(finding.Summary)}";
    }

    public static string ForContentSafetyFinding(ContentSafetyFinding finding)
    {
        ArgumentNullException.ThrowIfNull(finding);

        return $"content-safety:{NormalizeToken(finding.Axis.ToString())}:{NormalizeToken(finding.ItemName)}:{NormalizeToken(finding.Reason)}";
    }

    public static string ForCoverage(ValidationCoverageDeclaration coverage)
    {
        ArgumentNullException.ThrowIfNull(coverage);

        return $"coverage:{NormalizeToken(coverage.LayerId)}:{NormalizeToken(coverage.Scope)}:{coverage.Status}";
    }

    public static string ForProbe(ProbeContext probe)
    {
        ArgumentNullException.ThrowIfNull(probe);

        return $"probe:{NormalizeToken(FirstNonEmpty(probe.ProbeId, probe.RequestId, probe.Method))}";
    }

    public static string NormalizeToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "unknown";
        }

        var normalized = new string(value
            .Trim()
            .ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')
            .ToArray())
            .Trim('-');

        return string.IsNullOrWhiteSpace(normalized) ? "unknown" : normalized;
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }
}
