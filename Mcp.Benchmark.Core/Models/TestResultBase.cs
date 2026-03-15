namespace Mcp.Benchmark.Core.Models;

/// <summary>
/// Base class for all test results to ensure consistency.
/// </summary>
public abstract class TestResultBase
{
    /// <summary>
    /// Gets or sets the overall test status.
    /// </summary>
    public TestStatus Status { get; set; } = TestStatus.NotRun;

    /// <summary>
    /// Gets or sets the test execution duration.
    /// </summary>
    public TimeSpan Duration { get; set; } = TimeSpan.Zero;

    /// <summary>
    /// Gets or sets a general status message or summary.
    /// </summary>
    public string? Message { get; set; }

    /// <summary>
    /// Gets or sets the compliance/security score (0-100).
    /// Optional for some tests, but common enough to be in base.
    /// </summary>
    public virtual double Score { get; set; } = 0.0;

    /// <summary>
    /// Gets or sets any critical errors encountered during execution.
    /// </summary>
    public List<string> CriticalErrors { get; set; } = new();
}
