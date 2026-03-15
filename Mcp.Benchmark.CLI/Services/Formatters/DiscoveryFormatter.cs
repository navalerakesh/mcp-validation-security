using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.CLI.Services.Formatters;

public static class DiscoveryFormatter
{
    public static void DisplayPlan(McpServerConfig serverConfig, string format, Action<string, ConsoleColor> writeHeader, Action<string> writeInfo)
    {
        Console.WriteLine();
        writeHeader("CAPABILITY DISCOVERY", ConsoleColor.Cyan);

        writeInfo($"Server: {serverConfig.Endpoint}");
        writeInfo($"Transport: {serverConfig.Transport}");
        writeInfo($"Output Format: {format.ToUpper()}");
        Console.WriteLine();
        Console.Write("Discovering server capabilities... ");
    }
}
