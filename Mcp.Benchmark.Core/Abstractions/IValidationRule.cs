using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.Core.Abstractions;

/// <summary>
/// Represents a single atomic validation rule.
/// </summary>
/// <typeparam name="TContext">The context required to evaluate the rule.</typeparam>
public interface IValidationRule<TContext>
{
    /// <summary>
    /// Gets the unique identifier for the rule.
    /// </summary>
    string Id { get; }

    /// <summary>
    /// Gets the human-readable description of the rule.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Gets the MCP specification version this rule applies to (e.g., "2024-11-25").
    /// </summary>
    string SpecVersion { get; }

    /// <summary>
    /// Evaluates the rule against the provided context.
    /// </summary>
    Task<RuleResult> EvaluateAsync(TContext context, CancellationToken cancellationToken);
}

/// <summary>
/// Represents the result of a rule evaluation.
/// </summary>
public class RuleResult
{
    /// <summary>
    /// Gets or sets whether the rule passed.
    /// </summary>
    public bool IsCompliant { get; set; }

    /// <summary>
    /// Gets or sets the reason for failure (if any).
    /// </summary>
    public string? FailureReason { get; set; }

    /// <summary>
    /// Gets or sets the severity of the violation.
    /// </summary>
    public ViolationSeverity Severity { get; set; }

    /// <summary>
    /// Gets or sets the impact on the score (0-100).
    /// </summary>
    public double ScoreImpact { get; set; }
}
