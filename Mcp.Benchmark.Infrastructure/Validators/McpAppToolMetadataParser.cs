using System.Text.Json;
using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.Infrastructure.Validators;

internal static class McpAppToolMetadataParser
{
    public static McpAppToolMetadata? Parse(JsonElement metadata)
    {
        var issues = new List<string>();
        var hasNestedUi = metadata.TryGetProperty("ui", out var ui);
        var hasDeprecatedUri = metadata.TryGetProperty("ui/resourceUri", out var deprecatedUri);
        if (!hasNestedUi && !hasDeprecatedUri)
        {
            return null;
        }

        string? resourceUri = null;
        var visibility = new List<string>();
        if (hasNestedUi)
        {
            if (ui.ValueKind != JsonValueKind.Object)
            {
                issues.Add("_meta.ui must be an object.");
            }
            else
            {
                if (ui.TryGetProperty("resourceUri", out var nestedUri))
                {
                    if (nestedUri.ValueKind == JsonValueKind.String)
                    {
                        resourceUri = nestedUri.GetString();
                    }
                    else
                    {
                        issues.Add("_meta.ui.resourceUri must be a string.");
                    }
                }

                if (ui.TryGetProperty("visibility", out var visibilityElement))
                {
                    if (visibilityElement.ValueKind != JsonValueKind.Array)
                    {
                        issues.Add("_meta.ui.visibility must be an array.");
                    }
                    else
                    {
                        foreach (var item in visibilityElement.EnumerateArray())
                        {
                            if (item.ValueKind == JsonValueKind.String)
                            {
                                visibility.Add(item.GetString() ?? string.Empty);
                            }
                            else
                            {
                                issues.Add("_meta.ui.visibility entries must be strings.");
                            }
                        }
                    }
                }
            }
        }

        if (hasDeprecatedUri)
        {
            if (deprecatedUri.ValueKind == JsonValueKind.String)
            {
                var legacyValue = deprecatedUri.GetString();
                if (resourceUri == null)
                {
                    resourceUri = legacyValue;
                }
                else if (!string.Equals(resourceUri, legacyValue, StringComparison.Ordinal))
                {
                    issues.Add("Nested and deprecated UI resource URIs conflict.");
                }
            }
            else
            {
                issues.Add("_meta['ui/resourceUri'] must be a string.");
            }
        }

        return new McpAppToolMetadata
        {
            IsPresent = true,
            ResourceUri = resourceUri,
            Visibility = visibility,
            UsesDeprecatedResourceUri = hasDeprecatedUri,
            IsMalformed = issues.Count > 0,
            Issues = issues
        };
    }
}