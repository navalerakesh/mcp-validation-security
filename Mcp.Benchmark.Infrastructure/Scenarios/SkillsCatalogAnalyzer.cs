using System.Text.Json;
using System.Text.RegularExpressions;
using Mcp.Benchmark.Core.Constants;
using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.Infrastructure.Scenarios;

internal static partial class SkillsCatalogAnalyzer
{
    private const int MaximumResourcesPerSkill = 512;
    private const long MaximumTotalBytesPerSkill = 16 * 1024 * 1024;
    private static readonly string[] RiskBearingFrontmatterKeys = ["allowed-tools", "scripts", "hooks"];

    public static SkillsCatalogAnalysis Analyze(IReadOnlyList<string> pageJson)
    {
        var findings = new List<ValidationFinding>();
        var observations = new List<ValidationObservation>();
        var entries = new List<JsonElement>();
        var catalogEntries = new List<SkillCatalogEntry>();
        var seenUris = new HashSet<string>(StringComparer.Ordinal);
        var names = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var json in pageJson)
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                MaxDepth = 64,
                CommentHandling = JsonCommentHandling.Disallow,
                AllowTrailingCommas = false
            });
            if (!TryGetResult(document.RootElement, out var result) ||
                !TryGetRequiredCompleteResult(result) ||
                !result.TryGetProperty("skills", out var skills) ||
                skills.ValueKind != JsonValueKind.Array)
            {
                findings.Add(Finding(
                    ValidationFindingRuleIds.SkillsListResultInvalid,
                    McpSpecConstants.ExtensionMethods.SkillsList,
                    ValidationFindingSeverity.High,
                    ValidationRuleSource.Spec,
                    "skills/list must return a complete result containing a skills array.",
                    "Return resultType 'complete' and a skills array."));
                continue;
            }

            if (!result.TryGetProperty("ttlMs", out var ttlMs) || !ttlMs.TryGetInt64(out var ttlValue) || ttlValue < 0 ||
                !result.TryGetProperty("cacheScope", out var cacheScope) || cacheScope.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(cacheScope.GetString()))
            {
                findings.Add(Finding(
                    ValidationFindingRuleIds.SkillsCacheMetadataMissing,
                    McpSpecConstants.ExtensionMethods.SkillsList,
                    ValidationFindingSeverity.High,
                    ValidationRuleSource.Spec,
                    "skills/list is missing valid ttlMs or cacheScope metadata.",
                    "Return non-negative ttlMs and a non-empty cacheScope on every page."));
            }

            entries.AddRange(skills.EnumerateArray().Select(entry => entry.Clone()));
        }

        foreach (var entry in entries)
        {
            AnalyzeEntry(entry, findings, observations, seenUris, names, catalogEntries);
        }

        foreach (var collision in names.Where(pair => pair.Value.Count > 1))
        {
            findings.Add(Finding(
                ValidationFindingRuleIds.SkillsNameCollision,
                collision.Key,
                ValidationFindingSeverity.Medium,
                ValidationRuleSource.Guideline,
                $"Skill name '{collision.Key}' appears at multiple URIs in this server catalog.",
                "Present colliding skills with their origin and full URI; never silently shadow one entry."));
        }

        var failed = findings.Any(finding =>
            finding.Source == ValidationRuleSource.Spec &&
            finding.Severity >= ValidationFindingSeverity.High);
        return new SkillsCatalogAnalysis(
            failed ? TestStatus.Failed : TestStatus.Passed,
            entries.Count,
            findings,
            observations,
            catalogEntries);
    }

    public static string? ReadNextCursor(string json)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 64 });
        return TryGetResult(document.RootElement, out var result) &&
            result.TryGetProperty("nextCursor", out var cursor) &&
            cursor.ValueKind == JsonValueKind.String
                ? cursor.GetString()
                : null;
    }

    private static void AnalyzeEntry(
        JsonElement entry,
        ICollection<ValidationFinding> findings,
        ICollection<ValidationObservation> observations,
        ISet<string> seenUris,
        IDictionary<string, List<string>> names,
        ICollection<SkillCatalogEntry> catalogEntries)
    {
        var uri = TryGetString(entry, "uri") ?? string.Empty;
        var frontmatter = entry.TryGetProperty("frontmatter", out var frontmatterValue)
            ? frontmatterValue
            : default;
        var name = frontmatter.ValueKind == JsonValueKind.Object
            ? TryGetString(frontmatter, "name") ?? string.Empty
            : string.Empty;
        var description = frontmatter.ValueKind == JsonValueKind.Object
            ? TryGetString(frontmatter, "description") ?? string.Empty
            : string.Empty;

        if (!TryValidateSkillUri(uri, out var uriName))
        {
            findings.Add(Finding(
                ValidationFindingRuleIds.SkillsUriInvalid,
                uri,
                ValidationFindingSeverity.High,
                ValidationRuleSource.Spec,
                "Skill URI must identify a SKILL.md resource using a valid absolute URI.",
                "Return the full resource URI of the skill's SKILL.md."));
        }

        if (!seenUris.Add(uri))
        {
            findings.Add(Finding(
                ValidationFindingRuleIds.SkillsManifestInvalid,
                uri,
                ValidationFindingSeverity.High,
                ValidationRuleSource.Spec,
                "skills/list contains a duplicate skill URI.",
                "Return each skill entry exactly once across all pages."));
        }

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(description))
        {
            findings.Add(Finding(
                ValidationFindingRuleIds.SkillsFrontmatterInvalid,
                uri,
                ValidationFindingSeverity.High,
                ValidationRuleSource.Spec,
                "Skill frontmatter must contain non-empty name and description fields.",
                "Return verbatim frontmatter with required name and description fields."));
        }
        else
        {
            if (!names.TryGetValue(name, out var nameUris))
            {
                nameUris = [];
                names[name] = nameUris;
            }
            nameUris.Add(uri);

            if (!string.Equals(uriName, name, StringComparison.Ordinal))
            {
                findings.Add(Finding(
                    ValidationFindingRuleIds.SkillsNameUriMismatch,
                    uri,
                    ValidationFindingSeverity.High,
                    ValidationRuleSource.Spec,
                    "Skill URI path does not end in the declared frontmatter name.",
                    "Make the final skill path segment equal frontmatter.name."));
            }
        }

        if (frontmatter.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in frontmatter.EnumerateObject())
            {
                if (RiskBearingFrontmatterKeys.Contains(property.Name, StringComparer.OrdinalIgnoreCase))
                {
                    findings.Add(Finding(
                        ValidationFindingRuleIds.SkillsRiskBearingFrontmatter,
                        uri,
                        ValidationFindingSeverity.Medium,
                        ValidationRuleSource.Guideline,
                        $"Skill declares risk-bearing frontmatter field '{property.Name}'.",
                        "Require explicit per-skill approval and do not grant host tools or code execution implicitly."));
                }
            }
        }

        var resourceCount = 0;
        var dynamic = false;
        IReadOnlyDictionary<string, SkillResourceManifestEntry> manifest =
            new Dictionary<string, SkillResourceManifestEntry>(StringComparer.Ordinal);
        if (!entry.TryGetProperty("resources", out var resources))
        {
            AddInvalidManifest(findings, uri, "Skill entry is missing its required resources manifest.");
        }
        else if (resources.ValueKind == JsonValueKind.String && string.Equals(resources.GetString(), "dynamic", StringComparison.Ordinal))
        {
            dynamic = true;
            findings.Add(Finding(
                ValidationFindingRuleIds.SkillsDynamicManifestRisk,
                uri,
                ValidationFindingSeverity.Medium,
                ValidationRuleSource.Guideline,
                "Dynamic skill manifest cannot bind approval to stable content.",
                "Prefer a complete resource manifest with size and SHA-256 digest for every file."));
        }
        else if (resources.ValueKind == JsonValueKind.Array)
        {
            resourceCount = resources.GetArrayLength();
            manifest = AnalyzeManifest(uri, resources, findings);
        }
        else
        {
            AddInvalidManifest(findings, uri, "Skill resources must be an array or the string 'dynamic'.");
        }

        observations.Add(new ValidationObservation
        {
            Id = $"skills-entry/{CanonicalIdentifier(uri)}",
            LayerId = "extension/skills",
            Component = uri,
            ObservationKind = "skills-catalog-entry",
            ScenarioId = "skills-passive-catalog",
            Lane = EvaluationLane.ExtensionReadiness,
            Origin = EvidenceOrigin.DeterministicObservation,
            Metadata = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["name"] = name,
                ["resourceCount"] = resourceCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["dynamic"] = dynamic.ToString()
            }
        });
        catalogEntries.Add(new SkillCatalogEntry(uri, frontmatter.Clone(), dynamic, manifest));
    }

    private static IReadOnlyDictionary<string, SkillResourceManifestEntry> AnalyzeManifest(
        string skillUri,
        JsonElement resources,
        ICollection<ValidationFinding> findings)
    {
        var manifest = new SortedDictionary<string, SkillResourceManifestEntry>(StringComparer.Ordinal);
        if (resources.GetArrayLength() > MaximumResourcesPerSkill)
        {
            findings.Add(Finding(
                ValidationFindingRuleIds.SkillsManifestLimitExceeded,
                skillUri,
                ValidationFindingSeverity.Medium,
                ValidationRuleSource.Guideline,
                $"Skill manifest exceeds the interoperable limit of {MaximumResourcesPerSkill} resources.",
                "Reduce the manifest to 512 or fewer resources."));
        }

        var seenResources = new HashSet<string>(StringComparer.Ordinal);
        var includesSkill = false;
        long totalSize = 0;
        foreach (var resource in resources.EnumerateArray())
        {
            var uri = resource.ValueKind == JsonValueKind.Object ? TryGetString(resource, "uri") : null;
            var digest = resource.ValueKind == JsonValueKind.Object ? TryGetString(resource, "digest") : null;
            long byteSize = 0;
            var hasSize = resource.ValueKind == JsonValueKind.Object &&
                resource.TryGetProperty("size", out var size) &&
                size.TryGetInt64(out byteSize) &&
                byteSize >= 0;
            if (string.IsNullOrWhiteSpace(uri) || !seenResources.Add(uri) ||
                !DigestPattern().IsMatch(digest ?? string.Empty) || !hasSize ||
                !IsWithinSkill(skillUri, uri))
            {
                AddInvalidManifest(findings, skillUri, "Skill manifest contains a duplicate, malformed, or out-of-root resource entry.");
                continue;
            }

            includesSkill |= string.Equals(uri, skillUri, StringComparison.Ordinal);
            manifest[uri] = new SkillResourceManifestEntry(uri, digest!, byteSize);
            if (totalSize <= MaximumTotalBytesPerSkill)
            {
                totalSize += byteSize;
            }
        }

        if (!includesSkill)
        {
            AddInvalidManifest(findings, skillUri, "Skill manifest does not include its own SKILL.md URI.");
        }
        if (totalSize > MaximumTotalBytesPerSkill)
        {
            findings.Add(Finding(
                ValidationFindingRuleIds.SkillsManifestLimitExceeded,
                skillUri,
                ValidationFindingSeverity.Medium,
                ValidationRuleSource.Guideline,
                "Skill manifest exceeds the interoperable 16 MiB total-size limit.",
                "Reduce the total declared resource size to 16 MiB or less."));
        }
            return manifest;
    }

    private static bool TryGetResult(JsonElement root, out JsonElement result)
    {
        result = default;
        return root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("result", out result) &&
            result.ValueKind == JsonValueKind.Object;
    }

    private static bool TryGetRequiredCompleteResult(JsonElement result) =>
        string.Equals(TryGetString(result, "resultType"), "complete", StringComparison.Ordinal);

    private static string? TryGetString(JsonElement value, string propertyName) =>
        value.ValueKind == JsonValueKind.Object &&
        value.TryGetProperty(propertyName, out var property) &&
        property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static bool TryValidateSkillUri(string value, out string name)
    {
        name = string.Empty;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || !uri.AbsolutePath.EndsWith("/SKILL.md", StringComparison.Ordinal))
        {
            return false;
        }

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        name = segments.Length > 1 ? segments[^2] : uri.Host;
        return !string.IsNullOrWhiteSpace(name);
    }

    private static bool IsWithinSkill(string skillUri, string resourceUri)
    {
        var separator = skillUri.LastIndexOf("/SKILL.md", StringComparison.Ordinal);
        return separator > 0 &&
            (string.Equals(skillUri, resourceUri, StringComparison.Ordinal) ||
             resourceUri.StartsWith(skillUri[..(separator + 1)], StringComparison.Ordinal));
    }

    private static void AddInvalidManifest(ICollection<ValidationFinding> findings, string component, string summary) =>
        findings.Add(Finding(
            ValidationFindingRuleIds.SkillsManifestInvalid,
            component,
            ValidationFindingSeverity.High,
            ValidationRuleSource.Spec,
            summary,
            "Return a complete in-root manifest with unique URIs, non-negative sizes, and sha256 digests."));

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

    private static string CanonicalIdentifier(string value) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant()[..16];

    [GeneratedRegex("^sha256:[a-f0-9]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex DigestPattern();
}

internal sealed record SkillsCatalogAnalysis(
    TestStatus Status,
    int SkillCount,
    IReadOnlyList<ValidationFinding> Findings,
    IReadOnlyList<ValidationObservation> Observations,
    IReadOnlyList<SkillCatalogEntry> Entries);

internal sealed record SkillCatalogEntry(
    string Uri,
    JsonElement Frontmatter,
    bool IsDynamic,
    IReadOnlyDictionary<string, SkillResourceManifestEntry> Resources);

internal sealed record SkillResourceManifestEntry(string Uri, string Digest, long Size);