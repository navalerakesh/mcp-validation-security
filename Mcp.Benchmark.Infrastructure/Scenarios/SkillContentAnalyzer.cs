using System.Collections;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Mcp.Benchmark.Core.Constants;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Utilities;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace Mcp.Benchmark.Infrastructure.Scenarios;

internal static class SkillContentAnalyzer
{
    private const int MaximumSkillDocumentBytes = 1024 * 1024;
    private const int MaximumFrontmatterBytes = 256 * 1024;
    private const int MaximumYamlDepth = 32;
    private const int MaximumYamlEvents = 8192;
    private const int MaximumYamlAliases = 16;
    private const int MaximumYamlScalarLength = 64 * 1024;

    public static SkillContentAnalysis Analyze(
        SkillCatalogEntry entry,
        SkillResourceManifestEntry manifest,
        string rawJson)
    {
        var findings = new List<ValidationFinding>();
        if (!TryReadContent(entry.Uri, rawJson, findings, out var bytes, out var mimeType))
        {
            return new SkillContentAnalysis(entry.Uri, null, findings);
        }

        var digest = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (bytes.LongLength != manifest.Size ||
            !string.Equals(manifest.Digest, $"sha256:{digest}", StringComparison.Ordinal))
        {
            findings.Add(Finding(
                ValidationFindingRuleIds.SkillsContentIntegrityMismatch,
                entry.Uri,
                ValidationFindingSeverity.High,
                ValidationRuleSource.Spec,
                "Skill content size or SHA-256 digest does not match its catalog manifest.",
                "Refresh skills/get or skills/list and do not load content that fails manifest verification."));
        }

        if (!string.Equals(mimeType, "text/markdown", StringComparison.OrdinalIgnoreCase))
        {
            findings.Add(Finding(
                ValidationFindingRuleIds.SkillsContentIntegrityMismatch,
                entry.Uri,
                ValidationFindingSeverity.Medium,
                ValidationRuleSource.Guideline,
                "SKILL.md resource does not use the recommended text/markdown MIME type.",
                "Serve SKILL.md as text/markdown."));
        }

        var text = Encoding.UTF8.GetString(bytes);
        if (!TryParseFrontmatter(text, out var parsedFrontmatter, out var body))
        {
            findings.Add(Finding(
                ValidationFindingRuleIds.SkillsFrontmatterMismatch,
                entry.Uri,
                ValidationFindingSeverity.High,
                ValidationRuleSource.Spec,
                "SKILL.md frontmatter is missing, malformed, or exceeds parser bounds.",
                "Return bounded YAML frontmatter matching the catalog entry exactly."));
        }
        else
        {
            var expectedDigest = CanonicalJsonDigest.ComputeElement(entry.Frontmatter);
            var actualDigest = CanonicalJsonDigest.ComputeElement(parsedFrontmatter);
            if (!string.Equals(expectedDigest, actualDigest, StringComparison.Ordinal))
            {
                findings.Add(Finding(
                    ValidationFindingRuleIds.SkillsFrontmatterMismatch,
                    entry.Uri,
                    ValidationFindingSeverity.High,
                    ValidationRuleSource.Spec,
                    "SKILL.md frontmatter does not match the catalog entry field-for-field.",
                    "Regenerate the catalog entry from the exact SKILL.md frontmatter."));
            }
            AnalyzeSecurityPatterns(body, entry.Uri, findings);
        }

        return new SkillContentAnalysis(entry.Uri, digest, findings);
    }

    private static bool TryReadContent(
        string requestedUri,
        string rawJson,
        ICollection<ValidationFinding> findings,
        out byte[] bytes,
        out string? mimeType)
    {
        bytes = [];
        mimeType = null;
        using var document = JsonDocument.Parse(rawJson, new JsonDocumentOptions
        {
            MaxDepth = 64,
            CommentHandling = JsonCommentHandling.Disallow,
            AllowTrailingCommas = false
        });
        if (!document.RootElement.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Object ||
            !result.TryGetProperty("contents", out var contents) || contents.ValueKind != JsonValueKind.Array ||
            contents.GetArrayLength() != 1 || contents[0].ValueKind != JsonValueKind.Object)
        {
            findings.Add(InvalidContent(requestedUri, "resources/read must return exactly one SKILL.md content item."));
            return false;
        }

        var content = contents[0];
        if (!content.TryGetProperty("uri", out var uri) || uri.ValueKind != JsonValueKind.String ||
            !string.Equals(uri.GetString(), requestedUri, StringComparison.Ordinal))
        {
            findings.Add(InvalidContent(requestedUri, "resources/read returned a URI that does not match the requested skill."));
            return false;
        }
        mimeType = content.TryGetProperty("mimeType", out var mime) && mime.ValueKind == JsonValueKind.String
            ? mime.GetString()
            : null;
        var hasText = content.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String;
        var hasBlob = content.TryGetProperty("blob", out var blob) && blob.ValueKind == JsonValueKind.String;
        if (hasText == hasBlob)
        {
            findings.Add(InvalidContent(requestedUri, "Skill content must contain exactly one of text or blob."));
            return false;
        }

        try
        {
            bytes = hasText ? Encoding.UTF8.GetBytes(text.GetString()!) : Convert.FromBase64String(blob.GetString()!);
        }
        catch (FormatException)
        {
            findings.Add(InvalidContent(requestedUri, "Skill blob content is not valid base64."));
            return false;
        }
        if (bytes.Length > MaximumSkillDocumentBytes)
        {
            findings.Add(InvalidContent(requestedUri, "SKILL.md exceeds the bounded 1 MiB content-analysis limit."));
            return false;
        }
        return true;
    }

