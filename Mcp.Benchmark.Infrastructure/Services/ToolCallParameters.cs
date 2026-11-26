using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mcp.Benchmark.Infrastructure.Services;

internal sealed class ToolCallParameters(string name, JsonElement arguments, JsonElement inputSchema) : IClientInputCapabilities
{
    [JsonPropertyName("name")]
    public string Name { get; } = name;
    [JsonPropertyName("arguments")]
    public JsonElement Arguments { get; } = arguments.Clone();
    [JsonIgnore]
    public JsonElement InputSchema { get; } = inputSchema.Clone();

    [JsonIgnore]
    public JsonElement? ClientCapabilities { get; init; }

    [JsonPropertyName("inputResponses")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? InputResponses { get; init; }

    [JsonPropertyName("requestState")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RequestState { get; init; }
}