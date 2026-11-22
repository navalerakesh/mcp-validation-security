using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.Core.Services;

public static class ValidationObservability
{
    private const int RetainedSampleCapacity = 4096;
    public const string InstrumentationName = "McpVal.Validation";
    public const string InstrumentationVersion = "1.0.0";

    public static readonly ActivitySource ActivitySource = new(InstrumentationName, InstrumentationVersion);
    public static readonly Meter Meter = new(InstrumentationName, InstrumentationVersion);

    private static readonly Histogram<double> RequestDuration = Meter.CreateHistogram<double>("mcpval.target.request.duration", "ms");
    private static readonly Histogram<double> QueueDuration = Meter.CreateHistogram<double>("mcpval.validator.queue.duration", "ms");
    private static readonly Histogram<double> RetryDelay = Meter.CreateHistogram<double>("mcpval.target.retry.delay", "ms");
    private static readonly Histogram<double> StageDuration = Meter.CreateHistogram<double>("mcpval.validator.stage.duration", "ms");
    private static readonly Counter<long> RequestCounter = Meter.CreateCounter<long>("mcpval.target.requests");
    private static readonly Counter<long> RetryCounter = Meter.CreateCounter<long>("mcpval.target.retries");
    private static readonly Counter<long> TruncationCounter = Meter.CreateCounter<long>("mcpval.target.response.rejections");
    private static readonly AsyncLocal<RunCollector?> Current = new();

    public static ValidationRunTelemetry BeginRun(string correlationId, int requestBudgetLimit)
    {
        var parent = Current.Value;
        var collector = new RunCollector(correlationId, requestBudgetLimit);
        Current.Value = collector;
        var activity = ActivitySource.StartActivity("mcpval.validation.run", ActivityKind.Internal);
        activity?.SetTag("mcpval.run.id", correlationId);
        activity?.SetTag("mcpval.request.budget", requestBudgetLimit);
        return new ValidationRunTelemetry(parent, collector, activity);
    }

    public static IDisposable MeasureStage(string stageName)
    {
        var activity = ActivitySource.StartActivity($"mcpval.stage.{stageName}", ActivityKind.Internal);
        activity?.SetTag("mcpval.stage.name", stageName);
        return new StageScope(stageName, activity);
    }

    public static void RecordQueue(double milliseconds)
    {
        if (IsValidDuration(milliseconds)) QueueDuration.Record(milliseconds);
        Current.Value?.QueueTimes.Record(milliseconds);
    }

    public static void RecordRequestStarted(string? method)
    {
        RequestCounter.Add(1, new KeyValuePair<string, object?>("mcpval.request.state", "started"));
        var collector = Current.Value;
        if (collector != null) Interlocked.Increment(ref collector.RequestsStarted);
    }

    public static void RecordRequestCompleted(string? method, double milliseconds, bool success) =>
        RecordRequestCompleted(method, milliseconds, success, null, RequestFailureKind.Unspecified);

    public static void RecordRequestCompleted(string? method, double milliseconds, bool success,
        int? httpStatusCode = null, RequestFailureKind failureKind = RequestFailureKind.Unspecified)
    {
        if (IsValidDuration(milliseconds))
            RequestDuration.Record(milliseconds, new KeyValuePair<string, object?>("mcpval.request.state", success ? "completed" : "failed"));
        var collector = Current.Value;
        if (collector == null) return;
        collector.TargetDurations.Record(milliseconds);
        Interlocked.Increment(ref collector.RequestsCompleted);
        if (!success)
        {
            Interlocked.Increment(ref collector.RequestsFailed);
            collector.RecordFailure(method, httpStatusCode, failureKind);
        }
    }

    public static void RecordRetry(TimeSpan delay)
    {
        RetryCounter.Add(1);
        RetryDelay.Record(delay.TotalMilliseconds);
        var collector = Current.Value;
        if (collector == null) return;
        Interlocked.Increment(ref collector.RetryCount);
        AddDouble(ref collector.RetryDelayMs, delay.TotalMilliseconds);
    }

