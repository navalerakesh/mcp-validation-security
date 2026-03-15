using System.Text.RegularExpressions;
using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Models;
using Microsoft.Extensions.Logging;

namespace Mcp.Benchmark.Infrastructure.Services;

/// <summary>
/// Default implementation of <see cref="IContentSafetyAnalyzer"/> that
/// performs lightweight, metadata-only risk analysis for tools, resources,
/// and prompts. It relies on simple keyword heuristics over names,
/// descriptions, and URIs and is intentionally MCP-server agnostic.
/// </summary>
public class ContentSafetyAnalyzer : IContentSafetyAnalyzer
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

    public IReadOnlyList<ContentSafetyFinding> AnalyzeTool(string toolName)
    {
        if (string.IsNullOrWhiteSpace(toolName))
        {
            return Array.Empty<ContentSafetyFinding>();
        }

        var normalized = Normalize(toolName);
        var findings = new List<ContentSafetyFinding>();

        AddAxisFindings(findings, ContentItemKind.Tool, toolName, normalized);

        if (findings.Count > 0)
        {
            _logger.LogDebug("Content safety: {Count} findings for tool {Tool}", findings.Count, toolName);
        }

        return findings;
    }

    public IReadOnlyList<ContentSafetyFinding> AnalyzeResource(string? resourceName, string resourceUri)
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

        AddAxisFindings(findings, ContentItemKind.Resource, label, normalized);

        if (findings.Count > 0)
        {
            _logger.LogDebug("Content safety: {Count} findings for resource {Resource}", findings.Count, label);
        }

        return findings;
    }

    public IReadOnlyList<ContentSafetyFinding> AnalyzePrompt(string promptName, string? description, int argumentsCount)
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

        AddAxisFindings(findings, ContentItemKind.Prompt, label, normalized, argumentsCount);

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
        int argumentsCount = 0)
    {
        // System impact (create/update/delete/execute)
        AddFindingIfMatched(findings, kind, itemName, normalized,
            ContentRiskAxis.SystemImpact,
            SystemImpactHighKeywords,
            SystemImpactMediumKeywords);

        // Data exfiltration (dump/export/all data)
        AddFindingIfMatched(findings, kind, itemName, normalized,
            ContentRiskAxis.DataExfiltration,
            DataExfiltrationHighKeywords,
            DataExfiltrationMediumKeywords);

        // Abuse / mass messaging
        AddFindingIfMatched(findings, kind, itemName, normalized,
            ContentRiskAxis.Abuse,
            AbuseHighKeywords,
            AbuseMediumKeywords);

        // Very argument-rich prompts can be higher risk for system impact
        if (kind == ContentItemKind.Prompt && argumentsCount > 5)
        {
            findings.Add(new ContentSafetyFinding
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
            });
        }
    }

    private static void AddFindingIfMatched(
        List<ContentSafetyFinding> findings,
        ContentItemKind kind,
        string itemName,
        string normalized,
        ContentRiskAxis axis,
        IEnumerable<string> highKeywords,
        IEnumerable<string> mediumKeywords)
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

        findings.Add(new ContentSafetyFinding
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
        });
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
