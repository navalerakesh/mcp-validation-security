namespace Mcp.Benchmark.Infrastructure.Utilities;

/// <summary>
/// Stateless calculator for performance metrics.
/// Extracted from PerformanceValidator so it can be tested independently
/// and reused across validators that need latency/throughput analysis.
/// </summary>
public static class PerformanceMetricsCalculator
{
    /// <summary>
    /// Calculates the percentile value from a list of response times.
    /// </summary>
    /// <param name="times">Response times (will be sorted internally).</param>
    /// <param name="percentile">Percentile to calculate (0.0 to 1.0, e.g., 0.95 for P95).</param>
    public static double GetPercentile(IReadOnlyList<double> times, double percentile)
    {
        if (times.Count == 0) return 0;
        var sorted = times.OrderBy(x => x).ToList();
        var index = (int)Math.Ceiling(percentile * sorted.Count) - 1;
        return sorted[Math.Clamp(index, 0, sorted.Count - 1)];
    }

    /// <summary>
    /// Calculates throughput in requests per second.
    /// </summary>
    public static double CalculateThroughput(int successfulRequests, TimeSpan duration)
    {
        return duration.TotalSeconds > 0 ? successfulRequests / duration.TotalSeconds : 0;
    }

    /// <summary>
    /// Identifies performance bottlenecks based on measured metrics.
    /// Returns a list of findings — empty list means no issues detected.
    /// </summary>
    public static List<string> IdentifyBottlenecks(
        double avgResponseTimeMs,
        double errorRate,
        double p99ResponseTimeMs,
        double? toolCallLatencyMs = null,
        string? toolName = null)
    {
        var findings = new List<string>();

        if (avgResponseTimeMs > 2000)
            findings.Add($"Average response time ({avgResponseTimeMs:F0}ms) exceeds 2000ms threshold");

        if (errorRate > 5.0)
            findings.Add($"Error rate: {errorRate:F1}% (above 5% threshold)");

        if (p99ResponseTimeMs > 5000)
            findings.Add($"P99 latency ({p99ResponseTimeMs:F0}ms) exceeds 5000ms — tail latency concern");

        if (toolCallLatencyMs.HasValue && toolName != null && toolCallLatencyMs > 5000)
        {
            findings.Add($"tools/call latency ({toolName}) exceeds 5000ms SLO ({toolCallLatencyMs:F0}ms)");
        }

        return findings;
    }
}
