using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Infrastructure.Strategies.Scoring;
using FluentAssertions;
using Xunit;

namespace Mcp.Benchmark.Tests.Unit.Scoring;

/// <summary>
/// Tests for component-level scoring strategies (Tool, Resource, Prompt, Performance).
/// </summary>
public class ComponentScoringTests
{
    // ─── ToolScoringStrategy ─────────────────────────────────────

    [Fact]
    public void ToolScoring_WithAllPassed_ShouldBe100()
    {
        var strategy = new ToolScoringStrategy();
        var result = new ToolTestResult { ToolsTestPassed = 5, ToolsTestFailed = 0 };

        strategy.CalculateScore(result).Should().Be(100);
    }

    [Fact]
    public void ToolScoring_WithMixed_ShouldCalculateRatio()
    {
        var strategy = new ToolScoringStrategy();
        var result = new ToolTestResult { ToolsTestPassed = 3, ToolsTestFailed = 2 };

        strategy.CalculateScore(result).Should().Be(60);
    }

    [Fact]
    public void ToolScoring_WithNone_ShouldReturn100IfPassed()
    {
        var strategy = new ToolScoringStrategy();
        var result = new ToolTestResult { ToolsTestPassed = 0, ToolsTestFailed = 0, Status = TestStatus.Passed };

        strategy.CalculateScore(result).Should().Be(100);
    }

    [Fact]
    public void ToolScoring_WithNone_ShouldReturn0IfFailed()
    {
        var strategy = new ToolScoringStrategy();
        var result = new ToolTestResult { ToolsTestPassed = 0, ToolsTestFailed = 0, Status = TestStatus.Failed };

        strategy.CalculateScore(result).Should().Be(0);
    }

    // ─── ResourceScoringStrategy ─────────────────────────────────

    [Fact]
    public void ResourceScoring_WithAllPassed_ShouldBe100()
    {
        var strategy = new ResourceScoringStrategy();
        var result = new ResourceTestResult
        {
            ResourceResults = new List<IndividualResourceResult>
            {
                new() { Status = TestStatus.Passed },
                new() { Status = TestStatus.Passed }
            }
        };

        strategy.CalculateScore(result).Should().Be(100);
    }

    [Fact]
    public void ResourceScoring_WithMixed_ShouldCalculateRatio()
    {
        var strategy = new ResourceScoringStrategy();
        var result = new ResourceTestResult
        {
            ResourceResults = new List<IndividualResourceResult>
            {
                new() { Status = TestStatus.Passed },
                new() { Status = TestStatus.Failed }
            }
        };

        strategy.CalculateScore(result).Should().Be(50);
    }

    [Fact]
    public void ResourceScoring_WithEmpty_ShouldReturn100IfPassed()
    {
        var strategy = new ResourceScoringStrategy();
        var result = new ResourceTestResult { Status = TestStatus.Passed };

        strategy.CalculateScore(result).Should().Be(100);
    }

    // ─── PromptScoringStrategy ───────────────────────────────────

    [Fact]
    public void PromptScoring_WithAllPassed_ShouldBe100()
    {
        var strategy = new PromptScoringStrategy();
        var result = new PromptTestResult
        {
            PromptResults = new List<IndividualPromptResult>
            {
                new() { Status = TestStatus.Passed },
                new() { Status = TestStatus.Passed }
            }
        };

        strategy.CalculateScore(result).Should().Be(100);
    }

    [Fact]
    public void PromptScoring_WithEmpty_ShouldReturn0IfFailed()
    {
        var strategy = new PromptScoringStrategy();
        var result = new PromptTestResult { Status = TestStatus.Failed };

        strategy.CalculateScore(result).Should().Be(0);
    }

    // ─── PerformanceScoringStrategy ──────────────────────────────

    [Fact]
    public void PerformanceScoring_WithFastResponses_ShouldBe100()
    {
        var strategy = new PerformanceScoringStrategy();
        var result = new PerformanceTestResult
        {
            LoadTesting = new LoadTestResult { AverageResponseTimeMs = 50, FailedRequests = 0 }
        };

        strategy.CalculateScore(result).Should().Be(100);
    }

    [Fact]
    public void PerformanceScoring_WithSlowResponses_ShouldDeduct()
    {
        var strategy = new PerformanceScoringStrategy();
        var result = new PerformanceTestResult
        {
            LoadTesting = new LoadTestResult { AverageResponseTimeMs = 500, FailedRequests = 0 }
        };

        // (500 - 200) / 20 = 15 points deducted → 85
        strategy.CalculateScore(result).Should().Be(85);
    }

    [Fact]
    public void PerformanceScoring_WithFailedRequests_ShouldDeductPerFailure()
    {
        var strategy = new PerformanceScoringStrategy();
        var result = new PerformanceTestResult
        {
            LoadTesting = new LoadTestResult { AverageResponseTimeMs = 100, FailedRequests = 3 }
        };

        // 3 * 5 = 15 points deducted → 85
        strategy.CalculateScore(result).Should().Be(85);
    }

    [Fact]
    public void PerformanceScoring_WithOnlyRateLimitedFailures_ShouldNotPenalizeServerReadiness()
    {
        var strategy = new PerformanceScoringStrategy();
        var result = new PerformanceTestResult
        {
            LoadTesting = new LoadTestResult
            {
                AverageResponseTimeMs = 100,
                FailedRequests = 3,
                RateLimitedRequests = 3
            }
        };

        strategy.CalculateScore(result).Should().Be(100);
    }

    [Fact]
    public void PerformanceScoring_WithNullLoadTesting_ShouldHandleGracefully()
    {
        var strategy = new PerformanceScoringStrategy();
        var result = new PerformanceTestResult();

        // Should not crash and return a valid score
        var score = strategy.CalculateScore(result);
        score.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public void PerformanceScoring_ShouldNeverGoNegative()
    {
        var strategy = new PerformanceScoringStrategy();
        var result = new PerformanceTestResult
        {
            LoadTesting = new LoadTestResult { AverageResponseTimeMs = 10000, FailedRequests = 100 }
        };

        strategy.CalculateScore(result).Should().Be(0);
    }
}
