using System.Text.RegularExpressions;
using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Models;
using Microsoft.Extensions.Logging;

namespace Mcp.Benchmark.Infrastructure.Services;

/// <summary>
/// Default implementation of <see cref="IContentSafetyAnalyzer"/> that
/// performs lightweight, metadata-only risk analysis for tools, resources,
/// and prompts. It relies on simple keyword heuristics over names,
/// descriptions, and URIs, then calibrates risk by declared server context
/// and observed AI-safety controls when provided.
/// </summary>
public class ContentSafetyAnalyzer : IContextualContentSafetyAnalyzer
{
    private readonly ILogger<ContentSafetyAnalyzer> _logger;

    private static readonly string[] SystemImpactHighKeywords =
    {
        "delete", "remove", "destroy", "drop", "truncate", "wipe",
        "shutdown", "terminate", "kill", "lock", "ban",
        "execute", "exec", "shell", "command", "powershell", "bash",
        "admin", "root"
    };

    private static readonly string[] SystemImpactMediumKeywords =
    {
        "update", "modify", "change", "set", "write", "patch"
    };

    private static readonly string[] DataExfiltrationHighKeywords =
    {
        "dump", "download", "export", "backup", "snapshot",
        "all-data", "fulldump", "full-dump"
    };

    private static readonly string[] DataExfiltrationMediumKeywords =
    {
        "list-all", "history", "logs", "audit", "report"
    };

    private static readonly string[] AbuseHighKeywords =
    {
        "broadcast", "notify-all", "email-all", "message-all",
        "spam", "bulk-send"
    };

    private static readonly string[] AbuseMediumKeywords =
    {
        "notify", "email", "message", "post"
    };

    public ContentSafetyAnalyzer(ILogger<ContentSafetyAnalyzer> logger)
    {
        _logger = logger;
    }

    public IReadOnlyList<ContentSafetyFinding> AnalyzeTool(string toolName) =>
        AnalyzeTool(toolName, new ContentSafetyAnalysisContext());

    public IReadOnlyList<ContentSafetyFinding> AnalyzeTool(string toolName, ContentSafetyAnalysisContext context)
    {
        if (string.IsNullOrWhiteSpace(toolName))
        {
            return Array.Empty<ContentSafetyFinding>();
        }

        var normalized = Normalize(toolName);
        var findings = new List<ContentSafetyFinding>();

        AddAxisFindings(findings, ContentItemKind.Tool, toolName, normalized, context);

        if (findings.Count > 0)
        {
            _logger.LogDebug("Content safety: {Count} findings for tool {Tool}", findings.Count, toolName);
        }

        return findings;
    }

    public IReadOnlyList<ContentSafetyFinding> AnalyzeResource(string? resourceName, string resourceUri) =>
        AnalyzeResource(resourceName, resourceUri, new ContentSafetyAnalysisContext());

    public IReadOnlyList<ContentSafetyFinding> AnalyzeResource(string? resourceName, string resourceUri, ContentSafetyAnalysisContext context)
    {
        if (string.IsNullOrWhiteSpace(resourceUri))
        {
            return Array.Empty<ContentSafetyFinding>();
        }

        var label = string.IsNullOrWhiteSpace(resourceName) ? resourceUri : resourceName!;
        var combined = string.IsNullOrWhiteSpace(resourceName)
            ? resourceUri
            : resourceName + " " + resourceUri;

        var normalized = Normalize(combined);
        var findings = new List<ContentSafetyFinding>();

        AddAxisFindings(findings, ContentItemKind.Resource, label, normalized, context);

        if (findings.Count > 0)
        {
            _logger.LogDebug("Content safety: {Count} findings for resource {Resource}", findings.Count, label);
        }

        return findings;
    }

    public IReadOnlyList<ContentSafetyFinding> AnalyzePrompt(string promptName, string? description, int argumentsCount) =>
        AnalyzePrompt(promptName, description, argumentsCount, new ContentSafetyAnalysisContext());

