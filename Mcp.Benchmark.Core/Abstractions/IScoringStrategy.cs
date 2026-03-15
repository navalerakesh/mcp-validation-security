namespace Mcp.Benchmark.Core.Abstractions;

/// <summary>
/// Defines a strategy for calculating compliance scores.
/// </summary>
/// <typeparam name="TResult">The type of test result to score.</typeparam>
public interface IScoringStrategy<in TResult>
{
    /// <summary>
    /// Calculates the score (0-100) based on the provided result.
    /// </summary>
    /// <param name="result">The test result to evaluate.</param>
    /// <returns>A score between 0 and 100.</returns>
    double CalculateScore(TResult result);
}
