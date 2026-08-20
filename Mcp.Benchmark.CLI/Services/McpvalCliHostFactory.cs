using Mcp.Benchmark.CLI.Utilities;
using Microsoft.Extensions.Hosting;

namespace Mcp.Benchmark.CLI.Services;

internal static class McpvalCliHostFactory
{
    internal static IHostBuilder Create(string[] args, CliSessionContext sessionContext) =>
        global::Program.CreateHostBuilder(args, sessionContext);
}