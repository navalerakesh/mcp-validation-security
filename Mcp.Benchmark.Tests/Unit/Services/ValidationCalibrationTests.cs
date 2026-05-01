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
    public void ApplyPerformanceOutcomeCalibration_WithPublicPartialFailures_ShouldRecordStructuredOverrideAudit()
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

        ValidationCalibration.ApplyPerformanceOutcomeCalibration(serverConfig, performanceResult);
        ValidationCalibration.ApplyPerformanceOutcomeCalibration(serverConfig, performanceResult);

        performanceResult.Status.Should().Be(TestStatus.Skipped);
        performanceResult.Score.Should().Be(ValidationCalibration.AdvisoryPerformanceScore);
        performanceResult.CalibrationOverrides.Should().ContainSingle();

        var overrideRecord = performanceResult.CalibrationOverrides.Single();
        overrideRecord.RuleId.Should().Be(ValidationFindingRuleIds.PerformancePublicRemoteAdvisory);
        overrideRecord.AffectedTests.Should().ContainSingle().Which.Should().Be("performance/load-testing");
        overrideRecord.BeforeStatus.Should().Be(TestStatus.Failed);
        overrideRecord.AfterStatus.Should().Be(TestStatus.Skipped);
        overrideRecord.BeforeScore.Should().Be(55);
        overrideRecord.AfterScore.Should().Be(ValidationCalibration.AdvisoryPerformanceScore);
        overrideRecord.BeforeSeverity.Should().Be(ValidationFindingSeverity.Medium);
        overrideRecord.AfterSeverity.Should().Be(ValidationFindingSeverity.Info);
        overrideRecord.ChangedComponentStatus.Should().BeTrue();
        overrideRecord.ChangedDeterministicVerdict.Should().BeFalse();
        overrideRecord.Inputs.Should().Contain("serverProfile", "Public");
        overrideRecord.Inputs.Should().Contain("totalRequests", "20");
        overrideRecord.Inputs.Should().Contain("successfulRequests", "18");
        overrideRecord.Inputs.Should().Contain("failedRequests", "2");
        overrideRecord.Inputs.Should().Contain("successRatio", "0.900");
        overrideRecord.Inputs.Should().Contain("averageResponseTimeMs", "250.0");
        overrideRecord.Inputs.Should().Contain("advisoryScoreFloor", "70.0");

        var finding = performanceResult.Findings.Should()
            .ContainSingle(finding => finding.RuleId == ValidationFindingRuleIds.PerformancePublicRemoteAdvisory)
            .Subject;
        finding.Metadata.Should().Contain("calibrationOverride", "true");
        finding.Metadata.Should().Contain("beforeSeverity", "Medium");
        finding.Metadata.Should().Contain("afterSeverity", "Info");
        finding.Metadata.Should().Contain("changedDeterministicVerdict", "false");
    }

    [Fact]
    public void ApplyPerformanceOutcomeCalibration_WithPublicTimeoutAndNoMeasurements_ShouldRecordOriginalReason()
    {
        var serverConfig = new McpServerConfig { Profile = McpServerProfile.Public };
        var performanceResult = new PerformanceTestResult
        {
            Status = TestStatus.Failed,
            Score = 0,
            CriticalErrors = ["The performance probe timed out before samples were captured."],
            LoadTesting = new LoadTestResult
            {
                TotalRequests = 5,
                FailedRequests = 5
            }
        };

        ValidationCalibration.ApplyPerformanceOutcomeCalibration(serverConfig, performanceResult);

        var overrideRecord = performanceResult.CalibrationOverrides.Should().ContainSingle().Subject;
        overrideRecord.RuleId.Should().Be(ValidationFindingRuleIds.PerformancePublicRemoteTimeoutAdvisory);
        overrideRecord.BeforeSeverity.Should().Be(ValidationFindingSeverity.Critical);
        overrideRecord.AfterSeverity.Should().Be(ValidationFindingSeverity.Info);
        overrideRecord.Inputs.Should().Contain("metricsCaptured", "False");
        overrideRecord.Inputs.Should().Contain("unavailableReason", "The performance probe timed out before samples were captured.");
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