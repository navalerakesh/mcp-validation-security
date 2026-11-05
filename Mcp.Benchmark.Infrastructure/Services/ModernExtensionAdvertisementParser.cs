using System.Text.Json;
using Mcp.Benchmark.Core.Constants;
using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.Infrastructure.Services;

internal static class ModernExtensionAdvertisementParser
{
    public static IReadOnlyDictionary<string, ModernExtensionAdvertisement> Parse(JsonElement extensions)
    {
        var result = new SortedDictionary<string, ModernExtensionAdvertisement>(StringComparer.Ordinal);
        if (extensions.ValueKind != JsonValueKind.Object)
        {
            return result;
        }

        foreach (var extension in extensions.EnumerateObject())
        {
            result[extension.Name] = ParseOne(extension.Name, extension.Value);
        }
        return result;
    }

    private static ModernExtensionAdvertisement ParseOne(string extensionId, JsonElement settings)
    {
        var errors = new List<string>();
        var mimeTypes = new List<string>();
        bool? directoryRead = null;
        if (settings.ValueKind != JsonValueKind.Object)
        {
            errors.Add("Extension settings must be an object.");
        }
        else if (string.Equals(extensionId, McpExtensionCatalog.Apps.Id, StringComparison.Ordinal))
        {
            if (!settings.TryGetProperty("mimeTypes", out var mimeTypesElement) || mimeTypesElement.ValueKind != JsonValueKind.Array)
            {
                errors.Add("MCP Apps settings require a mimeTypes array.");
            }
            else
            {
                foreach (var mimeType in mimeTypesElement.EnumerateArray())
                {
                    if (mimeType.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(mimeType.GetString()))
                    {
                        mimeTypes.Add(mimeType.GetString()!);
                    }
                    else
                    {
                        errors.Add("MCP Apps mimeTypes entries must be non-empty strings.");
                    }
                }
                if (!mimeTypes.Contains(McpExtensionCatalog.Apps.HtmlMimeType, StringComparer.OrdinalIgnoreCase))
                {
                    errors.Add($"MCP Apps mimeTypes must include {McpExtensionCatalog.Apps.HtmlMimeType}.");
                }
            }
        }
        else if (string.Equals(extensionId, McpExtensionCatalog.Skills.Id, StringComparison.Ordinal) &&
                 settings.TryGetProperty("directoryRead", out var directoryReadElement))
        {
            if (directoryReadElement.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                directoryRead = directoryReadElement.GetBoolean();
            }
            else
            {
                errors.Add("MCP Skills directoryRead must be a boolean.");
            }
        }

        return new ModernExtensionAdvertisement
        {
            ExtensionId = extensionId,
            IsValid = errors.Count == 0,
            MimeTypes = mimeTypes,
            DirectoryRead = directoryRead,
            Errors = errors
        };
    }
}