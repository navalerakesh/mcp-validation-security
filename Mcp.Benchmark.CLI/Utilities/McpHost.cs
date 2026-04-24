using System;

namespace Mcp.Benchmark.CLI.Utilities;

internal static class McpHost
{
    public static CliSessionContext CreateSession()
    {
        return new CliSessionContext(GenerateSessionId());
    }

    private static string GenerateSessionId()
    {
        return Guid.NewGuid().ToString();
    }
}
