namespace Mcp.Benchmark.Core.Models;

public sealed class ValidationOperationalMetrics
{
    public string RunCorrelationId { get; set; } = string.Empty;

    public double ValidatorOverheadMs { get; init; }

    public double TotalRunDurationMs { get; init; }

    public int RequestBudgetLimit { get; init; }

    public int RequestsStarted { get; init; }

    public int RequestsCompleted { get; init; }

    public int RequestsFailed { get; init; }

    public int RetryCount { get; init; }

    public double RetryDelayMs { get; init; }

    public int TruncatedResponseCount { get; init; }

    public double TargetLatencyP50Ms { get; init; }

    public double TargetLatencyP95Ms { get; init; }

    public double TargetLatencyP99Ms { get; init; }

    public double QueueTimeP95Ms { get; init; }

    public int TargetLatencySampleCount { get; init; }

    public int QueueTimeSampleCount { get; init; }

    public double ThroughputRequestsPerSecond { get; init; }

    public double ErrorRate { get; init; }

    public double EvidenceCoverageRatio { get; set; }

    public IReadOnlyDictionary<string, double> StageDurationMs { get; init; } = new SortedDictionary<string, double>(StringComparer.Ordinal);
}
