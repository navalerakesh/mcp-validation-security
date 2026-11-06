using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Mcp.Benchmark.Core.Constants;
using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.Infrastructure.Scenarios;

internal static class McpAppResourceAnalyzer
{
    private const int MaximumHtmlBytes = 2 * 1024 * 1024;
    private static readonly string[] CspKeys = ["connectDomains", "resourceDomains", "frameDomains", "baseUriDomains"];
    private static readonly string[] PermissionKeys = ["camera", "microphone", "geolocation", "clipboardWrite"];

    public static AppsResourceAnalysis Analyze(string requestedUri, string rawJson)
    {
        var findings = new List<ValidationFinding>();
        using var document = JsonDocument.Parse(rawJson, new JsonDocumentOptions
        {
            MaxDepth = 64,
            CommentHandling = JsonCommentHandling.Disallow,
            AllowTrailingCommas = false
        });
        if (!TryReadSingleContent(document.RootElement, out var content, findings, requestedUri))
        {
            return new AppsResourceAnalysis(requestedUri, null, EmptyCsp(), [], findings);
        }

        var observedUri = ReadString(content, "uri");
        var mimeType = ReadString(content, "mimeType");
        if (!string.Equals(observedUri, requestedUri, StringComparison.Ordinal) ||
            !string.Equals(mimeType, McpExtensionCatalog.Apps.HtmlMimeType, StringComparison.OrdinalIgnoreCase))
        {
            findings.Add(Finding(
                ValidationFindingRuleIds.AppsResourceReadInvalid,
                requestedUri,
                ValidationFindingSeverity.High,
                ValidationRuleSource.Spec,
                "MCP Apps resource response must match the requested URI and use the required HTML MIME type.",
                $"Return exactly {McpExtensionCatalog.Apps.HtmlMimeType} content for the requested ui:// URI."));
        }

        var hasText = content.TryGetProperty("text", out var textElement) && textElement.ValueKind == JsonValueKind.String;
        var hasBlob = content.TryGetProperty("blob", out var blobElement) && blobElement.ValueKind == JsonValueKind.String;
        if (hasText == hasBlob)
        {
            findings.Add(Finding(
                ValidationFindingRuleIds.AppsResourceReadInvalid,
                requestedUri,
                ValidationFindingSeverity.High,
                ValidationRuleSource.Spec,
                "MCP Apps resource content must contain exactly one of text or blob.",
                "Return one HTML content representation, not both and not neither."));
            return new AppsResourceAnalysis(requestedUri, null, EmptyCsp(), [], findings);
        }

        byte[] htmlBytes;
        try
        {
            htmlBytes = hasText
                ? Encoding.UTF8.GetBytes(textElement.GetString()!)
                : Convert.FromBase64String(blobElement.GetString()!);
        }
        catch (FormatException)
        {
            findings.Add(Finding(
                ValidationFindingRuleIds.AppsResourceReadInvalid,
                requestedUri,
                ValidationFindingSeverity.High,
                ValidationRuleSource.Spec,
                "MCP Apps blob content is not valid base64.",
                "Encode the HTML resource as valid base64 or return text content."));
            return new AppsResourceAnalysis(requestedUri, null, EmptyCsp(), [], findings);
        }

        if (htmlBytes.Length > MaximumHtmlBytes)
        {
            findings.Add(Finding(
                ValidationFindingRuleIds.AppsResourceReadInvalid,
                requestedUri,
                ValidationFindingSeverity.High,
                ValidationRuleSource.Heuristic,
                $"MCP Apps HTML exceeds the bounded {MaximumHtmlBytes}-byte analysis limit.",
                "Reduce UI resource size or review it with a separately governed artifact scanner."));
            return new AppsResourceAnalysis(requestedUri, null, EmptyCsp(), [], findings);
        }

        var csp = ReadCsp(content, requestedUri, findings);
        var permissions = ReadPermissions(content, requestedUri, findings);
        AnalyzeHtml(Encoding.UTF8.GetString(htmlBytes), requestedUri, csp, findings);
        var digest = Convert.ToHexString(SHA256.HashData(htmlBytes)).ToLowerInvariant();
        return new AppsResourceAnalysis(requestedUri, digest, csp, permissions, findings);
    }

    private static bool TryReadSingleContent(
        JsonElement root,
        out JsonElement content,
        ICollection<ValidationFinding> findings,
        string requestedUri)
    {
        content = default;
        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("result", out var result) && result.ValueKind == JsonValueKind.Object &&
            result.TryGetProperty("contents", out var contents) && contents.ValueKind == JsonValueKind.Array &&
            contents.GetArrayLength() == 1 && contents[0].ValueKind == JsonValueKind.Object)
        {
            content = contents[0];
            return true;
        }

