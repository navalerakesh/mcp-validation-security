using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.Infrastructure.Strategies.Scoring;

/// <summary>
/// Scoring strategy for Resource validation.
/// Score is based on the percentage of resources that were successfully discovered and accessed.
/// </summary>
public class ResourceScoringStrategy : IScoringStrategy<ResourceTestResult>
{
    public double CalculateScore(ResourceTestResult result)
    {
        if (result.ResourceResults.Count == 0)
        {
            return result.Status == TestStatus.Passed ? 100.0 : 0.0;
        }

        var passedCount = result.ResourceResults.Count(r => r.Status == TestStatus.Passed);
        return (double)passedCount / result.ResourceResults.Count * 100.0;
    }
}
