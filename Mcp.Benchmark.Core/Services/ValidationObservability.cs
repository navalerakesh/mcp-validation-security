using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.Core.Services;

public static class ValidationObservability
{
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
        QueueDuration.Record(milliseconds);
        Current.Value?.QueueTimes.Record(milliseconds);
    }

    public static void RecordRequestStarted(string? method)
    {
        RequestCounter.Add(1, new KeyValuePair<string, object?>("mcpval.request.state", "started"));
        var collector = Current.Value;
        if (collector != null) Interlocked.Increment(ref collector.RequestsStarted);
    }

    public static void RecordRequestCompleted(string? method, double milliseconds, bool success)
    {
        RequestDuration.Record(milliseconds, new KeyValuePair<string, object?>("mcpval.request.state", success ? "completed" : "failed"));
        var collector = Current.Value;
        if (collector == null) return;
        collector.TargetDurations.Record(milliseconds);
        Interlocked.Increment(ref collector.RequestsCompleted);
        if (!success) Interlocked.Increment(ref collector.RequestsFailed);
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

    private static double Percentile(IEnumerable<double> values, double percentile)
    {
        var ordered = values.OrderBy(value => value).ToArray();
        if (ordered.Length == 0) return 0;
        var index = (int)Math.Ceiling(percentile * ordered.Length) - 1;
        return ordered[Math.Clamp(index, 0, ordered.Length - 1)];
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
            var elapsedSeconds = totalRunDurationMs / 1000.0;
            var targetDurationSamples = _collector.TargetDurations.Snapshot();
            var queueTimeSamples = _collector.QueueTimes.Snapshot();
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
                RetryCount = _collector.RetryCount,
                RetryDelayMs = Math.Round(_collector.RetryDelayMs, 3),
                TruncatedResponseCount = _collector.TruncatedResponseCount,
                TargetLatencyP50Ms = Math.Round(Percentile(targetDurationSamples, 0.50), 3),
                TargetLatencyP95Ms = Math.Round(Percentile(targetDurationSamples, 0.95), 3),
                TargetLatencyP99Ms = Math.Round(Percentile(targetDurationSamples, 0.99), 3),
                QueueTimeP95Ms = Math.Round(Percentile(queueTimeSamples, 0.95), 3),
                TargetLatencySampleCount = targetDurationSamples.Length,
                QueueTimeSampleCount = queueTimeSamples.Length,
                ThroughputRequestsPerSecond = elapsedSeconds > 0 ? Math.Round(_collector.RequestsCompleted / elapsedSeconds, 3) : 0,
                ErrorRate = _collector.RequestsCompleted > 0 ? Math.Round(Math.Min(1, (double)_collector.RequestsFailed / _collector.RequestsCompleted), 4) : 0,
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
        public BoundedSampleBuffer TargetDurations { get; } = new(4096);
        public BoundedSampleBuffer QueueTimes { get; } = new(4096);
        public ConcurrentDictionary<string, double> StageDurations { get; } = new(StringComparer.Ordinal);
    }

    internal sealed class BoundedSampleBuffer
    {
        private readonly double[] _samples;
        private readonly object _sync = new();
        private long _writeCount;

        public BoundedSampleBuffer(int capacity)
        {
            _samples = new double[capacity];
        }

        public void Record(double value)
        {
            lock (_sync)
            {
                _samples[_writeCount % _samples.Length] = value;
                _writeCount++;
            }
        }

        public double[] Snapshot()
        {
            lock (_sync)
            {
                var count = (int)Math.Min(_writeCount, _samples.Length);
                var snapshot = new double[count];
                Array.Copy(_samples, snapshot, count);
                return snapshot;
            }
        }
    }
}
