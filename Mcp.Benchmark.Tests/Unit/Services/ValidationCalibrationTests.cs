using FluentAssertions;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Services;
using Xunit;

namespace Mcp.Benchmark.Tests.Unit.Services;

public class ValidationCalibrationTests
{
    [Fact]
    public void GetFunctionalProbeConcurrency_WithAuthenticatedProfile_ShouldSerializeFunctionalProbes()
    {
        var serverConfig = new McpServerConfig { Profile = McpServerProfile.Authenticated };

        var concurrency = ValidationCalibration.GetFunctionalProbeConcurrency(serverConfig, 10);

        concurrency.Should().Be(1);
    }

    [Fact]
    public void GetInitialPerformanceProbeConcurrency_WithPublicProfile_ShouldStartAtReducedCalibrationLevel()
    {
        var serverConfig = new McpServerConfig { Profile = McpServerProfile.Public };

        var initialConcurrency = ValidationCalibration.GetInitialPerformanceProbeConcurrency(serverConfig, 100);

        initialConcurrency.Should().Be(20);
    }

    [Fact]
    public void ShouldEscalatePerformanceProbe_WithStablePublicCalibrationRound_ShouldEscalate()
    {
        var serverConfig = new McpServerConfig { Profile = McpServerProfile.Public };

        var shouldEscalate = ValidationCalibration.ShouldEscalatePerformanceProbe(
            serverConfig,
            currentConcurrency: 20,
            targetConcurrency: 100,
            failedRequests: 0,
            rateLimitedRequests: 0,
            transientFailures: 0);

        shouldEscalate.Should().BeTrue();
    }

    [Fact]
    public void ShouldEscalatePerformanceProbe_WithNonPublicProfile_ShouldNotEscalate()
    {
        var serverConfig = new McpServerConfig { Profile = McpServerProfile.Authenticated };

        var shouldEscalate = ValidationCalibration.ShouldEscalatePerformanceProbe(
            serverConfig,
            currentConcurrency: 20,
            targetConcurrency: 100,
            failedRequests: 0,
            rateLimitedRequests: 0,
            transientFailures: 0);

        shouldEscalate.Should().BeFalse();
    }

    [Fact]
    public void GetOperationalReadinessScore_WithPublicPartialFailuresAndStableMetrics_ShouldUseAdvisoryFloor()
    {
        var serverConfig = new McpServerConfig { Profile = McpServerProfile.Public };
        var performanceResult = new PerformanceTestResult
        {
            Status = TestStatus.Failed,
            Score = 55,
            LoadTesting = new LoadTestResult
            {
                TotalRequests = 20,
                SuccessfulRequests = 18,
                FailedRequests = 2,
                AverageResponseTimeMs = 250,
                ConnectionErrors = new List<string>()
            }
        };

        var readiness = ValidationCalibration.GetOperationalReadinessScore(serverConfig, performanceResult);

        readiness.Should().Be(ValidationCalibration.AdvisoryPerformanceScore);
    }

    [Fact]
    public void GetOperationalReadinessScore_WithObservedCalibrationPressureButHealthyFinalRound_ShouldPreserveFinalScore()
    {
        var serverConfig = new McpServerConfig { Profile = McpServerProfile.Public };
        var performanceResult = new PerformanceTestResult
        {
            Status = TestStatus.Passed,
            Score = 88,
            LoadTesting = new LoadTestResult
            {
                TotalRequests = 20,
                SuccessfulRequests = 20,
                FailedRequests = 0,
                AverageResponseTimeMs = 180,
                ProbeRoundsExecuted = 3,
                ObservedRateLimitedRequests = 4,
                ObservedTransientFailures = 1
            }
        };

        var readiness = ValidationCalibration.GetOperationalReadinessScore(serverConfig, performanceResult);

        readiness.Should().Be(88);
    }
}