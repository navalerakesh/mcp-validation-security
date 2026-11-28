using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mcp.Benchmark.Infrastructure.Services;

internal interface IClientInputCapabilities
{
    JsonElement? ClientCapabilities { get; }
}

internal sealed class PromptCallParameters(string name, JsonElement arguments) : IClientInputCapabilities
{
    [JsonPropertyName("name")]
    public string Name { get; } = name;
    [JsonPropertyName("arguments")]
    public JsonElement Arguments { get; } = arguments.Clone();
    [JsonIgnore]
    public JsonElement? ClientCapabilities { get; init; }
    [JsonPropertyName("inputResponses")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? InputResponses { get; init; }
    [JsonPropertyName("requestState")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RequestState { get; init; }
}
