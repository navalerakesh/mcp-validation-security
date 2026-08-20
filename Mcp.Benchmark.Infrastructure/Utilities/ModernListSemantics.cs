using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Mcp.Benchmark.Infrastructure.Utilities;

internal readonly record struct ModernListAssessment(
    bool IsValid,
    string? Error,
    string? CacheScope,
    long? TtlMs,
    string? Fingerprint);

internal static class ModernListSemantics
{
    public static ModernListAssessment Assess(string? rawJson, string collectionProperty)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(collectionProperty);
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return Invalid("Modern list response body is empty.");
        }

        try
        {
            using var document = JsonDocument.Parse(rawJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("result", out var result) ||
                result.ValueKind != JsonValueKind.Object)
            {
                return Invalid("Modern list response requires an object result.");
            }

            if (!result.TryGetProperty("resultType", out var resultType) || resultType.GetString() != "complete")
            {
                return Invalid("Modern list response requires resultType 'complete'.");
            }

            if (!result.TryGetProperty("cacheScope", out var cacheScopeElement) ||
                cacheScopeElement.ValueKind != JsonValueKind.String ||
                cacheScopeElement.GetString() is not ("public" or "private"))
            {
                return Invalid("Modern list response requires cacheScope 'public' or 'private'.");
            }

            if (!result.TryGetProperty("ttlMs", out var ttlElement) ||
                !ttlElement.TryGetInt64(out var ttlMs) || ttlMs < 0)
            {
                return Invalid("Modern list response requires a non-negative ttlMs.");
            }

            if (!result.TryGetProperty(collectionProperty, out var collection) || collection.ValueKind != JsonValueKind.Array)
            {
                return Invalid($"Modern list response requires an array {collectionProperty} field.");
            }

            var canonical = new StringBuilder();
            WriteCanonical(collection, canonical);
            if (result.TryGetProperty("nextCursor", out var nextCursor))
            {
                canonical.Append('|');
                WriteCanonical(nextCursor, canonical);
            }

            var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())))
                .ToLowerInvariant();
            return new ModernListAssessment(true, null, cacheScopeElement.GetString(), ttlMs, fingerprint);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return Invalid($"Modern list response could not be parsed: {ex.Message}");
        }
    }

    private static void WriteCanonical(JsonElement element, StringBuilder builder)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                builder.Append('{');
                foreach (var property in element.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal))
                {
                    builder.Append(JsonSerializer.Serialize(property.Name));
                    builder.Append(':');
                    WriteCanonical(property.Value, builder);
                    builder.Append(',');
                }
                builder.Append('}');
                break;
            case JsonValueKind.Array:
                builder.Append('[');
                foreach (var item in element.EnumerateArray())
                {
                    WriteCanonical(item, builder);
                    builder.Append(',');
                }
                builder.Append(']');
                break;
            default:
                builder.Append(element.GetRawText());
                break;
        }
    }

    private static ModernListAssessment Invalid(string error) => new(false, error, null, null, null);
}
