using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.CLI.Services.Formatters;

public static class PerformanceFormatter
{
    public static void DisplayBreakdown(ValidationResult result, bool useColors)
    {
        if (result.PerformanceTesting == null) return;

        Console.WriteLine();
        FormatterUtils.WriteLineWithColor("PERFORMANCE TESTING", ConsoleColor.Cyan, useColors);
        Console.WriteLine(new string('-', 40));

        var perf = result.PerformanceTesting;
        Console.WriteLine($"Score: {perf.Score:F1}%");

        if (!string.IsNullOrEmpty(perf.Message))
        {
            Console.WriteLine($"Note:  {perf.Message}");
        }

        if (perf.LoadTesting != null && perf.LoadTesting.TotalRequests > 0)
        {
            Console.WriteLine($"Requests:   {perf.LoadTesting.TotalRequests} ({perf.LoadTesting.SuccessfulRequests} success, {perf.LoadTesting.FailedRequests} failed)");
            Console.WriteLine($"Latency:    {perf.LoadTesting.AverageResponseTimeMs:F1}ms avg, {perf.LoadTesting.P95ResponseTimeMs:F1}ms p95");
            Console.WriteLine($"Throughput: {perf.LoadTesting.RequestsPerSecond:F1} req/sec");
        }

        if (perf.PerformanceBottlenecks.Any())
        {
            DisplayFailureTable(perf.PerformanceBottlenecks, 0, useColors);
        }
        else if (perf.Status == TestStatus.Passed && perf.Score > 0)
        {
            FormatterUtils.WriteLineWithColor("✅ No performance bottlenecks detected", ConsoleColor.Green, useColors);
        }
    }

    public static void DisplayFailureTable(IEnumerable<string> bottlenecks, int indent, bool useColors)
    {
        var indentString = new string(' ', indent);

        Console.WriteLine();
        FormatterUtils.WriteLineWithColor($"{indentString}PERFORMANCE BOTTLENECKS", ConsoleColor.Red, useColors);
        Console.WriteLine($"{indentString}{new string('-', 30)}");

        foreach (var bottleneck in bottlenecks.Take(10))
        {
            var issueType = bottleneck.Contains("response time") ? "LATENCY" :
                          bottleneck.Contains("memory") ? "MEMORY" :
                          bottleneck.Contains("CPU") ? "CPU" :
                          bottleneck.Contains("connection") ? "NETWORK" :
                          bottleneck.Contains("load") ? "LOAD" :
                          "GENERAL";

            var severity = bottleneck.Contains("critical") || bottleneck.Contains("failed") ? "CRITICAL" :
                          bottleneck.Contains("high") ? "HIGH" :
                          "MEDIUM";

            var description = FormatterUtils.TruncateAndPad(bottleneck, 50);

            Console.Write(indentString);
            FormatterUtils.WriteWithColor(issueType.PadRight(15), ConsoleColor.White, useColors);
            FormatterUtils.WriteWithColor(severity.PadRight(10), ConsoleColor.Yellow, useColors);
            FormatterUtils.WriteLineWithColor(description, ConsoleColor.Gray, useColors);
        }

        if (bottlenecks.Count() > 10)
        {
            FormatterUtils.WriteWithColor($"{indentString}... and {bottlenecks.Count() - 10} more performance issues", ConsoleColor.Gray, useColors);
            Console.WriteLine();
        }
    }
}