    private static bool TryParseFrontmatter(
        string text,
        out JsonElement frontmatter,
        out string body)
    {
        frontmatter = default;
        body = string.Empty;
        using var reader = new StringReader(text);
        if (!string.Equals(reader.ReadLine(), "---", StringComparison.Ordinal))
        {
            return false;
        }

        var yaml = new StringBuilder();
        string? line;
        while ((line = reader.ReadLine()) != null && !string.Equals(line, "---", StringComparison.Ordinal))
        {
            yaml.AppendLine(line);
            if (Encoding.UTF8.GetByteCount(yaml.ToString()) > MaximumFrontmatterBytes)
            {
                return false;
            }
        }
        if (line == null)
        {
            return false;
        }
        body = reader.ReadToEnd();

        try
        {
            if (!IsYamlWithinBounds(yaml.ToString()))
            {
                return false;
            }
            var deserializer = new DeserializerBuilder().Build();
            var value = deserializer.Deserialize<object>(yaml.ToString());
            frontmatter = JsonSerializer.SerializeToElement(NormalizeYaml(value));
            return frontmatter.ValueKind == JsonValueKind.Object;
        }
        catch (YamlException)
        {
            return false;
        }
    }

    private static bool IsYamlWithinBounds(string yaml)
    {
        var parser = new Parser(new StringReader(yaml));
        var depth = 0;
        var events = 0;
        var aliases = 0;
        while (parser.MoveNext())
        {
            events++;
            switch (parser.Current)
            {
                case MappingStart or SequenceStart:
                    depth++;
                    break;
                case MappingEnd or SequenceEnd:
                    depth--;
                    break;
                case AnchorAlias:
                    aliases++;
                    break;
                case Scalar scalar when scalar.Value?.Length > MaximumYamlScalarLength:
                    return false;
            }
            if (events > MaximumYamlEvents || depth > MaximumYamlDepth || aliases > MaximumYamlAliases || depth < 0)
            {
                return false;
            }
        }
        return depth == 0;
    }

    private static object? NormalizeYaml(object? value)
    {
        if (value is IDictionary dictionary)
        {
            var normalized = new SortedDictionary<string, object?>(StringComparer.Ordinal);
            foreach (DictionaryEntry item in dictionary)
            {
                if (item.Key is not string key)
                {
                    throw new YamlException("Frontmatter mapping keys must be strings.");
                }
                normalized[key] = NormalizeYaml(item.Value);
            }
            return normalized;
        }
        if (value is IEnumerable sequence and not string)
        {
            return sequence.Cast<object?>().Select(NormalizeYaml).ToArray();
        }
        return value;
    }

    private static void AnalyzeSecurityPatterns(
        string body,
        string component,
        ICollection<ValidationFinding> findings)
    {
        var normalized = body.ToLowerInvariant();
        AddPatternFinding(
            ScoringConstants.PromptInjectionPatterns,
            ValidationFindingRuleIds.SkillsPromptInjectionRisk,
            "Skill content contains prompt-injection language.",
            "Remove instructions that override higher-authority model or user directives.");
        AddPatternFinding(
            ScoringConstants.SkillSecretRequestPatterns,
            ValidationFindingRuleIds.SkillsSecretRequestRisk,
            "Skill content requests secrets or credentials.",
            "Do not request secrets through model instructions; use governed authentication flows.");
        AddPatternFinding(
            ScoringConstants.SkillCodeExecutionPatterns,
            ValidationFindingRuleIds.SkillsCodeExecutionRisk,
            "Skill content directs host-side code execution.",
            "Require explicit per-skill approval before any code-execution tool call.");

        void AddPatternFinding(
            IEnumerable<string> patterns,
            string ruleId,
            string summary,
            string recommendation)
        {
            if (patterns.Any(pattern => normalized.Contains(pattern, StringComparison.Ordinal)))
            {
                findings.Add(Finding(
                    ruleId,
                    component,
                    ValidationFindingSeverity.High,
                    ValidationRuleSource.Heuristic,
                    summary,
                    recommendation));
            }
        }
    }

    private static ValidationFinding InvalidContent(string component, string summary) => Finding(
        ValidationFindingRuleIds.SkillsContentIntegrityMismatch,
        component,
        ValidationFindingSeverity.High,
        ValidationRuleSource.Spec,
        summary,
        "Return the exact bounded SKILL.md bytes declared by the current manifest.");

    private static ValidationFinding Finding(
        string ruleId,
        string component,
        ValidationFindingSeverity severity,
        ValidationRuleSource source,
        string summary,
        string recommendation) => new()
        {
            RuleId = ruleId,
            Category = "MCPSkills",
            Component = component,
            Severity = severity,
            Source = source,
            Summary = summary,
            Recommendation = recommendation,
            SpecReference = McpExtensionCatalog.Skills.SpecificationUrl,
            ExcludedFromAggregate = true
        };
}

internal sealed record SkillContentAnalysis(
    string SkillUri,
    string? ContentSha256,
    IReadOnlyList<ValidationFinding> Findings);