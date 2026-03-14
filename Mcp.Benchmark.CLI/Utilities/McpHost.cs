using System;

namespace Mcp.Benchmark.CLI.Utilities;

internal static class McpHost
{
    public static CliSessionContext CreateSession()
    {
        var sessionId = GenerateSessionId();
        var sessionRoot = McpHostHelper.CreateSessionRoot(sessionId);
        var stateDirectory = Path.Combine(sessionRoot, "State");
        var logsDirectory = Path.Combine(sessionRoot, "Logs");

        Directory.CreateDirectory(stateDirectory);
        Directory.CreateDirectory(logsDirectory);

        var logFilePath = Path.Combine(logsDirectory, $"mcpval-{sessionId}.log");
        return new CliSessionContext(sessionId, sessionRoot, stateDirectory, logsDirectory, logFilePath);
    }

    private static string GenerateSessionId()
    {
        return Guid.NewGuid().ToString();
    }
}
