using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.Core.Services;

public static class AiSafetyControlAnalyzer
{
    private static readonly string[] ConfirmationCues =
    {
        "confirm",
        "confirmation",
        "approve",
        "approval",
        "warn",
        "warning",
        "review",
        "consent"
    };

    private static readonly string[] ConfirmationNegations =
    {
        "without confirmation",
        "no confirmation",
        "without approval",
        "no approval",
        "without consent",
        "no consent"
    };

    private static readonly string[] AuditCues =
    {
        "audit",
        "logged",
        "logging",
        "recorded",
        "trace",
        "approval log"
    };

    private static readonly string[] DestructiveNameCues =
    {
        "delete",
        "remove",
        "destroy",
        "drop",
        "truncate",
        "wipe",
        "shutdown",
        "terminate",
        "kill",
        "write",
        "update",
        "patch",
        "modify"
    };

    public static AiSafetyControlAnalysis AnalyzeTool(AiSafetyControlTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);

        var evidence = new List<AiSafetyControlEvidence>();
        var isDestructive = IsPotentiallyDestructive(target);
        var hasConfirmationLanguage = HasHumanConfirmationLanguage(target.Description);
        var hasAuditLanguage = HasAuditLanguage(target.Description);

        evidence.Add(BuildUserConfirmationEvidence(target, isDestructive, hasConfirmationLanguage));
        evidence.Add(BuildDestructiveConfirmationEvidence(target, isDestructive, hasConfirmationLanguage));
        evidence.Add(BuildDataSharingEvidence(target));
        evidence.Add(BuildAuditEvidence(target, isDestructive, hasAuditLanguage));
        evidence.Add(BuildHostResponsibilityEvidence(target));

        return new AiSafetyControlAnalysis { Evidence = evidence };
    }

    public static bool HasHumanConfirmationLanguage(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return false;
        }

        if (ConfirmationNegations.Any(phrase => description.Contains(phrase, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        return ConfirmationCues.Any(phrase => description.Contains(phrase, StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasAuditLanguage(string? description)
    {
        return !string.IsNullOrWhiteSpace(description)
               && AuditCues.Any(phrase => description.Contains(phrase, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsPotentiallyDestructive(AiSafetyControlTarget target)
    {
        if (target.ReadOnlyHint == true && target.DestructiveHint == false)
        {
            return false;
        }

        if (target.DestructiveHint == true)
        {
            return true;
        }

        var text = string.Join(' ', target.Name, target.Description, string.Join(' ', target.ParameterNames));
        return DestructiveNameCues.Any(cue => text.Contains(cue, StringComparison.OrdinalIgnoreCase));
    }

    private static AiSafetyControlEvidence BuildUserConfirmationEvidence(AiSafetyControlTarget target, bool isDestructive, bool hasConfirmationLanguage)
    {
        if (!isDestructive)
        {
            return Create(target, AiSafetyControlKind.UserConfirmation, AiSafetyControlStatus.NotApplicable,
                "No destructive tool behavior was declared or inferred from tool metadata.",
                null,
                ValidationRuleSource.Guideline);
        }

        if (hasConfirmationLanguage)
        {
            return Create(target, AiSafetyControlKind.UserConfirmation, AiSafetyControlStatus.Declared,
                "Tool metadata declares human confirmation, approval, warning, review, or consent guidance.",
                null,
                ValidationRuleSource.Guideline);
        }

        return Create(target, AiSafetyControlKind.UserConfirmation, AiSafetyControlStatus.Missing,
            "Tool metadata does not declare human confirmation or consent guidance for a potentially destructive action.",
            "Document the expected confirmation/approval flow and ensure the host can deny the invocation before execution.",
            ValidationRuleSource.Heuristic);
    }

    private static AiSafetyControlEvidence BuildDestructiveConfirmationEvidence(AiSafetyControlTarget target, bool isDestructive, bool hasConfirmationLanguage)
    {
        if (!isDestructive)
        {
            return Create(target, AiSafetyControlKind.DestructiveActionConfirmation, AiSafetyControlStatus.NotApplicable,
                "Tool is not declared or inferred as destructive.",
                null,
                ValidationRuleSource.Guideline);
        }

        if (hasConfirmationLanguage)
        {
            return Create(target, AiSafetyControlKind.DestructiveActionConfirmation, AiSafetyControlStatus.Declared,
                "Destructive action confirmation guidance is declared in tool metadata.",
                null,
                ValidationRuleSource.Guideline);
        }

        return Create(target, AiSafetyControlKind.DestructiveActionConfirmation, AiSafetyControlStatus.Missing,
            "Destructive action confirmation guidance is missing from tool metadata.",
            "Add explicit confirmation, approval, warning, or consent language for destructive operations.",
            ValidationRuleSource.Guideline);
    }

    private static AiSafetyControlEvidence BuildDataSharingEvidence(AiSafetyControlTarget target)
    {
        if (target.OpenWorldHint.HasValue)
        {
            var scope = target.OpenWorldHint.Value
                ? "Tool declares that it may interact with external systems."
                : "Tool declares that it does not intentionally interact with external systems.";
            return Create(target, AiSafetyControlKind.DataSharingDisclosure, AiSafetyControlStatus.Declared,
                scope,
                null,
                ValidationRuleSource.Guideline);
        }

        return Create(target, AiSafetyControlKind.DataSharingDisclosure, AiSafetyControlStatus.Missing,
            "Tool metadata does not declare open-world/data-sharing behavior.",
            "Declare annotations.openWorldHint so hosts can explain data-sharing boundaries to users.",
            ValidationRuleSource.Guideline);
    }

    private static AiSafetyControlEvidence BuildAuditEvidence(AiSafetyControlTarget target, bool isDestructive, bool hasAuditLanguage)
    {
        if (hasAuditLanguage)
        {
            return Create(target, AiSafetyControlKind.AuditTrail, AiSafetyControlStatus.Declared,
                "Tool metadata mentions audit, logging, trace, or recording behavior.",
                null,
                ValidationRuleSource.Heuristic);
        }

        return Create(target, AiSafetyControlKind.AuditTrail, AiSafetyControlStatus.NotObservable,
            isDestructive
                ? "Audit trail behavior is not observable from tool metadata for this potentially destructive tool."
                : "Audit trail behavior is not observable from tool metadata.",
            "Confirm that the deployment logs tool invocations, approvals, denials, and actor identity where appropriate.",
            ValidationRuleSource.Heuristic);
    }

    private static AiSafetyControlEvidence BuildHostResponsibilityEvidence(AiSafetyControlTarget target)
    {
        return Create(target, AiSafetyControlKind.HostServerResponsibilitySplit, AiSafetyControlStatus.NotObservable,
            "Host-side consent, denial, and disclosure UX cannot be proven from server tool metadata alone.",
            "Document which controls are enforced by the MCP host and which are declared or enforced by the server.",
            ValidationRuleSource.Guideline);
    }

    private static AiSafetyControlEvidence Create(
        AiSafetyControlTarget target,
        AiSafetyControlKind controlKind,
        AiSafetyControlStatus status,
        string summary,
        string? recommendation,
        ValidationRuleSource authority)
    {
        return new AiSafetyControlEvidence
        {
            SubjectName = target.Name,
            ControlKind = controlKind,
            Status = status,
            Summary = summary,
            Recommendation = recommendation,
            Authority = authority,
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["readOnlyHint"] = target.ReadOnlyHint?.ToString() ?? "unknown",
                ["destructiveHint"] = target.DestructiveHint?.ToString() ?? "unknown",
                ["openWorldHint"] = target.OpenWorldHint?.ToString() ?? "unknown"
            }
        };
    }
}