using System;

namespace Mcp.Benchmark.CLI.Utilities;

internal static class McpHostHelper
{
    private static readonly Lazy<string> BasePathValue = new(() =>
    {
        string basePath;
        if (OperatingSystem.IsWindows())
        {
            basePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "McpCli");
        }
        else
        {
            basePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) ?? ".",
                ".local",
                "share",
                "mcp-cli");
        }

        return basePath;
    });

    public static string BasePath => BasePathValue.Value;

    public static string CreateSessionRoot(string sessionId)
    {
        Directory.CreateDirectory(BasePath);
        var sessionsRoot = Path.Combine(BasePath, "Sessions");
        Directory.CreateDirectory(sessionsRoot);
        var sessionRoot = Path.Combine(sessionsRoot, sessionId);
        Directory.CreateDirectory(sessionRoot);
        return sessionRoot;
    }
}
