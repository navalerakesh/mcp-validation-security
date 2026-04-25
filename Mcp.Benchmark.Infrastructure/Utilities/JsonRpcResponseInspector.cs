using System.Text.Json;
using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.Infrastructure.Utilities;

internal enum JsonRpcResponseSurface
{
    Unknown = 0,
    JsonRpcErrorEnvelope,
    HttpFrontDoorRejection,
    AuthenticationChallenge,
    TransportFailure,
    EmptyBody,
    NonJsonRpcJson
}

internal readonly record struct JsonRpcResponseClassification(
    JsonRpcResponseSurface Surface,
    int? ErrorCode,
    int StatusCode,
    string? ContentType);

internal static class JsonRpcResponseInspector
{
    public static bool IsMethodNotFound(JsonRpcResponse? response)
    {
        return TryGetErrorCode(response, out var errorCode) && errorCode == -32601;
    }

    public static JsonRpcResponseClassification Classify(JsonRpcResponse? response)
    {
        if (response is null)
        {
            return new JsonRpcResponseClassification(JsonRpcResponseSurface.Unknown, null, 0, null);
        }

        var contentType = TryGetHeader(response, "Content-Type");

        if (response.StatusCode is 401 or 403)
        {
            return new JsonRpcResponseClassification(JsonRpcResponseSurface.AuthenticationChallenge, null, response.StatusCode, contentType);
        }

        if (response.StatusCode < 0)
        {
            return new JsonRpcResponseClassification(JsonRpcResponseSurface.TransportFailure, null, response.StatusCode, contentType);
        }

        if (string.IsNullOrWhiteSpace(response.RawJson))
        {
            return new JsonRpcResponseClassification(JsonRpcResponseSurface.EmptyBody, null, response.StatusCode, contentType);
        }

        var raw = response.RawJson.Trim();
        if (raw.StartsWith("<", StringComparison.Ordinal) ||
            (!raw.StartsWith("{", StringComparison.Ordinal) && !raw.StartsWith("[", StringComparison.Ordinal)))
        {
            return new JsonRpcResponseClassification(JsonRpcResponseSurface.HttpFrontDoorRejection, null, response.StatusCode, contentType);
        }

        try
        {
            using var document = JsonDocument.Parse(raw);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return new JsonRpcResponseClassification(JsonRpcResponseSurface.NonJsonRpcJson, null, response.StatusCode, contentType);
            }

            var hasJsonRpc = root.TryGetProperty("jsonrpc", out var jsonRpcVersion) && jsonRpcVersion.GetString() == "2.0";
            if (root.TryGetProperty("error", out var errorElement) &&
                errorElement.TryGetProperty("code", out var codeElement) &&
                codeElement.ValueKind == JsonValueKind.Number)
            {
                var errorCode = codeElement.GetInt32();
                return new JsonRpcResponseClassification(
                    hasJsonRpc ? JsonRpcResponseSurface.JsonRpcErrorEnvelope : JsonRpcResponseSurface.NonJsonRpcJson,
                    errorCode,
                    response.StatusCode,
                    contentType);
            }

            return new JsonRpcResponseClassification(
                hasJsonRpc ? JsonRpcResponseSurface.Unknown : JsonRpcResponseSurface.NonJsonRpcJson,
                null,
                response.StatusCode,
                contentType);
        }
        catch (JsonException)
        {
            return new JsonRpcResponseClassification(JsonRpcResponseSurface.HttpFrontDoorRejection, null, response.StatusCode, contentType);
        }
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

    private static string? TryGetHeader(JsonRpcResponse response, string headerName)
    {
        foreach (var pair in response.Headers)
        {
            if (string.Equals(pair.Key, headerName, StringComparison.OrdinalIgnoreCase))
            {
                return pair.Value;
            }
        }

        return null;
    }
}