    public static void RecordResponseRejected(bool responseWasInitiallySuccessful = true)
    {
        TruncationCounter.Add(1);
        var collector = Current.Value;
        if (collector == null) return;
        Interlocked.Increment(ref collector.TruncatedResponseCount);
        if (responseWasInitiallySuccessful)
        {
            Interlocked.Increment(ref collector.RequestsFailed);
            collector.RecordFailure(null, null, RequestFailureKind.ResponseRejected);
        }
    }

    private static void AddDouble(ref double target, double value)
    {
        double initial;
        double updated;
        do
        {
            initial = target;
            updated = initial + value;
        }
        while (Interlocked.CompareExchange(ref target, updated, initial) != initial);
    }

    private static bool IsValidDuration(double value) => double.IsFinite(value) && value >= 0;

    private static double? Percentile(IEnumerable<double> values, double percentile)
    {
        var ordered = values.OrderBy(value => value).ToArray();
        if (ordered.Length == 0) return null;
        var index = (int)Math.Ceiling(percentile * ordered.Length) - 1;
        return Math.Round(ordered[Math.Clamp(index, 0, ordered.Length - 1)], 3);
    }

    public sealed class ValidationRunTelemetry : IDisposable
    {
        private readonly RunCollector? _parent;
        private readonly RunCollector _collector;
        private readonly Activity? _activity;
        private bool _disposed;

        internal ValidationRunTelemetry(RunCollector? parent, RunCollector collector, Activity? activity)
        {
            _parent = parent;
            _collector = collector;
            _activity = activity;
        }

        public ValidationOperationalMetrics Complete(double totalRunDurationMs)
        {
            if (!IsValidDuration(totalRunDurationMs))
                throw new ArgumentOutOfRangeException(nameof(totalRunDurationMs), "Run duration must be finite and non-negative.");
            var elapsedSeconds = totalRunDurationMs / 1000.0;
            var targetSnapshot = _collector.TargetDurations.Snapshot();
            var queueSnapshot = _collector.QueueTimes.Snapshot();
            var targetDurationSamples = targetSnapshot.Values;
            var queueTimeSamples = queueSnapshot.Values;
            var localOverheadMs = _collector.StageDurations
                .Where(pair => pair.Key is "validation.scenarios" or "validation.scoring" or "validation.trust" or "validation.verdict")
                .Sum(pair => pair.Value);
            return new ValidationOperationalMetrics
            {
                RunCorrelationId = _collector.CorrelationId,
                ValidatorOverheadMs = Math.Round(localOverheadMs, 3),
                TotalRunDurationMs = Math.Round(totalRunDurationMs, 3),
                RequestBudgetLimit = _collector.RequestBudgetLimit,
                RequestsStarted = _collector.RequestsStarted,
                RequestsCompleted = _collector.RequestsCompleted,
                RequestsFailed = _collector.RequestsFailed,
                RequestFailures = _collector.FailureCounts
                    .OrderBy(pair => pair.Key.Method, StringComparer.Ordinal)
                    .ThenBy(pair => pair.Key.Status).ThenBy(pair => pair.Key.Kind)
                    .Select(pair => new RequestFailureCount(pair.Key.Method, pair.Key.Status, pair.Key.Kind, pair.Value)).ToArray(),
                RetryCount = _collector.RetryCount,
                RetryDelayMs = Math.Round(_collector.RetryDelayMs, 3),
                TruncatedResponseCount = _collector.TruncatedResponseCount,
                TargetLatencyP50Ms = Percentile(targetDurationSamples, 0.50),
                TargetLatencyP95Ms = Percentile(targetDurationSamples, 0.95),
                TargetLatencyP99Ms = Percentile(targetDurationSamples, 0.99),
                QueueTimeP95Ms = Percentile(queueTimeSamples, 0.95),
                TargetLatencySampleCount = targetDurationSamples.Length,
                QueueTimeSampleCount = queueTimeSamples.Length,
                TargetLatencyWindow = targetSnapshot.Window,
                QueueTimeWindow = queueSnapshot.Window,
                ThroughputRequestsPerSecond = elapsedSeconds > 0 ? Math.Round(_collector.RequestsCompleted / elapsedSeconds, 3) : null,
                ErrorRate = _collector.RequestsCompleted > 0 ? Math.Round(Math.Min(1, (double)_collector.RequestsFailed / _collector.RequestsCompleted), 4) : null,
                StageDurationMs = new SortedDictionary<string, double>(
                    _collector.StageDurations.ToDictionary(pair => pair.Key, pair => Math.Round(pair.Value, 3), StringComparer.Ordinal),
                    StringComparer.Ordinal)
            };
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _activity?.Dispose();
            Current.Value = _parent;
        }
    }

