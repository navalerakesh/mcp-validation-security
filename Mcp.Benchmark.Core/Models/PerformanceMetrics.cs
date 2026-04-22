namespace Mcp.Benchmark.Core.Models;

/// <summary>
/// Represents load testing results with concurrent connection and request metrics.
/// </summary>
public class LoadTestResult
{
    /// <summary>
    /// Gets or sets the maximum number of concurrent connections tested.
    /// </summary>
    public int MaxConcurrentConnections { get; set; } = 0;

    /// <summary>
    /// Gets or sets the total number of requests sent during load testing.
    /// </summary>
    public int TotalRequests { get; set; } = 0;

    /// <summary>
    /// Gets or sets the number of successful requests.
    /// </summary>
    public int SuccessfulRequests { get; set; } = 0;

    /// <summary>
    /// Gets or sets the number of failed requests.
    /// </summary>
    public int FailedRequests { get; set; } = 0;

    /// <summary>
    /// Gets or sets the number of failed requests caused by upstream rate limiting or backoff controls.
    /// These requests remain part of the observed failure rate, but they are scored separately from
    /// genuine server-side failures.
    /// </summary>
    public int RateLimitedRequests { get; set; } = 0;

    /// <summary>
    /// Gets or sets the number of probe rounds executed before a final calibrated result was produced.
    /// Values greater than one indicate the validator had to ramp up or recalibrate before settling.
    /// </summary>
    public int ProbeRoundsExecuted { get; set; } = 0;

    /// <summary>
    /// Gets or sets the total number of rate-limited requests observed across all probe rounds,
    /// including discarded calibration attempts.
    /// </summary>
    public int ObservedRateLimitedRequests { get; set; } = 0;

    /// <summary>
    /// Gets or sets the total number of retryable transient failures observed across all probe rounds,
    /// including discarded calibration attempts.
    /// </summary>
    public int ObservedTransientFailures { get; set; } = 0;

    /// <summary>
    /// Gets or sets the average response time in milliseconds.
    /// </summary>
    public double AverageResponseTimeMs { get; set; } = 0.0;

    /// <summary>
    /// Gets or sets the median response time in milliseconds.
    /// </summary>
    public double MedianResponseTimeMs { get; set; } = 0.0;

    /// <summary>
    /// Gets or sets the 95th percentile response time in milliseconds.
    /// </summary>
    public double P95ResponseTimeMs { get; set; } = 0.0;

    /// <summary>
    /// Gets or sets the 99th percentile response time in milliseconds.
    /// </summary>
    public double P99ResponseTimeMs { get; set; } = 0.0;

    /// <summary>
    /// Gets or sets the maximum response time observed in milliseconds.
    /// </summary>
    public double MaxResponseTimeMs { get; set; } = 0.0;

    /// <summary>
    /// Gets or sets the minimum response time observed in milliseconds.
    /// </summary>
    public double MinResponseTimeMs { get; set; } = 0.0;

    /// <summary>
    /// Gets or sets the requests per second throughput.
    /// </summary>
    public double RequestsPerSecond { get; set; } = 0.0;

    /// <summary>
    /// Gets or sets the error rate as a percentage.
    /// </summary>
    public double ErrorRate => TotalRequests > 0 ? (double)FailedRequests / TotalRequests * 100 : 0.0;

    /// <summary>
    /// Gets the number of failed requests that were not caused by upstream rate limiting.
    /// This is the failure bucket used for readiness scoring.
    /// </summary>
    public int NonRateLimitedFailedRequests => Math.Max(0, FailedRequests - RateLimitedRequests);

    /// <summary>
    /// Gets a value indicating whether throttling or retryable transport pressure was observed
    /// during any probe round, even if the final calibrated round completed cleanly.
    /// </summary>
    public bool PressureSignalsObserved => ObservedRateLimitedRequests > 0 || ObservedTransientFailures > 0 || ProbeRoundsExecuted > 1;

    /// <summary>
    /// Gets or sets connection-related errors encountered.
    /// </summary>
    public List<string> ConnectionErrors { get; set; } = new();

    /// <summary>
    /// Gets or sets whether the server maintained stability under load.
    /// </summary>
    public bool ServerStabilityMaintained { get; set; } = false;
}