    public IReadOnlyList<ContentSafetyFinding> AnalyzePrompt(string promptName, string? description, int argumentsCount, ContentSafetyAnalysisContext context)
    {
        if (string.IsNullOrWhiteSpace(promptName) && string.IsNullOrWhiteSpace(description))
        {
            return Array.Empty<ContentSafetyFinding>();
        }

        var label = string.IsNullOrWhiteSpace(promptName) ? description ?? string.Empty : promptName;
        var combined = string.IsNullOrWhiteSpace(description)
            ? label
            : label + " " + description;

        var normalized = Normalize(combined);
        var findings = new List<ContentSafetyFinding>();

        AddAxisFindings(findings, ContentItemKind.Prompt, label, normalized, context, argumentsCount);

        if (findings.Count > 0)
        {
            _logger.LogDebug("Content safety: {Count} findings for prompt {Prompt}", findings.Count, label);
        }

        return findings;
    }

    private static void AddAxisFindings(
        List<ContentSafetyFinding> findings,
        ContentItemKind kind,
        string itemName,
        string normalized,
        ContentSafetyAnalysisContext context,
        int argumentsCount = 0)
    {
        // System impact (create/update/delete/execute)
        AddFindingIfMatched(findings, kind, itemName, normalized,
            ContentRiskAxis.SystemImpact,
            SystemImpactHighKeywords,
            SystemImpactMediumKeywords,
            context);

        // Data exfiltration (dump/export/all data)
        AddFindingIfMatched(findings, kind, itemName, normalized,
            ContentRiskAxis.DataExfiltration,
            DataExfiltrationHighKeywords,
            DataExfiltrationMediumKeywords,
            context);

        // Abuse / mass messaging
        AddFindingIfMatched(findings, kind, itemName, normalized,
            ContentRiskAxis.Abuse,
            AbuseHighKeywords,
            AbuseMediumKeywords,
            context);

        // Very argument-rich prompts can be higher risk for system impact
        if (kind == ContentItemKind.Prompt && argumentsCount > 5)
        {
            var finding = new ContentSafetyFinding
            {
                ItemKind = kind,
                ItemName = itemName,
                Axis = ContentRiskAxis.SystemImpact,
                RiskLevel = ContentRiskLevel.Medium,
                RiskScore = 65.0,
                Reason = "Prompt accepts many arguments; ensure strict validation and scoping.",
                Recommendation = "Review this prompt's intended use and enforce least-privilege access to underlying tools/resources.",
                Context =
                {
                    ["argumentsCount"] = argumentsCount
                }
            };

            CalibrateFinding(finding, context, ContentRiskLevel.Medium, 65.0);
            findings.Add(finding);
        }
    }

    private static void AddFindingIfMatched(
        List<ContentSafetyFinding> findings,
        ContentItemKind kind,
        string itemName,
        string normalized,
        ContentRiskAxis axis,
        IEnumerable<string> highKeywords,
        IEnumerable<string> mediumKeywords,
        ContentSafetyAnalysisContext context)
    {
        var matchedHigh = FindFirstMatch(normalized, highKeywords);
        var matchedMedium = matchedHigh == null
            ? FindFirstMatch(normalized, mediumKeywords)
            : null;

        if (matchedHigh == null && matchedMedium == null)
        {
            return;
        }

        var isHigh = matchedHigh != null;
        var keyword = matchedHigh ?? matchedMedium!;

        var level = isHigh ? ContentRiskLevel.High : ContentRiskLevel.Medium;
        var score = isHigh ? 90.0 : 60.0;

        var axisLabel = axis switch
        {
            ContentRiskAxis.Abuse => "abusive or mass messaging",
            ContentRiskAxis.DataExfiltration => "data exfiltration",
            ContentRiskAxis.SystemImpact => "system impact",
            _ => "content risk"
        };

        var reason = $"Name/URI suggests potential {axisLabel} capability (matched keyword: '{keyword}').";

        var recommendation = axis switch
        {
            ContentRiskAxis.Abuse =>
                "Restrict access to this operation, apply rate limits, and ensure proper auditing.",
            ContentRiskAxis.DataExfiltration =>
                "Limit who can invoke this capability, protect sensitive fields, and consider redaction or aggregation.",
            ContentRiskAxis.SystemImpact =>
                "Require strong authentication/authorization, validate inputs, and log all state-changing operations.",
            _ =>
                "Review this capability and apply least-privilege and strong validation."
        };

        var finding = new ContentSafetyFinding
        {
            ItemKind = kind,
            ItemName = itemName,
            Axis = axis,
            RiskLevel = level,
            RiskScore = score,
            Reason = reason,
            Recommendation = recommendation,
            Context =
            {
                ["matchedKeyword"] = keyword
            }
        };

        CalibrateFinding(finding, context, level, score);
        findings.Add(finding);
    }

