using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Utilities;

namespace Mcp.Benchmark.Core.Services;

public static class ValidationTargetIdentity
{
    public static string ComputeDigest(McpServerConfig serverConfig)
    {
        ArgumentNullException.ThrowIfNull(serverConfig);
        var target = string.Equals(serverConfig.Transport, "stdio", StringComparison.OrdinalIgnoreCase)
            ? serverConfig.Endpoint
            : serverConfig.CloneWithoutSecrets().Endpoint;
        return CanonicalJsonDigest.ComputeText(target ?? string.Empty);
    }
}