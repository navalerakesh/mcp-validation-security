using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.Infrastructure.Strategies.Scoring;

/// <summary>
/// Scoring strategy for Performance validation.
/// Score starts at 100 and is penalized by errors and high latency.
/// </summary>
public class PerformanceScoringStrategy : IScoringStrategy<PerformanceTestResult>
{
    public double CalculateScore(PerformanceTestResult result)
    {
        if (result.LoadTesting == null) return 0.0;

        double score = 100.0;
        
        // Deduct for failed requests (5 points per failure)
        score -= result.LoadTesting.FailedRequests * 5;

        // Deduct for latency (1 point for every 20ms over 200ms)
        if (result.LoadTesting.AverageResponseTimeMs > 200)
        {
            score -= (result.LoadTesting.AverageResponseTimeMs - 200) / 20.0;
        }

        return Math.Max(0, Math.Min(100, score));
    }
}
