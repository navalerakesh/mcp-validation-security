using System.Text.Json;
using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.Infrastructure.Utilities;

internal static class JsonRpcResponseInspector
{
    public static bool IsMethodNotFound(JsonRpcResponse? response)
    {
        return TryGetErrorCode(response, out var errorCode) && errorCode == -32601;
    }

    public static bool TryGetErrorCode(JsonRpcResponse? response, out int errorCode)
    {
        errorCode = default;

        if (string.IsNullOrWhiteSpace(response?.RawJson))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(response.RawJson);
            if (!document.RootElement.TryGetProperty("error", out var errorElement) ||
                !errorElement.TryGetProperty("code", out var codeElement) ||
                codeElement.ValueKind != JsonValueKind.Number)
            {
                return false;
            }

            errorCode = codeElement.GetInt32();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}