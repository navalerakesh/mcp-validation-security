namespace Mcp.Benchmark.Core.Models;

/// <summary>
/// Represents the result of a scoring calculation.
/// </summary>
public class ScoringResult
{
    /// <summary>
    /// Gets or sets the overall calculated score (0-100).
    /// </summary>
    public double OverallScore { get; set; }

    /// <summary>
    /// Gets or sets the determined validation status based on the score and critical failures.
    /// </summary>
    public ValidationStatus Status { get; set; }

    /// <summary>
    /// Gets or sets notes explaining the scoring decision, including penalties applied.
    /// </summary>
    public List<string> ScoringNotes { get; set; } = new();

    /// <summary>
    /// Gets or sets the breakdown of scores by category.
    /// </summary>
    public Dictionary<string, double> CategoryScores { get; set; } = new();

    /// <summary>
    /// Gets or sets the fraction of total weighting that was executed during scoring.
    /// Values less than 1 indicate partial coverage, which is used as a multiplier.
    /// </summary>
    public double CoverageRatio { get; set; } = 1.0;
}