/// <summary>
/// Represents response time benchmark results for various operations.
/// </summary>
public class ResponseTimeBenchmark
{
    /// <summary>
    /// Gets or sets benchmark results for individual operations.
    /// </summary>
    public List<OperationBenchmark> OperationBenchmarks { get; set; } = new();

    /// <summary>
    /// Gets or sets the overall average response time across all operations in milliseconds.
    /// </summary>
    public double OverallAverageResponseTimeMs { get; set; } = 0.0;

    /// <summary>
    /// Gets or sets operations that exceeded acceptable response time thresholds.
    /// </summary>
    public List<string> SlowOperations { get; set; } = new();

    /// <summary>
    /// Gets or sets the fastest operation measured.
    /// </summary>
    public string? FastestOperation { get; set; }

    /// <summary>
    /// Gets or sets the slowest operation measured.
    /// </summary>
    public string? SlowestOperation { get; set; }
}

/// <summary>
/// Represents benchmark results for a specific operation.
/// </summary>
public class OperationBenchmark
{
    /// <summary>
    /// Gets or sets the operation name.
    /// </summary>
    public string OperationName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the number of times this operation was executed.
    /// </summary>
    public int ExecutionCount { get; set; } = 0;

    /// <summary>
    /// Gets or sets the average response time for this operation in milliseconds.
    /// </summary>
    public double AverageResponseTimeMs { get; set; } = 0.0;

    /// <summary>
    /// Gets or sets the minimum response time for this operation in milliseconds.
    /// </summary>
    public double MinResponseTimeMs { get; set; } = 0.0;

    /// <summary>
    /// Gets or sets the maximum response time for this operation in milliseconds.
    /// </summary>
    public double MaxResponseTimeMs { get; set; } = 0.0;

    /// <summary>
    /// Gets or sets the standard deviation of response times.
    /// </summary>
    public double StandardDeviation { get; set; } = 0.0;

    /// <summary>
    /// Gets or sets whether this operation meets performance expectations.
    /// </summary>
    public bool MeetsPerformanceExpectations { get; set; } = false;
}

/// <summary>
/// Represents resource usage metrics during testing.
/// </summary>
public class ResourceUsageMetrics
{
    /// <summary>
    /// Gets or sets memory usage statistics.
    /// </summary>
    public MemoryUsageMetrics Memory { get; set; } = new();

    /// <summary>
    /// Gets or sets CPU usage statistics.
    /// </summary>
    public CpuUsageMetrics Cpu { get; set; } = new();

    /// <summary>
    /// Gets or sets network usage statistics.
    /// </summary>
    public NetworkUsageMetrics Network { get; set; } = new();

    /// <summary>
    /// Gets or sets whether resource usage remained within acceptable limits.
    /// </summary>
    public bool WithinAcceptableLimits { get; set; } = false;

    /// <summary>
    /// Gets or sets any resource exhaustion events detected.
    /// </summary>
    public List<string> ResourceExhaustionEvents { get; set; } = new();
}

/// <summary>
/// Represents memory usage metrics.
/// </summary>
public class MemoryUsageMetrics
{
    /// <summary>
    /// Gets or sets the initial memory usage in bytes.
    /// </summary>
    public long InitialMemoryBytes { get; set; } = 0;

    /// <summary>
    /// Gets or sets the peak memory usage in bytes.
    /// </summary>
    public long PeakMemoryBytes { get; set; } = 0;

    /// <summary>
    /// Gets or sets the final memory usage in bytes.
    /// </summary>
    public long FinalMemoryBytes { get; set; } = 0;

    /// <summary>
    /// Gets or sets the average memory usage in bytes.
    /// </summary>
    public long AverageMemoryBytes { get; set; } = 0;

    /// <summary>
    /// Gets or sets whether memory leaks were detected.
    /// </summary>
    public bool MemoryLeaksDetected { get; set; } = false;
}

/// <summary>
/// Represents CPU usage metrics.
/// </summary>
public class CpuUsageMetrics
{
    /// <summary>
    /// Gets or sets the average CPU usage percentage.
    /// </summary>
    public double AverageCpuUsagePercent { get; set; } = 0.0;