        findings.Add(Finding(
            ValidationFindingRuleIds.AppsResourceReadInvalid,
            requestedUri,
            ValidationFindingSeverity.High,
            ValidationRuleSource.Spec,
            "MCP Apps resources/read must return exactly one content item.",
            "Return one UI resource content item for each requested ui:// URI."));
        return false;
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> ReadCsp(
        JsonElement content,
        string component,
        ICollection<ValidationFinding> findings)
    {
        var result = new SortedDictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        if (!TryGetUiMetadata(content, out var ui) || !ui.TryGetProperty("csp", out var csp))
        {
            return result;
        }
        if (csp.ValueKind != JsonValueKind.Object)
        {
            findings.Add(CspInvalid(component, "MCP Apps ui.csp must be an object."));
            return result;
        }

        foreach (var key in CspKeys)
        {
            if (!csp.TryGetProperty(key, out var values))
            {
                continue;
            }
            if (values.ValueKind != JsonValueKind.Array)
            {
                findings.Add(CspInvalid(component, $"MCP Apps ui.csp.{key} must be an array."));
                continue;
            }

            var origins = new List<string>();
            foreach (var value in values.EnumerateArray())
            {
                if (value.ValueKind != JsonValueKind.String || !IsValidDeclaredOrigin(value.GetString()!, key == "resourceDomains"))
                {
                    findings.Add(CspInvalid(component, $"MCP Apps ui.csp.{key} contains an invalid origin."));
                    continue;
                }
                origins.Add(value.GetString()!);
            }
            result[key] = origins.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray();
        }
        return result;
    }

    private static IReadOnlyList<string> ReadPermissions(
        JsonElement content,
        string component,
        ICollection<ValidationFinding> findings)
    {
        if (!TryGetUiMetadata(content, out var ui) || !ui.TryGetProperty("permissions", out var permissions))
        {
            return Array.Empty<string>();
        }
        if (permissions.ValueKind != JsonValueKind.Object)
        {
            findings.Add(Finding(
                ValidationFindingRuleIds.AppsPermissionRisk,
                component,
                ValidationFindingSeverity.Medium,
                ValidationRuleSource.Spec,
                "MCP Apps ui.permissions must be an object.",
                "Declare only supported permission names with empty object values."));
            return Array.Empty<string>();
        }

        var requested = new List<string>();
        foreach (var permission in permissions.EnumerateObject())
        {
            if (!PermissionKeys.Contains(permission.Name, StringComparer.Ordinal) || permission.Value.ValueKind != JsonValueKind.Object)
            {
                findings.Add(Finding(
                    ValidationFindingRuleIds.AppsPermissionRisk,
                    component,
                    ValidationFindingSeverity.Medium,
                    ValidationRuleSource.Spec,
                    $"MCP Apps resource requests unsupported or malformed permission '{permission.Name}'.",
                    "Use only camera, microphone, geolocation, or clipboardWrite with empty object values."));
                continue;
            }
            requested.Add(permission.Name);
            findings.Add(Finding(
                ValidationFindingRuleIds.AppsPermissionRisk,
                component,
                ValidationFindingSeverity.Medium,
                ValidationRuleSource.Guideline,
                $"MCP Apps resource requests browser permission '{permission.Name}'.",
                "Review the permission against the UI purpose and require host/user approval."));
        }
        return requested.OrderBy(value => value, StringComparer.Ordinal).ToArray();
    }

    private static void AnalyzeHtml(
        string html,
        string component,
        IReadOnlyDictionary<string, IReadOnlyList<string>> csp,
        ICollection<ValidationFinding> findings)
    {
        var parser = new HtmlParser(new HtmlParserOptions { IsScripting = false });
        var document = parser.ParseDocument(html);
        foreach (var element in document.QuerySelectorAll("object, embed"))
        {
            findings.Add(Finding(
                ValidationFindingRuleIds.AppsDangerousHtmlContent,
                component,
                ValidationFindingSeverity.High,
                ValidationRuleSource.Heuristic,
                $"MCP Apps HTML contains executable object element <{element.LocalName}>.",
                "Remove object/embed content and use sandbox-compatible HTML primitives."));
        }

        AnalyzeReferences(document.QuerySelectorAll("script[src], link[href], img[src], audio[src], video[src], source[src]"), "resourceDomains", csp, component, findings);
        AnalyzeReferences(document.QuerySelectorAll("iframe[src]"), "frameDomains", csp, component, findings);
        AnalyzeReferences(document.QuerySelectorAll("base[href]"), "baseUriDomains", csp, component, findings);
        AnalyzeReferences(document.QuerySelectorAll("form[action]"), "connectDomains", csp, component, findings);
    }

