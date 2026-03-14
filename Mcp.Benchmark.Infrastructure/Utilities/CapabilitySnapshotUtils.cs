using System.Collections.Generic;
using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.Infrastructure.Utilities;

internal static class CapabilitySnapshotUtils
{
    public static JsonRpcResponse? CloneResponse(JsonRpcResponse? source)
    {
        if (source == null)
        {
            return null;
        }

        return new JsonRpcResponse
        {
            StatusCode = source.StatusCode,
            IsSuccess = source.IsSuccess,
            RawJson = source.RawJson,
            Error = source.Error,
            Headers = source.Headers != null
                ? new Dictionary<string, string>(source.Headers)
                : new Dictionary<string, string>()
        };
    }
}
