using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mcp.Benchmark.Infrastructure.Services;

internal sealed class ToolCallParameters(string name, JsonElement arguments, JsonElement inputSchema)
{
    [JsonPropertyName("name")]
    public string Name { get; } = name;
    [JsonPropertyName("arguments")]
    public JsonElement Arguments { get; } = arguments.Clone();
    [JsonIgnore]
    public JsonElement InputSchema { get; } = inputSchema.Clone();
}