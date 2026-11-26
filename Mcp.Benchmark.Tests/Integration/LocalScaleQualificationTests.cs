using System.Diagnostics;
using System.Text.Json;
using Mcp.Benchmark.Core.Constants;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Services;
using Mcp.Benchmark.Core.Utilities;
using Mcp.Benchmark.Infrastructure.Http;
using Mcp.Benchmark.Infrastructure.Services.Reporting;
using Xunit.Abstractions;

namespace Mcp.Benchmark.Tests.Integration;

public sealed class LocalScaleQualificationTests(ITestOutputHelper output)
{
    [Fact]
    public async Task BoundedOwnedSessionSoak_ShouldLeaveNoOwnedProcessesOrBudgetState()
    {
        var fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Servers", "mcp-fixture-client-input.cjs");
        var stopwatch = Stopwatch.StartNew();
        var samples = new List<object>();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        while (stopwatch.Elapsed < TimeSpan.FromSeconds(30) && samples.Count < 64)
        {
            await using var adapter = new StdioMcpClientAdapter(Mock.Of<ILogger<StdioMcpClientAdapter>>());
            adapter.ConfigureExecutionPolicy(new() { Mode = ExecutionMode.Standard, MaxRequests = 2 });
            adapter.SetProtocolVersion("2026-07-28");
            await adapter.StartProcessAsync($"node \"{fixture}\"", null, deadline.Token);
            (await adapter.CallAsync("owned", "tools/list", null, deadline.Token)).IsSuccess.Should().BeTrue();
            var stats = await adapter.CallAsync("owned", "fixture/stats", null, deadline.Token);
            using var document = JsonDocument.Parse(stats.RawJson!);
            using var owned = Process.GetProcessById(document.RootElement.GetProperty("result").GetProperty("pid").GetInt32());
            (await adapter.CallAsync("owned", "tools/list", null, deadline.Token)).IsSuccess.Should().BeFalse();
            await adapter.DisposeAsync();
            owned.HasExited.Should().BeTrue();
            using var current = Process.GetCurrentProcess();
            samples.Add(new { elapsedMs = stopwatch.Elapsed.TotalMilliseconds, heapBytes = GC.GetTotalMemory(false),
                workingSetBytes = current.WorkingSet64, handles = current.HandleCount });
        }
        samples.Should().NotBeEmpty();
        output.WriteLine(JsonSerializer.Serialize(new
        {
            workload = "30-second-or-64-session-owned-soak", repetitions = samples.Count, samples,
            ownedProcessCleanupVerified = true, limitations = "Sampled shared test-process resources; no universal leak-free or long-duration reliability claim."
        }));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    public async Task DeclaredConcurrentStdioWorkload_ShouldKeepRunBudgetsAndCleanupIndependent(int concurrency)
    {
        const int rounds = 10;
        var script = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Servers", "mcp-fixture-compliant.cjs");
        var clock = Stopwatch.StartNew();
        var allocations = GC.GetTotalAllocatedBytes();
        var results = await Task.WhenAll(Enumerable.Range(0, concurrency).Select(async index =>
        {
            await using var adapter = new StdioMcpClientAdapter(Mock.Of<ILogger<StdioMcpClientAdapter>>());
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using var telemetry = ValidationObservability.BeginRun($"scale-{index}", rounds);
            adapter.ConfigureExecutionPolicy(new() { Mode = ExecutionMode.Standard, MaxRequests = rounds });
            await adapter.StartProcessAsync($"node \"{script}\"", null, deadline.Token);
            for (var round = 0; round < rounds; round++)
            {
                var response = await adapter.CallAsync("owned", round % 2 == 0 ? "tools/list" : "rpc.invalid.method", null, deadline.Token);
                response.IsSuccess.Should().Be(round % 2 == 0);
                response.StatusCode.Should().Be(round % 2 == 0 ? 200 : 400);
            }
            var overBudget = await adapter.CallAsync("owned", "tools/list", null, deadline.Token);
            overBudget.ProbeContext!.ResponseClassification.Should().Be(ProbeResponseClassification.PolicyBlocked);
            var metrics = telemetry.Complete(clock.Elapsed.TotalMilliseconds);
            metrics.RequestsStarted.Should().Be(rounds);
            metrics.RequestsCompleted.Should().Be(rounds);
            metrics.RequestsFailed.Should().Be(0);
            return metrics;
        }));
        results.Select(result => result.RunCorrelationId).Distinct().Should().HaveCount(concurrency);
        output.WriteLine(JsonSerializer.Serialize(new
        {
            workload = "owned-stdio-mixed-protocol-outcomes", concurrency, rounds,
            elapsedMs = clock.Elapsed.TotalMilliseconds, processAllocatedBytes = GC.GetTotalAllocatedBytes() - allocations,
            requests = results.Sum(result => result.RequestsStarted),
            limitation = "Process allocation includes concurrent test activity; not a target-capacity or leak-free soak certification."
        }));
    }

    [Theory]
    [InlineData(100)]
    [InlineData(1000)]
    [InlineData(10000)]
    public void DeclaredEvidenceScale_ShouldPreserveEveryObligationAndDeterministicOrder(int count)
    {
        var first = Create(count, reverse: false);
        var second = Create(count, reverse: true);
        var left = AssuranceAssessmentEngine.Evaluate(first, AssuranceProfileCatalog.Inventory);
        var right = AssuranceAssessmentEngine.Evaluate(second, AssuranceProfileCatalog.Inventory);
        left.Claims.Should().HaveCount(count);
        right.Claims.Should().HaveCount(count);
        CanonicalJsonDigest.ComputeObject(left).Should().Be(CanonicalJsonDigest.ComputeObject(right));
        var renderer = new ValidationReportRenderer();
        first.Assurance = left;
        second.Assurance = right;
        var xml = renderer.GenerateXmlReport(first, verbose: true);
        var sarif = renderer.GenerateSarifReport(first);
        xml.Should().Contain($"obligation-{count - 1}");
        sarif.Should().Contain("obligation-0");
        output.WriteLine(JsonSerializer.Serialize(new { workload = "evidence-reduction-and-offline-render", count, xmlCharacters = xml.Length, sarifCharacters = sarif.Length }));
    }

    private static ValidationResult Create(int count, bool reverse)
    {
        var result = new ValidationResult
        {
            ValidationId = "scale-fixture", StartTime = new DateTime(2026,11,24,0,0,0,DateTimeKind.Utc),
            EndTime = new DateTime(2026,11,24,0,0,1,DateTimeKind.Utc), OverallStatus = ValidationStatus.Passed
        };
        var indices = Enumerable.Range(0,count);
        foreach (var index in reverse ? indices.Reverse() : indices)
            result.Evidence.Coverage.Add(ValidationCoverageFactory.FromOutcome("tool-surface", $"obligation-{index}",
                index % 3 == 0 ? ValidationOutcome.Unavailable : index % 3 == 1 ? ValidationOutcome.Failed : ValidationOutcome.Succeeded));
        return result;
    }
}
