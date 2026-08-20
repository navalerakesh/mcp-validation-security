using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mcp.Benchmark.CLI.Utilities;

internal static class ArtifactJsonOptions
{
    internal static JsonSerializerOptions Create(bool writeIndented = true, bool propertyNameCaseInsensitive = false)
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = writeIndented,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = propertyNameCaseInsensitive
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: true));
        return options;
    }
}