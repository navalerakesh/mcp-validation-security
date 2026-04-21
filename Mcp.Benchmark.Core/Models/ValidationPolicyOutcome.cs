namespace Mcp.Benchmark.Core.Models;

/// <summary>
/// The host-level pass/fail outcome derived from a completed validation result.
/// </summary>
public class ValidationPolicyOutcome
{
    /// <summary>
    /// Gets or sets the normalized policy mode used for evaluation.
    /// </summary>
    public string Mode { get; set; } = ValidationPolicyModes.Balanced;

    /// <summary>
    /// Gets or sets whether the policy considers the validation successful.
    /// </summary>
    public bool Passed { get; set; }

    /// <summary>
    /// Gets or sets the exit code the CLI should use when this policy is applied.
    /// </summary>
    public int RecommendedExitCode { get; set; }

    /// <summary>
    /// Gets or sets a short human-readable summary of the policy decision.
    /// </summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the reasons that drove the policy decision.
    /// </summary>
    public List<string> Reasons { get; set; } = new();

    /// <summary>
    /// Gets or sets the number of suppressible signals that were muted by active suppressions.
    /// </summary>
    public int SuppressedSignalCount { get; set; }

    /// <summary>
    /// Gets or sets the suppressions that were applied to this policy decision.
    /// </summary>
    public List<AppliedPolicySuppression> AppliedSuppressions { get; set; } = new();

    /// <summary>
    /// Gets or sets suppressions that were ignored because they were invalid or expired.
    /// </summary>
    public List<IgnoredPolicySuppression> IgnoredSuppressions { get; set; } = new();
}

public class AppliedPolicySuppression
{
    public string Id { get; set; } = string.Empty;

    public string Owner { get; set; } = string.Empty;

    public string Reason { get; set; } = string.Empty;

    public DateTimeOffset? ExpiresOn { get; set; }

    public int MatchedSignalCount { get; set; }

    public List<string> MatchedSignals { get; set; } = new();
}

public class IgnoredPolicySuppression
{
    public string Id { get; set; } = string.Empty;

    public string Reason { get; set; } = string.Empty;
}