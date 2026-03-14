using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.Infrastructure.Strategies.Scoring;

/// <summary>
/// Scoring strategy for Prompt validation.
/// Score is based on the percentage of prompts that were successfully discovered and validated.
/// </summary>
public class PromptScoringStrategy : IScoringStrategy<PromptTestResult>
{
    public double CalculateScore(PromptTestResult result)
    {
        if (result.PromptResults.Count == 0)
        {
            return result.Status == TestStatus.Passed ? 100.0 : 0.0;
        }

        var passedCount = result.PromptResults.Count(r => r.Status == TestStatus.Passed);
        return (double)passedCount / result.PromptResults.Count * 100.0;
    }
}
