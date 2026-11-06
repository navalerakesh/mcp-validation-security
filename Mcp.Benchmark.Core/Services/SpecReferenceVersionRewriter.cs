using System.Text.RegularExpressions;
using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.Core.Services;

/// <summary>
/// Rewrites embedded MCP spec URLs (e.g. <c>spec.modelcontextprotocol.io/specification/2025-11-25/...</c>)
/// inside a <see cref="ValidationResult"/> so they reference the protocol version that was
/// actually negotiated with the server. This avoids citing the latest revision when the
/// session ran against an older one.
/// </summary>
/// <remarks>
/// The rewriter only substitutes when the negotiated version is non-null, syntactically
/// looks like an MCP date-based version, and is part of the supplied allow-list of known
/// embedded versions. Otherwise the canonical URL is left untouched.
/// </remarks>
public static class SpecReferenceVersionRewriter
{
    // Matches both the canonical (legacy) host spec.modelcontextprotocol.io and the
    // current modelcontextprotocol.io paths.
    private static readonly Regex SpecUrlVersionPattern = new(
        @"(?<prefix>(?:spec\.modelcontextprotocol\.io|modelcontextprotocol\.io)/specification/)(?<version>\d{4}-\d{2}-\d{2})(?=[/\)\.\s,;""'<#]|$)",
        RegexOptions.Compiled);

    private static readonly Regex VersionDatePattern = new(@"^\d{4}-\d{2}-\d{2}$", RegexOptions.Compiled);

    /// <summary>
    /// Walks the obvious string surfaces on <paramref name="result"/> and rewrites any
    /// hardcoded MCP spec URL versions to <paramref name="negotiatedVersion"/>.
    /// </summary>
    /// <param name="result">The validation result to mutate in-place.</param>
    /// <param name="negotiatedVersion">The version negotiated with the server.</param>
    /// <param name="knownEmbeddedVersions">The set of versions for which embedded schema docs exist.</param>
    public static void Apply(ValidationResult result, string? negotiatedVersion, IReadOnlyCollection<string> knownEmbeddedVersions)
    {
        if (result is null) return;
        if (string.IsNullOrWhiteSpace(negotiatedVersion)) return;
        if (!VersionDatePattern.IsMatch(negotiatedVersion)) return;
        if (knownEmbeddedVersions is null || knownEmbeddedVersions.Count == 0) return;

        // Only rewrite to versions we have embedded schemas for. This prevents citing
        // a date-shaped string the server invented (or a draft spec) as if it were
        // an authoritative MCP revision.
        var hasMatch = false;
        foreach (var v in knownEmbeddedVersions)
        {
            if (string.Equals(v, negotiatedVersion, StringComparison.Ordinal))
            {
                hasMatch = true;
                break;
            }
        }
        if (!hasMatch) return;

        string Rewrite(string? value) => string.IsNullOrEmpty(value)
            ? value ?? string.Empty
            : SpecUrlVersionPattern.Replace(value!, m => m.Groups["prefix"].Value + negotiatedVersion);

        // Top-level recommendations
        for (int i = 0; i < result.Recommendations.Count; i++)
        {
            result.Recommendations[i] = Rewrite(result.Recommendations[i]);
        }

        // ProtocolCompliance violations
        if (result.ProtocolCompliance?.Violations is { } pcv)
        {
            foreach (var v in pcv)
            {
                v.SpecReference = Rewrite(v.SpecReference);
                v.Recommendation = Rewrite(v.Recommendation);
            }
        }

        // Walk all Findings collections on the validator subdocuments
        RewriteFindings(result.ProtocolCompliance?.Findings, Rewrite);
        RewriteFindings(result.ToolValidation?.Findings, Rewrite);
        RewriteFindings(result.ResourceTesting?.Findings, Rewrite);
        RewriteFindings(result.PromptTesting?.Findings, Rewrite);
        RewriteFindings(result.SecurityTesting?.Findings, Rewrite);
        RewriteFindings(result.PerformanceTesting?.Findings, Rewrite);
        RewriteFindings(result.ErrorHandling?.Findings, Rewrite);

        // Per-tool / per-resource / per-prompt finding lists
        if (result.ToolValidation?.ToolResults is { } toolResults)
        {
            foreach (var tr in toolResults)
            {
                RewriteFindings(tr.Findings, Rewrite);
            }
            RewriteFindings(result.ToolValidation.AiReadinessFindings, Rewrite);
        }
        if (result.ResourceTesting?.ResourceResults is { } resourceResults)
        {
            foreach (var rr in resourceResults)
            {
                RewriteFindings(rr.Findings, Rewrite);
            }
        }
        if (result.PromptTesting?.PromptResults is { } promptResults)
        {
            foreach (var pr in promptResults)
            {
                RewriteFindings(pr.Findings, Rewrite);
            }
        }

        // Evidence observations carry their own metadata["specReference"] copies.
        if (result.Evidence?.Observations is { } observations)
        {
            foreach (var obs in observations)
            {
                if (obs.Metadata.TryGetValue("specReference", out var specRef))
                {
                    obs.Metadata["specReference"] = Rewrite(specRef);
                }
            }
        }

    }

    private static void RewriteFindings(IList<ValidationFinding>? findings, Func<string?, string> rewrite)
    {
        if (findings is null) return;
        foreach (var f in findings)
        {
            f.SpecReference = rewrite(f.SpecReference);
            f.Recommendation = rewrite(f.Recommendation);
            f.Summary = rewrite(f.Summary);
        }
    }
}