    private sealed class StageScope : IDisposable
    {
        private readonly string _stageName;
        private readonly Activity? _activity;
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();

        public StageScope(string stageName, Activity? activity)
        {
            _stageName = stageName;
            _activity = activity;
        }

        public void Dispose()
        {
            _stopwatch.Stop();
            StageDuration.Record(_stopwatch.Elapsed.TotalMilliseconds, new KeyValuePair<string, object?>("mcpval.stage.name", _stageName));
            Current.Value?.StageDurations.AddOrUpdate(_stageName, _stopwatch.Elapsed.TotalMilliseconds, (_, value) => value + _stopwatch.Elapsed.TotalMilliseconds);
            _activity?.Dispose();
        }
    }

    internal sealed class RunCollector
    {
        public RunCollector(string correlationId, int requestBudgetLimit)
        {
            CorrelationId = correlationId;
            RequestBudgetLimit = requestBudgetLimit;
        }

        public string CorrelationId { get; }
        public int RequestBudgetLimit { get; }
        public int RequestsStarted;
        public int RequestsCompleted;
        public int RequestsFailed;
        public int RetryCount;
        public double RetryDelayMs;
        public int TruncatedResponseCount;
        public BoundedSampleBuffer TargetDurations { get; } = new(RetainedSampleCapacity);
        public BoundedSampleBuffer QueueTimes { get; } = new(RetainedSampleCapacity);
        public ConcurrentDictionary<string, double> StageDurations { get; } = new(StringComparer.Ordinal);
        public ConcurrentDictionary<(string Method, int? Status, RequestFailureKind Kind), int> FailureCounts { get; } = new();

        public void RecordFailure(string? method, int? status, RequestFailureKind kind)
        {
            var safeMethod = RequestFailureCount.NormalizeMethod(method);
            var safeStatus = status is >= 100 and <= 599 ? status : null;
            var safeKind = safeStatus != null ? RequestFailureKind.HttpStatus
                : kind is RequestFailureKind.RequestException or RequestFailureKind.ResponseRejected or RequestFailureKind.RequestCancelled ? kind : RequestFailureKind.Unspecified;
            FailureCounts.AddOrUpdate((safeMethod, safeStatus, safeKind), 1, (_, count) => count + 1);
        }
    }

    internal sealed record BoundedSampleSnapshot(double[] Values, MetricSampleWindow Window);

    internal sealed class BoundedSampleBuffer
    {
        private readonly double[] _samples;
        private readonly object _sync = new();
        private long _writeCount;
        private long _rejectedCount;

        public BoundedSampleBuffer(int capacity)
        {
            _samples = new double[capacity];
        }

        public void Record(double value)
        {
            lock (_sync)
            {
                if (!IsValidDuration(value))
                {
                    _rejectedCount++;
                    return;
                }
                _samples[_writeCount % _samples.Length] = value;
                _writeCount++;
            }
        }

        public BoundedSampleSnapshot Snapshot()
        {
            lock (_sync)
            {
                var count = (int)Math.Min(_writeCount, _samples.Length);
                var snapshot = new double[count];
                Array.Copy(_samples, snapshot, count);
                return new BoundedSampleSnapshot(snapshot, new MetricSampleWindow(_writeCount + _rejectedCount, count, _samples.Length, _rejectedCount));
            }
        }
    }
}
