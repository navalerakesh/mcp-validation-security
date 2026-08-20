using ModelContextProtocol.Protocol;

namespace Mcp.Benchmark.CLI.Utilities;

internal static class CapabilityDisplayPolicy
{
    internal const string LegacyLoggingLabel = "logging (legacy, deprecated)";

    internal static bool HasLegacyLogging(ServerCapabilities capabilities)
    {
#pragma warning disable MCP9005
        return capabilities.Logging != null;
#pragma warning restore MCP9005
    }
}