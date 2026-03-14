using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.Core.Abstractions;

/// <summary>
/// Defines a strategy for calculating the overall validation score and status.
/// </summary>
public interface IAggregateScoringStrategy
{
    /// <summary>
    /// Calculates the score and status for a validation result.
    /// </summary>
    /// <param name="result">The validation result to evaluate.</param>
    /// <returns>The calculated scoring result.</returns>
    ScoringResult CalculateScore(ValidationResult result);
}