    private static void CalibrateFinding(
        ContentSafetyFinding finding,
        ContentSafetyAnalysisContext? context,
        ContentRiskLevel baseLevel,
        double baseScore)
    {
        context ??= new ContentSafetyAnalysisContext();

        var adjustedLevel = baseLevel;
        var adjustedScore = baseScore;
        var adjustment = "none";
        var hasRelevantControls = HasRelevantDeclaredControls(context, finding.Axis);

        switch (context.Profile)
        {
            case ContentSafetyContextProfile.PublicUnauthenticated:
                adjustedLevel = IncreaseRisk(baseLevel);
                adjustedScore = Math.Max(baseScore, adjustedLevel == ContentRiskLevel.High ? 95.0 : 75.0);
                adjustment = adjustedLevel == baseLevel ? "public-anonymous-score-increase" : "public-anonymous-escalation";
                break;

            case ContentSafetyContextProfile.PublicAuthenticated:
                adjustedScore = Math.Max(baseScore, baseLevel == ContentRiskLevel.High ? 90.0 : 65.0);
                adjustment = "public-authenticated-calibration";
                break;

            case ContentSafetyContextProfile.EnterpriseGoverned when hasRelevantControls:
                adjustedLevel = DecreaseRisk(baseLevel);
                adjustedScore = ScoreFor(adjustedLevel, fallbackScore: Math.Min(baseScore, 70.0));
                adjustment = adjustedLevel == baseLevel ? "enterprise-controls-confirmed" : "enterprise-controls-reduced-risk";
                break;

            case ContentSafetyContextProfile.EnterpriseGoverned:
                adjustedScore = Math.Max(baseScore, baseLevel == ContentRiskLevel.High ? 88.0 : 62.0);
                adjustment = "enterprise-controls-not-observed";
                break;

            case ContentSafetyContextProfile.LocalDeveloper:
                adjustedLevel = DecreaseRisk(baseLevel);
                adjustedScore = ScoreFor(adjustedLevel, fallbackScore: Math.Min(baseScore, 70.0));
                adjustment = adjustedLevel == baseLevel ? "local-development-context" : "local-development-reduced-risk";
                break;

            case ContentSafetyContextProfile.CIOnly:
                adjustedScore = finding.Axis == ContentRiskAxis.SystemImpact
                    ? Math.Max(baseScore, baseLevel == ContentRiskLevel.High ? 92.0 : 68.0)
                    : baseScore;
                adjustment = "ci-only-calibration";
                break;

            case ContentSafetyContextProfile.Internal when hasRelevantControls:
                adjustedLevel = DecreaseRisk(baseLevel);
                adjustedScore = ScoreFor(adjustedLevel, fallbackScore: Math.Min(baseScore, 72.0));
                adjustment = adjustedLevel == baseLevel ? "internal-controls-confirmed" : "internal-controls-reduced-risk";
                break;

            case ContentSafetyContextProfile.Internal:
                adjustedScore = Math.Min(Math.Max(baseScore, 55.0), baseLevel == ContentRiskLevel.High ? 85.0 : 65.0);
                adjustment = "internal-context-without-observed-controls";
                break;
        }

        finding.RiskLevel = adjustedLevel;
        finding.RiskScore = Math.Clamp(adjustedScore, 0.0, 100.0);
        AddContextMetadata(finding, context, baseLevel, baseScore, adjustment, hasRelevantControls);

        var contextRecommendation = BuildContextRecommendation(context.Profile, hasRelevantControls);
        if (!string.IsNullOrWhiteSpace(contextRecommendation))
        {
            finding.Recommendation = string.IsNullOrWhiteSpace(finding.Recommendation)
                ? contextRecommendation
                : contextRecommendation + " " + finding.Recommendation;
        }
    }

    private static ContentRiskLevel IncreaseRisk(ContentRiskLevel level)
    {
        return level switch
        {
            ContentRiskLevel.None => ContentRiskLevel.Low,
            ContentRiskLevel.Low => ContentRiskLevel.Medium,
            ContentRiskLevel.Medium => ContentRiskLevel.High,
            _ => ContentRiskLevel.High
        };
    }

    private static ContentRiskLevel DecreaseRisk(ContentRiskLevel level)
    {
        return level switch
        {
            ContentRiskLevel.High => ContentRiskLevel.Medium,
            ContentRiskLevel.Medium => ContentRiskLevel.Low,
            ContentRiskLevel.Low => ContentRiskLevel.Low,
            _ => ContentRiskLevel.None
        };
    }

