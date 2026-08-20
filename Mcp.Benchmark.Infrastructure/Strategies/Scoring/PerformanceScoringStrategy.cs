using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Constants;

namespace Mcp.Benchmark.Infrastructure.Strategies.Scoring;

/// <summary>
/// Scoring strategy for Performance validation.
/// Score starts at 100 and is penalized by non-rate-limited failures and high latency.
/// </summary>
public class PerformanceScoringStrategy : IScoringStrategy<PerformanceTestResult>
{
    public double CalculateScore(PerformanceTestResult result)
    {
        if (result.LoadTesting == null) return ScoringConstants.ScoreMinimum;

        double score = ScoringConstants.ScoreMaximum;
        
        // Deduct for failures attributable to server/runtime instability.
        // Requests rejected by explicit rate limiting remain visible in telemetry
        // but do not count as brokenness for readiness scoring.
        score -= result.LoadTesting.NonRateLimitedFailedRequests * ScoringConstants.FailedRequestPenalty;

        // Deduct for latency (1 point for every 20ms over 200ms)
        if (result.LoadTesting.AverageResponseTimeMs > ScoringConstants.LatencyBaselineMs)
        {
            score -= (result.LoadTesting.AverageResponseTimeMs - ScoringConstants.LatencyBaselineMs) / ScoringConstants.LatencyPenaltyPerMs;
        }

        return Math.Max(ScoringConstants.ScoreMinimum, Math.Min(ScoringConstants.ScoreMaximum, score));
    }
}
