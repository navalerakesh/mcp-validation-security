using Mcp.Benchmark.Core.Services;
using Mcp.Benchmark.Core.Models;
using System.Diagnostics;
using System.Collections.Concurrent;

namespace Mcp.Benchmark.Tests.Unit.Services;

public sealed class ValidationObservabilityTests
{
    [Fact]
    public void Complete_ShouldAggregateOperationalMetricsAndPercentiles()
    {
        using var run = ValidationObservability.BeginRun("run-1", 10);
        ValidationObservability.RecordQueue(2);
        ValidationObservability.RecordQueue(8);
        ValidationObservability.RecordRequestStarted("tools/list");
        ValidationObservability.RecordRequestCompleted("tools/list", 10, success: true);
        ValidationObservability.RecordRequestStarted("tools/list");
        ValidationObservability.RecordRequestCompleted("tools/list", 40, success: false);
        ValidationObservability.RecordRetry(TimeSpan.FromMilliseconds(25));
        ValidationObservability.RecordResponseRejected(responseWasInitiallySuccessful: false);
        using (ValidationObservability.MeasureStage("rules.test"))
        {
            _ = Enumerable.Range(0, 100).Sum();
        }

        var metrics = run.Complete(1000);

        metrics.RunCorrelationId.Should().Be("run-1");
        metrics.TotalRunDurationMs.Should().Be(1000);
        metrics.ValidatorOverheadMs.Should().BeGreaterThanOrEqualTo(0);
        metrics.RequestBudgetLimit.Should().Be(10);
        metrics.RequestsStarted.Should().Be(2);
        metrics.RequestsCompleted.Should().Be(2);
        metrics.RequestsFailed.Should().Be(1);
        metrics.RetryCount.Should().Be(1);
        metrics.RetryDelayMs.Should().Be(25);
        metrics.TruncatedResponseCount.Should().Be(1);
        metrics.TargetLatencyP50Ms.Should().Be(10);
        metrics.TargetLatencyP95Ms.Should().Be(40);
        metrics.TargetLatencyP99Ms.Should().Be(40);
        metrics.QueueTimeP95Ms.Should().Be(8);
        metrics.ThroughputRequestsPerSecond.Should().Be(2);
        metrics.ErrorRate.Should().Be(0.5);
        metrics.StageDurationMs.Should().ContainKey("rules.test");
    }

    [Fact]
    public void NestedRuns_ShouldRestoreParentCollector()
    {
        using var outer = ValidationObservability.BeginRun("outer", 2);
        ValidationObservability.RecordRequestStarted("outer");
        ValidationObservability.RecordRequestCompleted("outer", 1, true);
        using (var inner = ValidationObservability.BeginRun("inner", 1))
        {
            ValidationObservability.RecordRequestStarted("inner");
            ValidationObservability.RecordRequestCompleted("inner", 2, true);
            inner.Complete(10).RequestsCompleted.Should().Be(1);
        }
        ValidationObservability.RecordRequestStarted("outer");
        ValidationObservability.RecordRequestCompleted("outer", 3, true);

        outer.Complete(10).RequestsCompleted.Should().Be(2);
    }

    [Fact]
    public void Instrumentation_ShouldExposeStableOpenTelemetryIdentity()
    {
        ValidationObservability.ActivitySource.Name.Should().Be(ValidationObservability.InstrumentationName);
        ValidationObservability.ActivitySource.Version.Should().Be(ValidationObservability.InstrumentationVersion);
        ValidationObservability.Meter.Name.Should().Be(ValidationObservability.InstrumentationName);
        ValidationObservability.Meter.Version.Should().Be(ValidationObservability.InstrumentationVersion);
    }

    [Fact]
    public void SoakWithHighCardinalityMethodNames_ShouldAggregateWithoutPersistingNames()
    {
        using var run = ValidationObservability.BeginRun("soak", 10_000);
        for (var index = 0; index < 10_000; index++)
        {
            ValidationObservability.RecordRequestStarted($"untrusted-method-{index}");
            ValidationObservability.RecordRequestCompleted($"untrusted-method-{index}", index % 10, success: true);
        }

        var metrics = run.Complete(1_000);

        metrics.RequestsCompleted.Should().Be(10_000);
        metrics.TargetLatencySampleCount.Should().Be(4096);
        metrics.StageDurationMs.Keys.Should().NotContain(key => key.Contains("untrusted-method", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ParallelRuns_ShouldNotShareCollectors()
    {
        async Task<ValidationOperationalMetrics> CollectAsync(string id, int requestCount)
        {
            using var run = ValidationObservability.BeginRun(id, requestCount);
            for (var index = 0; index < requestCount; index++)
            {
                ValidationObservability.RecordRequestStarted("request");
                ValidationObservability.RecordRequestCompleted("request", index, true);
                await Task.Yield();
            }
            return run.Complete(100);
        }

        var results = await Task.WhenAll(
            Task.Run(() => CollectAsync("parallel-a", 3)),
            Task.Run(() => CollectAsync("parallel-b", 7)));

        results.Single(result => result.RunCorrelationId == "parallel-a").RequestsCompleted.Should().Be(3);
        results.Single(result => result.RunCorrelationId == "parallel-b").RequestsCompleted.Should().Be(7);
    }

    [Fact]
    public void ConcurrentWrappedSamples_ShouldRemainBoundedAndPreferRecentValues()
    {
        using var run = ValidationObservability.BeginRun("concurrent-wrap", 20_000);

        Parallel.For(0, 20_000, index =>
        {
            ValidationObservability.RecordRequestStarted("request");
            ValidationObservability.RecordRequestCompleted("request", index, true);
        });
        for (var index = 20_000; index < 24_096; index++)
        {
            ValidationObservability.RecordRequestStarted("request");
            ValidationObservability.RecordRequestCompleted("request", index, true);
        }

        var metrics = run.Complete(1_000);

        metrics.TargetLatencySampleCount.Should().Be(4096);
        metrics.TargetLatencyP99Ms.Should().BeGreaterThan(23_000);
    }

    [Fact]
    public void Activities_ShouldContainOnlyStableRedactedTags()
    {
        var activities = new ConcurrentBag<Activity>();
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == ValidationObservability.InstrumentationName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activities.Add
        };
        ActivitySource.AddActivityListener(listener);

        using (var run = ValidationObservability.BeginRun("correlation-only", 5))
        {
            using (ValidationObservability.MeasureStage("rules.protocol")) { }
            run.Complete(1);
        }
        listener.Dispose();
        var snapshot = activities.ToArray();

        snapshot.SelectMany(activity => activity.TagObjects).Select(tag => tag.Key).Should().OnlyContain(key =>
            key == "mcpval.run.id" || key == "mcpval.request.budget" || key == "mcpval.stage.name");
        snapshot.SelectMany(activity => activity.TagObjects).Select(tag => tag.Value?.ToString()).Should().NotContain(value =>
            value != null && (value.Contains("http", StringComparison.OrdinalIgnoreCase) || value.Contains("token", StringComparison.OrdinalIgnoreCase)));
    }
}
