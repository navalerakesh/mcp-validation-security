using Mcp.Benchmark.Infrastructure.Utilities;
using FluentAssertions;
using Xunit;

namespace Mcp.Benchmark.Tests.Unit.Utilities;

public class PerformanceMetricsCalculatorTests
{
    [Fact]
    public void GetPercentile_WithEmptyList_ShouldReturnZero()
    {
        PerformanceMetricsCalculator.GetPercentile(Array.Empty<long>(), 0.95).Should().Be(0);
    }

    [Fact]
    public void GetPercentile_WithSingleValue_ShouldReturnThatValue()
    {
        PerformanceMetricsCalculator.GetPercentile(new long[] { 100 }, 0.95).Should().Be(100);
    }

    [Fact]
    public void GetPercentile_P95_ShouldExcludeTop5Percent()
    {
        // 20 values: 1-20. P95 = value at index 19 (95th percentile) = 19
        var times = Enumerable.Range(1, 20).Select(x => (long)x).ToArray();
        var p95 = PerformanceMetricsCalculator.GetPercentile(times, 0.95);
        p95.Should().Be(19);
    }

    [Fact]
    public void GetPercentile_P99_ShouldBeHigherThanP95()
    {
        var times = Enumerable.Range(1, 100).Select(x => (long)x).ToArray();
        var p95 = PerformanceMetricsCalculator.GetPercentile(times, 0.95);
        var p99 = PerformanceMetricsCalculator.GetPercentile(times, 0.99);
        p99.Should().BeGreaterThanOrEqualTo(p95);
    }

    [Fact]
    public void CalculateThroughput_WithZeroDuration_ShouldReturnZero()
    {
        PerformanceMetricsCalculator.CalculateThroughput(100, TimeSpan.Zero).Should().Be(0);
    }

    [Fact]
    public void CalculateThroughput_ShouldCalculateCorrectly()
    {
        var rps = PerformanceMetricsCalculator.CalculateThroughput(100, TimeSpan.FromSeconds(10));
        rps.Should().Be(10);
    }

    [Fact]
    public void IdentifyBottlenecks_WithGoodMetrics_ShouldReturnEmpty()
    {
        var findings = PerformanceMetricsCalculator.IdentifyBottlenecks(100, 0, 200);
        findings.Should().BeEmpty();
    }

    [Fact]
    public void IdentifyBottlenecks_WithHighLatency_ShouldFlag()
    {
        var findings = PerformanceMetricsCalculator.IdentifyBottlenecks(3000, 0, 200);
        findings.Should().Contain(f => f.Contains("2000ms"));
    }

    [Fact]
    public void IdentifyBottlenecks_WithHighErrorRate_ShouldFlag()
    {
        var findings = PerformanceMetricsCalculator.IdentifyBottlenecks(100, 10, 200);
        findings.Should().Contain(f => f.Contains("Error rate"));
    }

    [Fact]
    public void IdentifyBottlenecks_WithHighP99_ShouldFlag()
    {
        var findings = PerformanceMetricsCalculator.IdentifyBottlenecks(100, 0, 6000);
        findings.Should().Contain(f => f.Contains("P99") || f.Contains("tail"));
    }

    [Fact]
    public void IdentifyBottlenecks_WithHealthyToolCallLatency_ShouldNotReport()
    {
        var findings = PerformanceMetricsCalculator.IdentifyBottlenecks(100, 0, 200, 250, "echo");
        findings.Should().BeEmpty();
    }

    [Fact]
    public void IdentifyBottlenecks_WithSlowToolCall_ShouldFlagSLO()
    {
        var findings = PerformanceMetricsCalculator.IdentifyBottlenecks(100, 0, 200, 6000, "slow_tool");
        findings.Should().Contain(f => f.Contains("5000ms SLO") && f.Contains("slow_tool"));
    }
}
