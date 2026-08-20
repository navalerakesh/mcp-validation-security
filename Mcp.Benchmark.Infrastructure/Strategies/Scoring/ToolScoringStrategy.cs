using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Constants;

namespace Mcp.Benchmark.Infrastructure.Strategies.Scoring;

/// <summary>
/// Scoring strategy for Tool validation.
/// Score is based on the ratio of passed tests to total tests.
/// </summary>
public class ToolScoringStrategy : IScoringStrategy<ToolTestResult>
{
    public double CalculateScore(ToolTestResult result)
    {
        if (result.ToolsTestFailed == 0 && result.ToolsTestPassed == 0)
        {
            // If no tests ran but status is Passed (e.g. empty list allowed), score is 100
            return result.Status == TestStatus.Passed ? ScoringConstants.ScoreMaximum : ScoringConstants.ScoreMinimum;
        }

        if (result.ToolsTestFailed == 0) return ScoringConstants.ScoreMaximum;

        var totalTests = result.ToolsTestPassed + result.ToolsTestFailed;
        return totalTests > 0 
            ? (double)result.ToolsTestPassed / totalTests * ScoringConstants.ScoreMaximum
            : ScoringConstants.ScoreMinimum;
    }
}