    private static double ScoreFor(ContentRiskLevel level, double fallbackScore)
    {
        return level switch
        {
            ContentRiskLevel.High => Math.Max(fallbackScore, 88.0),
            ContentRiskLevel.Medium => Math.Min(fallbackScore, 70.0),
            ContentRiskLevel.Low => Math.Min(fallbackScore, 40.0),
            _ => 0.0
        };
    }

    private static bool HasRelevantDeclaredControls(ContentSafetyAnalysisContext context, ContentRiskAxis axis)
    {
        if (context.ObservedControls.Count == 0)
        {
            return false;
        }

        var hasAuditTrail = HasDeclaredControl(context, AiSafetyControlKind.AuditTrail);
        var hasUserConfirmation = HasDeclaredControl(context, AiSafetyControlKind.UserConfirmation) ||
                                  HasDeclaredControl(context, AiSafetyControlKind.DestructiveActionConfirmation);
        var hasDataDisclosure = axis != ContentRiskAxis.DataExfiltration ||
                                HasDeclaredControl(context, AiSafetyControlKind.DataSharingDisclosure);

        return hasAuditTrail && hasUserConfirmation && hasDataDisclosure;
    }

    private static bool HasDeclaredControl(ContentSafetyAnalysisContext context, AiSafetyControlKind controlKind)
    {
        return context.ObservedControls.Any(control =>
            control.ControlKind == controlKind && control.Status == AiSafetyControlStatus.Declared);
    }

    private static void AddContextMetadata(
        ContentSafetyFinding finding,
        ContentSafetyAnalysisContext context,
        ContentRiskLevel baseLevel,
        double baseScore,
        string adjustment,
        bool hasRelevantControls)
    {
        finding.Context["contextProfile"] = context.Profile.ToString();
        finding.Context["serverProfile"] = context.ServerProfile.ToString();
        finding.Context["authenticationRequired"] = context.AuthenticationRequired;
        finding.Context["baseRiskLevel"] = baseLevel.ToString();
        finding.Context["baseRiskScore"] = baseScore;
        finding.Context["severityAdjustment"] = adjustment;
        finding.Context["relevantControlsDeclared"] = hasRelevantControls;

        var declaredControls = context.ObservedControls
            .Where(control => control.Status == AiSafetyControlStatus.Declared)
            .Select(control => control.ControlKind.ToString())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(control => control, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (declaredControls.Length > 0)
        {
            finding.Context["declaredControls"] = declaredControls;
        }
    }

    private static string? BuildContextRecommendation(ContentSafetyContextProfile profile, bool hasRelevantControls)
    {
        return profile switch
        {
            ContentSafetyContextProfile.PublicUnauthenticated =>
                "Do not expose this capability anonymously; require authentication, per-user authorization, rate limits, and audit before public use.",
            ContentSafetyContextProfile.PublicAuthenticated =>
                "Scope public access tokens narrowly and record user-level audit evidence for this capability.",
            ContentSafetyContextProfile.EnterpriseGoverned when hasRelevantControls =>
                "Keep the declared enterprise approval, confirmation, and audit controls attached to this capability.",
            ContentSafetyContextProfile.EnterpriseGoverned =>
                "Document the enterprise policy controls, approval path, and audit trail that govern this capability.",
            ContentSafetyContextProfile.LocalDeveloper =>
                "Keep this capability bound to local development contexts and avoid publishing it on shared or public endpoints.",
            ContentSafetyContextProfile.CIOnly =>
                "Restrict invocation to dedicated CI service identities, short-lived credentials, and auditable pipeline runs.",
            ContentSafetyContextProfile.Internal =>
                "Restrict this capability to authenticated internal users or networks and confirm audit coverage.",
            _ => null
        };
    }

    private static string? FindFirstMatch(string text, IEnumerable<string> keywords)
    {
        foreach (var keyword in keywords)
        {
            if (string.IsNullOrWhiteSpace(keyword))
            {
                continue;
            }

            if (text.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                return keyword;
            }
        }

        return null;
    }

    private static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        // Collapse whitespace and lower-case for simple keyword checks
        var normalized = Regex.Replace(value, @"\s+", " ").Trim();
        return normalized.ToLowerInvariant();
    }
}
