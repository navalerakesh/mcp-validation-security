using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Mcp.Benchmark.Core.Utilities;

public static class CanonicalJsonDigest
{
    public static string ComputeText(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return ComputeBytes(Encoding.UTF8.GetBytes(value));
    }

    public static string ComputeBytes(ReadOnlySpan<byte> value) =>
        Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();

    public static string ComputeObject<T>(T value, JsonSerializerOptions? options = null) =>
        ComputeElement(JsonSerializer.SerializeToElement(value, options));

    public static string ComputeElement(JsonElement element)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            WriteCanonical(writer, element);
        }

        return ComputeBytes(stream.ToArray());
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    WriteCanonical(writer, item);
                }
                writer.WriteEndArray();
                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }
}