    /// <summary>
    /// Gets or sets the peak CPU usage percentage.
    /// </summary>
    public double PeakCpuUsagePercent { get; set; } = 0.0;

    /// <summary>
    /// Gets or sets whether CPU usage remained within acceptable limits.
    /// </summary>
    public bool WithinAcceptableLimits { get; set; } = false;
}

/// <summary>
/// Represents network usage metrics.
/// </summary>
public class NetworkUsageMetrics
{
    /// <summary>
    /// Gets or sets the total bytes sent.
    /// </summary>
    public long TotalBytesSent { get; set; } = 0;

    /// <summary>
    /// Gets or sets the total bytes received.
    /// </summary>
    public long TotalBytesReceived { get; set; } = 0;

    /// <summary>
    /// Gets or sets the average network latency in milliseconds.
    /// </summary>
    public double AverageNetworkLatencyMs { get; set; } = 0.0;

    /// <summary>
    /// Gets or sets the number of network errors encountered.
    /// </summary>
    public int NetworkErrors { get; set; } = 0;
}

/// <summary>
/// Represents throughput measurement metrics.
/// </summary>
public class ThroughputMetrics
{
    /// <summary>
    /// Gets or sets the measured requests per second.
    /// </summary>
    public double RequestsPerSecond { get; set; } = 0.0;

    /// <summary>
    /// Gets or sets the measured bytes per second throughput.
    /// </summary>
    public double BytesPerSecond { get; set; } = 0.0;

    /// <summary>
    /// Gets or sets the measured transactions per minute.
    /// </summary>
    public double TransactionsPerMinute { get; set; } = 0.0;

    /// <summary>
    /// Gets or sets the peak throughput achieved.
    /// </summary>
    public double PeakThroughput { get; set; } = 0.0;

    /// <summary>
    /// Gets or sets whether the throughput meets expected performance targets.
    /// </summary>
    public bool MeetsPerformanceTargets { get; set; } = false;
}

/// <summary>
/// Represents the result of testing a specific error handling scenario.
/// </summary>
public class ErrorScenarioResult
{
    /// <summary>
    /// Gets or sets the error scenario name.
    /// </summary>
    public string ScenarioName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the type of error simualted.
    /// </summary>
    public string ErrorType { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets whether the error was handled correctly.
    /// </summary>
    public bool HandledCorrectly { get; set; } = false;

    /// <summary>
    /// Gets or sets the expected error response.
    /// </summary>
    public string ExpectedResponse { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the actual error response received.
    /// </summary>
    public string ActualResponse { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the response time for error handling in milliseconds.
    /// </summary>
    public double ErrorHandlingTimeMs { get; set; } = 0.0;

    /// <summary>
    /// Gets or sets whether the server recovered gracefully from the error.
    /// </summary>
    public bool GracefulRecovery { get; set; } = false;

    /// <summary>
    /// Gets or sets any additional issues identified during error testing.
    /// </summary>
    public List<string> AdditionalIssues { get; set; } = new();
}

/// <summary>
/// Represents the result of testing server recovery capabilities.
/// </summary>
public class RecoveryTestResult
{
    /// <summary>
    /// Gets or sets the recovery scenario name.
    /// </summary>
    public string ScenarioName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the type of failure simulated.
    /// </summary>
    public string FailureType { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets whether the server recovered successfully.
    /// </summary>
    public bool RecoverySuccessful { get; set; } = false;

    /// <summary>
    /// Gets or sets the time taken to recover in milliseconds.
    /// </summary>
    public double RecoveryTimeMs { get; set; } = 0.0;

    /// <summary>
    /// Gets or sets whether data integrity was maintained during recovery.
    /// </summary>
    public bool DataIntegrityMaintained { get; set; } = false;

    /// <summary>
    /// Gets or sets whether service availability was restored completely.
    /// </summary>
    public bool ServiceAvailabilityRestored { get; set; } = false;

    /// <summary>
    /// Gets or sets any data or functionality lost during the recovery process.
    /// </summary>
    public List<string> RecoveryLosses { get; set; } = new();
}