    private static void AnalyzeReferences(
        IEnumerable<IElement> elements,
        string cspKey,
        IReadOnlyDictionary<string, IReadOnlyList<string>> csp,
        string component,
        ICollection<ValidationFinding> findings)
    {
        var allowed = csp.TryGetValue(cspKey, out var values) ? values : Array.Empty<string>();
        foreach (var element in elements)
        {
            var raw = element.GetAttribute(element.HasAttribute("src") ? "src" : element.HasAttribute("href") ? "href" : "action");
            if (string.IsNullOrWhiteSpace(raw) || raw.StartsWith("#", StringComparison.Ordinal) || raw.StartsWith("/", StringComparison.Ordinal) && !raw.StartsWith("//", StringComparison.Ordinal))
            {
                continue;
            }
            if (raw.StartsWith("data:", StringComparison.OrdinalIgnoreCase) &&
                element.LocalName is "img" or "audio" or "video" or "source" &&
                raw.StartsWith($"data:{(element.LocalName == "img" ? "image" : element.LocalName == "source" ? "image" : element.LocalName)}/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            if (raw.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase) || raw.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                findings.Add(Finding(
                    ValidationFindingRuleIds.AppsDangerousHtmlContent,
                    component,
                    ValidationFindingSeverity.High,
                    ValidationRuleSource.Heuristic,
                    $"MCP Apps HTML uses dangerous URI scheme in <{element.LocalName}>.",
                    "Remove javascript: and executable data: references."));
                continue;
            }
            if (!raw.StartsWith("//", StringComparison.Ordinal) && Uri.TryCreate(raw, UriKind.Relative, out _))
            {
                continue;
            }
            if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri))
            {
                findings.Add(CspInvalid(component, $"MCP Apps HTML contains malformed external reference '{raw}'."));
                continue;
            }

            var origin = uri.GetLeftPart(UriPartial.Authority);
            if (!allowed.Any(declared => OriginMatches(declared, uri)))
            {
                findings.Add(Finding(
                    ValidationFindingRuleIds.AppsCspUndeclaredOrigin,
                    component,
                    ValidationFindingSeverity.High,
                    ValidationRuleSource.Spec,
                    $"MCP Apps HTML references undeclared {cspKey} origin '{origin}'.",
                    $"Declare {origin} in ui.csp.{cspKey} or remove the external reference."));
            }
        }
    }

    private static bool TryGetUiMetadata(JsonElement content, out JsonElement ui)
    {
        ui = default;
        return content.TryGetProperty("_meta", out var metadata) && metadata.ValueKind == JsonValueKind.Object &&
            metadata.TryGetProperty("ui", out ui) && ui.ValueKind == JsonValueKind.Object;
    }

    private static bool IsValidDeclaredOrigin(string value, bool allowWildcard)
    {
        var candidate = value;
        if (allowWildcard && value.Contains("://*.", StringComparison.Ordinal))
        {
            candidate = value.Replace("://*.", "://wildcard.", StringComparison.Ordinal);
        }
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https" or "ws" or "wss") ||
            !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
        {
            return false;
        }
        return uri.AbsolutePath is "" or "/";
    }

    private static bool OriginMatches(string declared, Uri actual)
    {
        var wildcard = declared.Contains("://*.", StringComparison.Ordinal);
        var normalized = wildcard
            ? declared.Replace("://*.", "://wildcard.", StringComparison.Ordinal)
            : declared;
        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var declaredUri) ||
            !string.Equals(declaredUri.Scheme, actual.Scheme, StringComparison.OrdinalIgnoreCase) ||
            declaredUri.Port != actual.Port)
        {
            return false;
        }
        if (!wildcard)
        {
            return string.Equals(declaredUri.Host, actual.Host, StringComparison.OrdinalIgnoreCase);
        }
        var suffix = declaredUri.Host["wildcard".Length..];
        return actual.Host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) && actual.Host.Length > suffix.Length;
    }

    private static string? ReadString(JsonElement value, string propertyName) =>
        value.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> EmptyCsp() =>
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

    private static ValidationFinding CspInvalid(string component, string summary) => Finding(
        ValidationFindingRuleIds.AppsCspOriginInvalid,
        component,
        ValidationFindingSeverity.High,
        ValidationRuleSource.Spec,
        summary,
        "Use absolute HTTP(S) or WebSocket origins without paths, user-info, queries, or fragments.");

    private static ValidationFinding Finding(
        string ruleId,
        string component,
        ValidationFindingSeverity severity,
        ValidationRuleSource source,
        string summary,
        string recommendation) => new()
        {
            RuleId = ruleId,
            Category = "MCPApps",
            Component = component,
            Severity = severity,
            Source = source,
            Summary = summary,
            Recommendation = recommendation,
            SpecReference = McpExtensionCatalog.Apps.SpecificationUrl,
            ExcludedFromAggregate = true
        };
}

internal sealed record AppsResourceAnalysis(
    string ResourceUri,
    string? ContentSha256,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Csp,
    IReadOnlyList<string> Permissions,
    IReadOnlyList<ValidationFinding> Findings);