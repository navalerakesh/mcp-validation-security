using System.Text.Json;

namespace Mcp.Benchmark.Infrastructure.Utilities;

internal static class JsonRpcResponseIdentity
{
    internal static bool Matches(JsonElement response, string? requestId) =>
        requestId is not null && response.ValueKind == JsonValueKind.Object && response.TryGetProperty("id", out var responseId) &&
        responseId.ValueKind == JsonValueKind.String && responseId.ValueEquals(requestId);
}