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

        Directory.CreateDirectory(basePath);
        return basePath;
    });

    public static string BasePath => BasePathValue.Value;

    public static string SessionsRoot
    {
        get
        {
            var path = Path.Combine(BasePath, "Sessions");
            Directory.CreateDirectory(path);
            return path;
        }
    }

    public static string CreateSessionRoot(string sessionId)
    {
        var sessionRoot = Path.Combine(SessionsRoot, sessionId);
        Directory.CreateDirectory(sessionRoot);
        return sessionRoot;
    }
}
