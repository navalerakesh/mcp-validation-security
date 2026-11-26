namespace Mcp.Benchmark.Core.Models;

public sealed class ValidationOperationalMetrics
{
    private IReadOnlyList<RequestFailureCount>? _requestFailures;
    public string RunCorrelationId { get; set; } = string.Empty;

    public double ValidatorOverheadMs { get; init; }

    public double TotalRunDurationMs { get; init; }

    public int RequestBudgetLimit { get; init; }

    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public long? CatalogPayloadCharactersReserved { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public long? CatalogPayloadCharacterLimit { get; set; }

    public int RequestsStarted { get; init; }

    public int RequestsCompleted { get; init; }

    public int RequestsFailed { get; init; }

    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<RequestFailureCount>? RequestFailures { get => _requestFailures; init => _requestFailures = value; }

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

    public static bool IsValidCatalogBudget(long? reserved, long? limit) =>
        reserved == null && limit == null || reserved is >= 0 &&
        limit is > 0 and <= Constants.ExecutionPolicyDefaults.MaximumCatalogPayloadCharacters && reserved <= limit;

    public ValidationOperationalMetrics CloneWithoutSecrets()
    {
        var clone = (ValidationOperationalMetrics)MemberwiseClone();
        clone._requestFailures = RequestFailureCount.IsValidCollection(RequestFailures, RequestsFailed)
            ? RequestFailures?.Select(entry => entry with { }).ToArray() : null;
        return clone;
    }
}

public sealed record MetricSampleWindow(long ObservedCount, int RetainedCount, int Capacity, long RejectedCount = 0)
{
    public long DiscardedCount => ObservedCount - RetainedCount - RejectedCount;

    public string Strategy => "mostRecent";
}

public enum RequestFailureKind
{
    Unspecified,
    HttpStatus,
    RequestException,
    ResponseRejected,
    RequestCancelled
}

public sealed record RequestFailureCount(string Method, int? HttpStatusCode, RequestFailureKind Kind, int Count)
{
    public const int MaximumBuckets = 5040;

    public static string NormalizeMethod(string? method) =>
        method is "GET" or "POST" or "PUT" or "PATCH" or "DELETE" or "HEAD" or "OPTIONS" or "CONNECT" or "TRACE" ? method : "OTHER";

    public static bool IsValidCollection(IReadOnlyList<RequestFailureCount>? entries, int expectedFailures)
    {
        if (entries == null) return true;
        if (entries.Count > MaximumBuckets) return false;
        var keys = new HashSet<(string, int?, RequestFailureKind)>();
        long total = 0;
        foreach (var entry in entries)
        {
            if (entry == null || entry.Method != NormalizeMethod(entry.Method) || !Enum.IsDefined(entry.Kind) || entry.Count <= 0 ||
                (entry.Kind == RequestFailureKind.HttpStatus ? entry.HttpStatusCode is not (>= 100 and <= 599) : entry.HttpStatusCode != null) ||
                !keys.Add((entry.Method, entry.HttpStatusCode, entry.Kind))) return false;
            total += entry.Count;
        }
        return total == expectedFailures;
    }
}

public sealed record ValidatorProcessResourceSnapshot(TimeSpan TotalProcessorTime, long WorkingSetBytes);

public sealed record ValidatorProcessResourceMetrics(double? ProcessorTimeDeltaMs, long? WorkingSetStartBytes, long? WorkingSetEndBytes)
{
    public string Scope => "validatorProcess";

    public bool IncludesConcurrentActivity => true;
}
