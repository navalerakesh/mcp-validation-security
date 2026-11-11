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

    public double? TargetLatencyP50Ms { get; init; }

    public double? TargetLatencyP95Ms { get; init; }

    public double? TargetLatencyP99Ms { get; init; }

    public double? QueueTimeP95Ms { get; init; }

    public int TargetLatencySampleCount { get; init; }

    public int QueueTimeSampleCount { get; init; }

    public MetricSampleWindow? TargetLatencyWindow { get; init; }

    public MetricSampleWindow? QueueTimeWindow { get; init; }

    public double? ThroughputRequestsPerSecond { get; init; }

    public double? ErrorRate { get; init; }

    public double EvidenceCoverageRatio { get; set; }

    public ValidatorProcessResourceMetrics? ValidatorProcessResources { get; set; }

    public IReadOnlyDictionary<string, double> StageDurationMs { get; init; } = new SortedDictionary<string, double>(StringComparer.Ordinal);
}

public sealed record MetricSampleWindow(long ObservedCount, int RetainedCount, int Capacity, long RejectedCount = 0)
{
    public long DiscardedCount => ObservedCount - RetainedCount - RejectedCount;

    public string Strategy => "mostRecent";
}

public sealed record ValidatorProcessResourceSnapshot(TimeSpan TotalProcessorTime, long WorkingSetBytes);

public sealed record ValidatorProcessResourceMetrics(double? ProcessorTimeDeltaMs, long? WorkingSetStartBytes, long? WorkingSetEndBytes)
{
    public string Scope => "validatorProcess";

    public bool IncludesConcurrentActivity => true;
}
