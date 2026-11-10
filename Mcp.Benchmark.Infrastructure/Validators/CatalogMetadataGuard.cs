using System.Text.Json;

namespace Mcp.Benchmark.Infrastructure.Validators;

internal static class CatalogMetadataGuard
{
    internal static bool HasMalformedText(JsonElement item, params string[] fields) =>
        item.ValueKind != JsonValueKind.Object || fields.Any(field =>
            item.TryGetProperty(field, out var value) && value.ValueKind != JsonValueKind.String);
}