using System.Collections;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.Infrastructure.Services.Reporting;

internal static class ReportContextFormatter
{
    private const int MaxDisplayValueLength = 180;

    private static readonly string[] PreferredKeyOrder =
    {
        "probeId",
        "method",
        "requestMethod",
        "httpMethod",
        "statusCode",
        "expectedStatusCode",
        "actualStatusCode",
        "contentType",
        "requestedProtocolVersion",
        "serverProtocolVersion",
        "schemaVersion",
        "fallbackApplied",
        "availableProtocolVersions",
        "expected",
        "actual",
        "requestShape",
        "responseShape",
        "uri",
        "endpoint",
        "transport"
    };

    private static readonly Dictionary<string, string> KnownLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["probeId"] = "Probe ID",
        ["method"] = "Method",
        ["requestMethod"] = "Request method",
        ["httpMethod"] = "HTTP method",
        ["statusCode"] = "HTTP status",
        ["expectedStatusCode"] = "Expected status",
        ["actualStatusCode"] = "Actual status",
        ["contentType"] = "Content-Type",
        ["requestedProtocolVersion"] = "Requested protocol",
        ["serverProtocolVersion"] = "Server protocol",
        ["schemaVersion"] = "Schema version",
        ["fallbackApplied"] = "Schema fallback",
        ["availableProtocolVersions"] = "Available protocols",
        ["expected"] = "Expected",
        ["actual"] = "Actual",
        ["requestShape"] = "Request shape",
        ["responseShape"] = "Response shape",
        ["uri"] = "URI",
        ["endpoint"] = "Endpoint",
        ["transport"] = "Transport"
    };

    internal static bool ShouldRenderHumanContext(ComplianceViolation violation)
    {
        return violation.Severity is ViolationSeverity.Critical or ViolationSeverity.High &&
            BuildDisplayItems(violation.Context).Count > 0;
    }

    internal static IReadOnlyList<ReportContextItem> BuildDisplayItems(IReadOnlyDictionary<string, object>? context)
    {
        if (context is null || context.Count == 0)
        {
            return Array.Empty<ReportContextItem>();
        }

        return context
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Key))
            .Select(entry => new
            {
                entry.Key,
                Value = FormatValue(entry.Value)
            })
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Value))
            .OrderBy(entry => GetKeyPriority(entry.Key))
            .ThenBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .Select(entry => new ReportContextItem(ResolveLabel(entry.Key), entry.Value))
            .ToList();
    }

    internal static string FormatMarkdownInline(IReadOnlyDictionary<string, object>? context)
    {
        return string.Join("; ", BuildDisplayItems(context).Select(item => $"**{item.Label}:** `{EscapeMarkdownCode(item.Value)}`"));
    }

    internal static string FormatPlainText(IReadOnlyDictionary<string, object>? context)
    {
        return string.Join("; ", BuildDisplayItems(context).Select(item => $"{item.Label}={item.Value}"));
    }

    private static int GetKeyPriority(string key)
    {
        for (var index = 0; index < PreferredKeyOrder.Length; index++)
        {
            if (string.Equals(PreferredKeyOrder[index], key, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return PreferredKeyOrder.Length;
    }

    private static string ResolveLabel(string key)
    {
        return KnownLabels.TryGetValue(key, out var label) ? label : HumanizeKey(key);
    }

    private static string HumanizeKey(string key)
    {
        var builder = new StringBuilder(key.Length + 8);
        char previous = '\0';

        foreach (var character in key.Trim())
        {
            if (character is '_' or '-' or '.')
            {
                AppendSpace(builder);
                previous = ' ';
                continue;
            }

            if (char.IsUpper(character) && builder.Length > 0 && previous != ' ' && !char.IsUpper(previous))
            {
                AppendSpace(builder);
            }

            builder.Append(character);
            previous = character;
        }

        var words = builder.ToString().Trim();
        return string.IsNullOrWhiteSpace(words)
            ? key
            : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(words.ToLowerInvariant());
    }

    private static void AppendSpace(StringBuilder builder)
    {
        if (builder.Length > 0 && builder[^1] != ' ')
        {
            builder.Append(' ');
        }
    }

    private static string FormatValue(object? value)
    {
        var formatted = FormatRawValue(value, 0);
        formatted = NormalizeWhitespace(formatted);
        return formatted.Length <= MaxDisplayValueLength
            ? formatted
            : formatted[..MaxDisplayValueLength] + "...";
    }

    private static string FormatRawValue(object? value, int depth)
    {
        if (value is null)
        {
            return string.Empty;
        }

        if (depth > 2)
        {
            return value.ToString() ?? string.Empty;
        }

        return value switch
        {
            string text => text,
            bool flag => flag ? "true" : "false",
            JsonElement element => FormatJsonElement(element),
            IDictionary dictionary => FormatDictionary(dictionary, depth),
            IEnumerable enumerable when value is not string => FormatEnumerable(enumerable, depth),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty
        };
    }

    private static string FormatJsonElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? string.Empty,
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => string.Empty,
            _ => element.GetRawText()
        };
    }

    private static string FormatDictionary(IDictionary dictionary, int depth)
    {
        var parts = new List<string>();
        foreach (DictionaryEntry entry in dictionary)
        {
            var key = entry.Key?.ToString();
            var value = FormatRawValue(entry.Value, depth + 1);
            if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(value))
            {
                parts.Add($"{key}={value}");
            }
        }

        return string.Join(", ", parts);
    }

    private static string FormatEnumerable(IEnumerable enumerable, int depth)
    {
        var parts = new List<string>();
        foreach (var item in enumerable)
        {
            var value = FormatRawValue(item, depth + 1);
            if (!string.IsNullOrWhiteSpace(value))
            {
                parts.Add(value);
            }
        }

        return string.Join(", ", parts);
    }

    private static string NormalizeWhitespace(string value)
    {
        return string.Join(" ", value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static string EscapeMarkdownCode(string value)
    {
        return value.Replace("`", "'");
    }
}

internal sealed record ReportContextItem(string Label, string Value);
