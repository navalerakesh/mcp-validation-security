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
    /// Values less than 1 indicate partial category coverage and should be interpreted separately from the score.
    /// </summary>
    public double CoverageRatio { get; set; } = 1.0;

    /// <summary>
    /// Gets or sets the evidence coverage and confidence summary used to interpret the score.
    /// </summary>
    public EvidenceCoverageSummary EvidenceSummary { get; set; } = new();

    /// <summary>
    /// Gets the fraction of applicable evidence declarations that were directly covered.
    /// </summary>
    public double EvidenceCoverageRatio
    {
        get => EvidenceSummary.EvidenceCoverageRatio;
        set => EvidenceSummary = new EvidenceCoverageSummary
        {
            TotalDeclarations = EvidenceSummary.TotalDeclarations,
            ApplicableDeclarations = EvidenceSummary.ApplicableDeclarations,
            Covered = EvidenceSummary.Covered,
            AuthRequired = EvidenceSummary.AuthRequired,
            Inconclusive = EvidenceSummary.Inconclusive,
            Skipped = EvidenceSummary.Skipped,
            NotApplicable = EvidenceSummary.NotApplicable,
            Unavailable = EvidenceSummary.Unavailable,
            Blocked = EvidenceSummary.Blocked,
            EvidenceCoverageRatio = value,
            EvidenceConfidenceRatio = EvidenceSummary.EvidenceConfidenceRatio,
            ConfidenceLevel = EvidenceSummary.ConfidenceLevel,
            Categories = EvidenceSummary.Categories
        };
    }
}
