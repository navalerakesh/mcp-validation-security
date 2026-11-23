using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Constants;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Infrastructure.Validators;
using Mcp.Benchmark.Infrastructure.Scenarios;
using Mcp.Benchmark.Core.Services;
using Microsoft.Extensions.Logging;
using Moq;
using FluentAssertions;
using System.Threading;
using Xunit;

namespace Mcp.Benchmark.Tests.Integration;

public class PerformanceValidatorIntegrationTests
{
    [Theory]
    [InlineData("performance-latency")]
    [InlineData("performance-concurrency")]
    [InlineData("performance-throughput")]
    public async Task PerformanceObligation_ShouldDetectMissingMeasurementDespitePassingCategory(string scenarioId)
    {
        var overlap = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        _httpClient.Setup(client => client.CallAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>(), It.IsAny<AuthenticationConfig>(), It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                var call = Interlocked.Increment(ref calls);
                if (call is 2 or 3)
                {
                    if (call == 3) overlap.TrySetResult();
                    await overlap.Task.WaitAsync(TimeSpan.FromSeconds(5));
                }
                return new JsonRpcResponse { StatusCode = 200, IsSuccess = true, ElapsedMs = 50 };
            });
        var configuration = new McpValidatorConfiguration();
        configuration.Server.Endpoint = "https://example.invalid/mcp";
        configuration.Server.Transport = "http";
        configuration.Validation.Categories.PerformanceTesting.MaxConcurrentConnections = 2;
        var measured = await _validator.PerformLoadTestingAsync(configuration.Server, configuration.Validation.Categories.PerformanceTesting);
        measured.Status.Should().Be(TestStatus.Passed);
        measured.LoadTesting.PeakInFlightRequests.Should().Be(2);
        var result = new ValidationResult { PerformanceTesting = measured };
        var context = new ValidationScenarioContext
        {
            ServerConfig = configuration.Server, ValidationConfiguration = configuration, ValidationResult = result,
            ApplicabilityContext = new ValidationApplicabilityContext
            {
                NegotiatedProtocolVersion = "2025-11-25", SchemaVersion = "2025-11-25", Transport = "http", AccessMode = "public"
            }
        };
        var scenario = new BuiltInObservedSurfaceScenarioPack().GetScenarios().Single(candidate => candidate.Descriptor.Key.Value == scenarioId);
        var complete = await scenario.ExecuteAsync(context, CancellationToken.None);
        complete.Coverage.Should().ContainSingle(coverage => coverage.ObservedOutcome == ValidationOutcome.Succeeded);
        switch (scenarioId)
        {
            case "performance-latency": measured.LoadTesting.TimingSampleCount = null; break;
            case "performance-concurrency": measured.LoadTesting.PeakInFlightRequests = 1; break;
            case "performance-throughput": measured.LoadTesting.ObservationDurationMs = null; break;
        }
        var missing = await scenario.ExecuteAsync(context, CancellationToken.None);
        measured.Status.Should().Be(TestStatus.Passed);
        missing.Coverage.Should().ContainSingle(coverage => coverage.Status == ValidationCoverageStatus.Unavailable);
        result.Evidence.Coverage.AddRange(missing.Coverage);
        AssuranceAssessmentEngine.Evaluate(result, AssuranceProfileCatalog.Comprehensive).Assessments.OperationalReadiness.Status
            .Should().Be(OperationalReadinessStatus.Incomplete);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData(-1.0, false)]
    [InlineData(double.NaN, false)]
    [InlineData(double.PositiveInfinity, false)]
    [InlineData(0.0, true)]
    [InlineData(0.6, true)]
    public async Task LoadTimings_ShouldDistinguishObservedZeroFromMissingAndInvalidSamples(double? elapsed, bool captured)
    {
        _httpClient.Setup(client => client.CallAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>(), It.IsAny<AuthenticationConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse { StatusCode = 200, IsSuccess = true, ElapsedMs = elapsed });
        var measured = await _validator.PerformLoadTestingAsync(
            new McpServerConfig { Endpoint = "https://example.invalid/mcp", Transport = "http" },
            new PerformanceTestingConfig { MaxConcurrentConnections = 1 });

        measured.LoadTesting.TimingSampleCount.Should().Be(captured ? measured.LoadTesting.TotalRequests : 0);
        measured.LoadTesting.ObservationDurationMs.Should().BeGreaterThan(0);
        measured.LoadTesting.SuccessfulRequests.Should().Be(measured.LoadTesting.TotalRequests);
        measured.LoadTesting.PeakInFlightRequests.Should().Be(1);
        measured.Status.Should().Be(captured ? TestStatus.Passed : TestStatus.Inconclusive);
        if (!captured)
        {
            measured.Score.Should().Be(0);
            measured.Throughput.MeetsPerformanceTargets.Should().BeFalse();
        }
    }

    private readonly PerformanceValidator _validator;
    private readonly Mock<IMcpHttpClient> _httpClient;

    public PerformanceValidatorIntegrationTests()
    {
        _httpClient = new Mock<IMcpHttpClient>();
        _validator = new PerformanceValidator(new Mock<ILogger<PerformanceValidator>>().Object, _httpClient.Object);
    }

    [Fact]
    public async Task LoadMeasurement_ShouldNeverInvokeAnAdvertisedToolWithoutWorkloadAdmission()
    {
        _httpClient.Setup(client => client.CallAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>(), It.IsAny<AuthenticationConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse
            {
                StatusCode = 200, IsSuccess = true, ElapsedMs = 50,
                RawJson = """{"jsonrpc":"2.0","id":1,"result":{"tools":[{"name":"delete_all_records","inputSchema":{"type":"object"},"annotations":{"destructiveHint":true}}]}}"""
            });

        var measured = await _validator.PerformLoadTestingAsync(
            new McpServerConfig { Endpoint = "https://example.invalid/mcp", Transport = "http" },
            new PerformanceTestingConfig { MaxConcurrentConnections = 1 });

        measured.LoadTesting.SuccessfulRequests.Should().BeGreaterThan(0);
        _httpClient.Verify(client => client.CallAsync(It.IsAny<string>(), ValidationConstants.Methods.ToolsCall,
            It.IsAny<object>(), It.IsAny<AuthenticationConfig>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(false, OperationalReadinessStatus.Ready)]
    [InlineData(true, OperationalReadinessStatus.Incomplete)]
    public async Task MeasuredPerformance_ShouldNotTurnScopeAdviceIntoRequiredEvidenceDebt(bool missingObjective, OperationalReadinessStatus expected)
    {
        _httpClient.Setup(client => client.CallAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>(), It.IsAny<AuthenticationConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse { StatusCode = 200, IsSuccess = true, RawJson = "{}", ElapsedMs = 50 });
        var performance = await _validator.PerformLoadTestingAsync(new McpServerConfig { Endpoint = "https://test.com/mcp", Transport = "http" },
            new PerformanceTestingConfig { MaxConcurrentConnections = 1 }, CancellationToken.None);
        var result = new ValidationResult { PerformanceTesting = performance };
        result.Evidence.Coverage.Add(ValidationCoverageFactory.FromOutcome("performance", "configured-load-objective",
            missingObjective ? ValidationOutcome.Unavailable : ValidationOutcome.Succeeded));

        var assessment = AssuranceAssessmentEngine.Evaluate(result, AssuranceProfileCatalog.Ci);

        assessment.Assessments.OperationalReadiness.Status.Should().Be(expected);
        assessment.Claims.Should().Contain(claim => claim.Metadata.ContainsKey("ruleId") && claim.Metadata["ruleId"] == "MCP.GUIDELINE.PERFORMANCE.SYNTHETIC_PROBE",
            "scope advice must remain visible");
    }

    [Fact]
    public async Task PerformLoadTesting_WithAuthRequired_ShouldReturnAuthRequired()
    {
        var config = new McpServerConfig { Endpoint = "https://test.com/mcp", Transport = "http" };
        _httpClient.Setup(x => x.CallAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>(), It.IsAny<AuthenticationConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse { StatusCode = 401, IsSuccess = false, Headers = new Dictionary<string, string> { { "WWW-Authenticate", "Bearer" } } });

        var result = await _validator.PerformLoadTestingAsync(config, new PerformanceTestingConfig(), CancellationToken.None);

        result.Status.Should().Be(TestStatus.AuthRequired);
    }

    [Fact]
    public async Task PerformLoadTesting_WithSuccessfulRequests_ShouldCalculateMetrics()
    {
        var config = new McpServerConfig { Endpoint = "https://test.com/mcp", Transport = "http" };
        _httpClient.Setup(x => x.CallAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>(), It.IsAny<AuthenticationConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse { StatusCode = 200, IsSuccess = true, RawJson = "{}", ElapsedMs = 50 });

        var result = await _validator.PerformLoadTestingAsync(config,
            new PerformanceTestingConfig { MaxConcurrentConnections = 2 }, CancellationToken.None);

        result.LoadTesting.Should().NotBeNull();
        result.LoadTesting.SuccessfulRequests.Should().BeGreaterThan(0);
        result.LoadTesting.FailedRequests.Should().Be(0);
        result.LoadTesting.RequestsPerSecond.Should().BeGreaterThan(0);
        ((object?)result.ResourceUsage.WithinAcceptableLimits).Should().BeNull("successful RPCs do not measure server resource limits");
        ((object?)result.ResourceUsage.Cpu.AverageCpuUsagePercent).Should().BeNull();
        ((object?)result.ResourceUsage.Cpu.PeakCpuUsagePercent).Should().BeNull();
        ((object?)result.ResourceUsage.Memory.InitialMemoryBytes).Should().BeNull();
        ((object?)result.ResourceUsage.Memory.PeakMemoryBytes).Should().BeNull();
        ((object?)result.ResourceUsage.Network.TotalBytesSent).Should().BeNull();
    }

    [Fact]
    public async Task PerformLoadTesting_WithSuccessfulPublicRemote_ShouldRampUpAfterCalibrationStage()
    {
        var config = new McpServerConfig
        {
            Endpoint = "https://test.com/mcp",
            Transport = "http",
            Profile = McpServerProfile.Public
        };

        _httpClient.Setup(x => x.CallAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>(), It.IsAny<AuthenticationConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse { StatusCode = 200, IsSuccess = true, RawJson = "{}", ElapsedMs = 50 });

        var result = await _validator.PerformLoadTestingAsync(config,
            new PerformanceTestingConfig { MaxConcurrentConnections = 10 }, CancellationToken.None);

        result.LoadTesting.MaxConcurrentConnections.Should().Be(10);
        result.Findings.Should().Contain(f => f.RuleId == ValidationFindingRuleIds.PerformancePublicRemoteRampUp);
    }

    [Fact]
    public async Task PerformLoadTesting_WithFailedRequests_ShouldReportErrors()
    {
        var config = new McpServerConfig { Endpoint = "https://test.com/mcp", Transport = "http" };
        _httpClient.Setup(x => x.CallAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>(), It.IsAny<AuthenticationConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse { StatusCode = 500, IsSuccess = false, ElapsedMs = 100 });

        var result = await _validator.PerformLoadTestingAsync(config,
            new PerformanceTestingConfig { MaxConcurrentConnections = 1 }, CancellationToken.None);

        result.LoadTesting.FailedRequests.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task PerformLoadTesting_WithStdioTransport_ShouldValidateConfig()
    {
        var config = new McpServerConfig { Endpoint = "npx test-server", Transport = "stdio" };
        _httpClient.Setup(x => x.CallAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>(), It.IsAny<AuthenticationConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse { StatusCode = 200, IsSuccess = true, RawJson = "{}", ElapsedMs = 10 });

        var result = await _validator.PerformLoadTestingAsync(config,
            new PerformanceTestingConfig { MaxConcurrentConnections = 1 }, CancellationToken.None);

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task PerformLoadTesting_WithSubMillisecondElapsedMs_ShouldPreserveFractionalLatency()
    {
        var config = new McpServerConfig { Endpoint = "npx test-server", Transport = "stdio" };
        _httpClient.Setup(x => x.CallAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>(), It.IsAny<AuthenticationConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse { StatusCode = 200, IsSuccess = true, RawJson = "{}", ElapsedMs = 0.6 });

        var result = await _validator.PerformLoadTestingAsync(config,
            new PerformanceTestingConfig { MaxConcurrentConnections = 1 }, CancellationToken.None);

        result.LoadTesting.Should().NotBeNull();
        result.LoadTesting!.AverageResponseTimeMs.Should().BeApproximately(0.6, 0.01);
        result.LoadTesting.P95ResponseTimeMs.Should().BeApproximately(0.6, 0.01);
    }

    [Fact]
    public async Task PerformLoadTesting_WithRateLimitedRemoteProfile_ShouldSkipInsteadOfFailing()
    {
        var config = new McpServerConfig
        {
            Endpoint = "https://test.com/mcp",
            Transport = "http",
            Profile = McpServerProfile.Public
        };

        var callIndex = -1;
        _httpClient
            .Setup(x => x.CallAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>(), It.IsAny<AuthenticationConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                var currentCall = Interlocked.Increment(ref callIndex);
                if (currentCall == 0)
                {
                    return new JsonRpcResponse { StatusCode = 200, IsSuccess = true, RawJson = "{}", ElapsedMs = 20 };
                }

                return new JsonRpcResponse { StatusCode = 429, IsSuccess = false, ElapsedMs = 20 };
            });

        var result = await _validator.PerformLoadTestingAsync(config,
            new PerformanceTestingConfig { MaxConcurrentConnections = 2 }, CancellationToken.None);

        result.Status.Should().Be(TestStatus.Skipped);
        result.LoadTesting.RateLimitedRequests.Should().BeGreaterThan(0);
        result.Message.Should().Contain("transient rate limits");
    }

    [Fact]
    public async Task PerformLoadTesting_WithTransientRateLimits_ShouldRecalibrateBeforeScoring()
    {
        var config = new McpServerConfig
        {
            Endpoint = "https://test.com/mcp",
            Transport = "http",
            Profile = McpServerProfile.Public
        };

        var callIndex = -1;
        _httpClient
            .Setup(x => x.CallAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>(), It.IsAny<AuthenticationConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                var currentCall = Interlocked.Increment(ref callIndex);
                if (currentCall == 0)
                {
                    return new JsonRpcResponse { StatusCode = 200, IsSuccess = true, RawJson = "{}", ElapsedMs = 20 };
                }

                if (currentCall <= 20)
                {
                    return currentCall % 2 == 0
                        ? new JsonRpcResponse { StatusCode = 429, IsSuccess = false, ElapsedMs = 20 }
                        : new JsonRpcResponse { StatusCode = 200, IsSuccess = true, RawJson = "{}", ElapsedMs = 20 };
                }

                return new JsonRpcResponse { StatusCode = 200, IsSuccess = true, RawJson = "{}", ElapsedMs = 20 };
            });

        var result = await _validator.PerformLoadTestingAsync(config,
            new PerformanceTestingConfig { MaxConcurrentConnections = 4 }, CancellationToken.None);

        result.Status.Should().NotBe(TestStatus.Skipped);
        result.LoadTesting.Should().NotBeNull();
        result.LoadTesting!.MaxConcurrentConnections.Should().Be(2);
        result.LoadTesting.ProbeRoundsExecuted.Should().BeGreaterThan(1);
        result.LoadTesting.ObservedRateLimitedRequests.Should().BeGreaterThan(0);
        result.LoadTesting.ObservedTransientFailures.Should().Be(0);
        result.Findings.Should().Contain(f => f.RuleId == "MCP.GUIDELINE.PERFORMANCE.RECALIBRATED_AFTER_TRANSIENT_LIMITS");
        result.Findings.Should().Contain(f => f.RuleId == ValidationFindingRuleIds.PerformancePressureSignalsObserved);
    }

    [Fact]
    public async Task PerformLoadTesting_WithHealthyToolCallLatency_ShouldPassWithoutBottleneck()
    {
        var config = new McpServerConfig { Endpoint = "https://test.com/mcp", Transport = "http" };

        _httpClient
            .Setup(x => x.CallAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>(), It.IsAny<AuthenticationConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, string method, object? _, AuthenticationConfig? _, CancellationToken _) =>
            {
                if (method == ValidationConstants.Methods.ToolsCall)
                {
                    return new JsonRpcResponse
                    {
                        StatusCode = 200,
                        IsSuccess = true,
                        RawJson = "{\"result\":{\"content\":[]}}",
                        ElapsedMs = 80
                    };
                }

                return new JsonRpcResponse
                {
                    StatusCode = 200,
                    IsSuccess = true,
                    RawJson = "{\"result\":{\"tools\":[{\"name\":\"echo\"}]}}",
                    ElapsedMs = 80
                };
            });

        var result = await _validator.PerformLoadTestingAsync(config,
            new PerformanceTestingConfig { MaxConcurrentConnections = 2 }, CancellationToken.None);

        result.Status.Should().Be(TestStatus.Passed);
        result.Score.Should().Be(100);
        result.PerformanceBottlenecks.Should().NotContain(b => b.Contains("tools/call latency", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TestResourceExhaustion_ConfiguredDeadline_ShouldRemainDistinctFromCallerCancellation(bool callerCancellation)
    {
        using var cancellation = new CancellationTokenSource();
        _httpClient.Setup(client => client.CallAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>(),
                It.IsAny<AuthenticationConfig>(), It.IsAny<CancellationToken>()))
            .Returns(async (string endpoint, string method, object? parameters, AuthenticationConfig? authentication, CancellationToken token) =>
            {
                if (callerCancellation) cancellation.Cancel();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return new JsonRpcResponse { StatusCode = 200, IsSuccess = true };
            });

        var result = await _validator.TestResourceExhaustionAsync(new McpServerConfig
            { Endpoint = "https://example.invalid/mcp", Transport = "http", TimeoutMs = 25 }, ["connections"], cancellation.Token)
            .WaitAsync(TimeSpan.FromSeconds(10));

        result.Status.Should().Be(callerCancellation ? TestStatus.Cancelled : TestStatus.Inconclusive);
        result.MeasurementDisposition.Should().Be(callerCancellation
            ? PerformanceMeasurementDisposition.Cancelled : PerformanceMeasurementDisposition.TimedOut);
        result.ResourceUsage.WithinAcceptableLimits.Should().BeNull();
    }

    [Fact]
    public async Task TestResourceExhaustion_LaterBatchTimeout_ShouldNotDowngradeObservedFailure()
    {
        var calls = 0;
        _httpClient.Setup(client => client.CallAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>(),
                It.IsAny<AuthenticationConfig>(), It.IsAny<CancellationToken>()))
            .Returns(() => ++calls <= 50
                ? Task.FromResult(new JsonRpcResponse { StatusCode = 500, IsSuccess = false })
                : Task.FromException<JsonRpcResponse>(new TimeoutException("Synthetic later timeout.")));

        var result = await _validator.TestResourceExhaustionAsync(new McpServerConfig
            { Endpoint = "https://example.invalid/mcp", Transport = "http" }, ["connections", "threads"]);

        result.Status.Should().Be(TestStatus.Failed);
        result.MeasurementDisposition.Should().Be(PerformanceMeasurementDisposition.Unavailable);
        result.PerformanceBottlenecks.Should().ContainSingle(value => value.Contains("50/50 failed", StringComparison.Ordinal));
        calls.Should().Be(100);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TestResourceExhaustion_MixedFaults_ShouldRetainCompletedOutcomes(bool blockedResponses)
    {
        var calls = 0;
        _httpClient.Setup(client => client.CallAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>(),
                It.IsAny<AuthenticationConfig>(), It.IsAny<CancellationToken>()))
            .Returns(() => ++calls == 50
                ? Task.FromException<JsonRpcResponse>(new TimeoutException("Synthetic timeout."))
                : Task.FromResult(new JsonRpcResponse
                {
                    StatusCode = blockedResponses ? -1 : 500, IsSuccess = false,
                    ProbeContext = blockedResponses ? new ProbeContext { ResponseClassification = ProbeResponseClassification.PolicyBlocked } : null
                }));

        var result = await _validator.TestResourceExhaustionAsync(new McpServerConfig
            { Endpoint = "https://example.invalid/mcp", Transport = "http" }, ["connections"]);

        result.Status.Should().Be(blockedResponses ? TestStatus.Inconclusive : TestStatus.Failed);
        result.MeasurementDisposition.Should().Be(blockedResponses ? PerformanceMeasurementDisposition.Blocked : PerformanceMeasurementDisposition.Unavailable);
        if (blockedResponses) result.PerformanceBottlenecks.Should().BeEmpty();
        else result.PerformanceBottlenecks.Should().ContainSingle(value => value.Contains("49/50 failed", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("timeout", TestStatus.Inconclusive, PerformanceMeasurementDisposition.TimedOut)]
    [InlineData("cancelled", TestStatus.Inconclusive, PerformanceMeasurementDisposition.TimedOut)]
    [InlineData("connection", TestStatus.Inconclusive, PerformanceMeasurementDisposition.Unavailable)]
    [InlineData("unsupported", TestStatus.Inconclusive, PerformanceMeasurementDisposition.Unavailable)]
    [InlineData("framework", TestStatus.Error, PerformanceMeasurementDisposition.Unavailable)]
    public async Task TestResourceExhaustion_Exceptions_ShouldPreserveLocalAttribution(
        string failure, TestStatus expectedStatus, PerformanceMeasurementDisposition expectedDisposition)
    {
        Exception exception = failure switch
        {
            "timeout" => new TimeoutException("synthetic-exception-canary"),
            "cancelled" => new TaskCanceledException("synthetic-exception-canary"),
            "connection" => new HttpRequestException("synthetic-exception-canary"),
            "unsupported" => new NotSupportedException("synthetic-exception-canary"),
            _ => new InvalidOperationException("synthetic-exception-canary")
        };
        _httpClient.Setup(client => client.CallAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>(),
            It.IsAny<AuthenticationConfig>(), It.IsAny<CancellationToken>())).ThrowsAsync(exception);

        var result = await _validator.TestResourceExhaustionAsync(new McpServerConfig
            { Endpoint = "https://example.invalid/mcp", Transport = "http" }, ["connections"]);

        result.Status.Should().Be(expectedStatus);
        result.MeasurementDisposition.Should().Be(expectedDisposition);
        result.PerformanceBottlenecks.Should().BeEmpty();
        System.Text.Json.JsonSerializer.Serialize(result).Should().NotContain("synthetic-exception-canary");
    }

    [Fact]
    public async Task TestResourceExhaustion_DispatchFailure_ShouldDrainStartedRequests()
    {
        var pending = new TaskCompletionSource<JsonRpcResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        _httpClient.Setup(client => client.CallAsync(It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<object>(), It.IsAny<AuthenticationConfig>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                if (++calls == 1) return pending.Task;
                throw new Mcp.Benchmark.Core.Exceptions.OperationAdmissionException("Synthetic exhausted admission.");
            });
        var operation = _validator.TestResourceExhaustionAsync(new McpServerConfig
            { Endpoint = "https://example.invalid/mcp", Transport = "http" }, ["connections"]);
        try
        {
            operation.IsCompleted.Should().BeFalse("started requests remain owned after a later dispatch refusal");
        }
        finally { pending.TrySetResult(new JsonRpcResponse { StatusCode = 200, IsSuccess = true }); }

        var result = await operation.WaitAsync(TimeSpan.FromSeconds(10));

        result.MeasurementDisposition.Should().Be(PerformanceMeasurementDisposition.Blocked);
        calls.Should().Be(2);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task TestResourceExhaustion_AdmissionAndAuthentication_ShouldNotBecomeTargetFailure(bool blocked, bool responseDenial)
    {
        var setup = _httpClient.Setup(client => client.CallAsync(It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<object>(), It.IsAny<AuthenticationConfig>(), It.IsAny<CancellationToken>()));
        if (responseDenial) setup.ReturnsAsync(new JsonRpcResponse
        {
            StatusCode = -1, IsSuccess = false,
            ProbeContext = new ProbeContext { ResponseClassification = ProbeResponseClassification.PolicyBlocked }
        });
        else if (blocked) setup.Throws(new Mcp.Benchmark.Core.Exceptions.OperationAdmissionException("Synthetic admission denial."));
        else setup.ReturnsAsync(new JsonRpcResponse { StatusCode = 401, IsSuccess = false });

        var result = await _validator.TestResourceExhaustionAsync(new McpServerConfig
            { Endpoint = "https://example.invalid/mcp", Transport = "http" }, ["connections"]);

        result.Status.Should().Be(blocked ? TestStatus.Inconclusive : TestStatus.AuthRequired);
        result.MeasurementDisposition.Should().Be(blocked ? PerformanceMeasurementDisposition.Blocked : PerformanceMeasurementDisposition.AuthRequired);
        result.ResourceUsage.WithinAcceptableLimits.Should().BeNull();
        result.ResourceUsage.ResourceExhaustionEvents.Should().BeEmpty();
        result.PerformanceBottlenecks.Should().BeEmpty();
    }

    [Theory]
    [InlineData(200, TestStatus.Inconclusive)]
    [InlineData(500, TestStatus.Failed)]
    [InlineData(503, TestStatus.Inconclusive)]
    [InlineData(429, TestStatus.Inconclusive)]
    [InlineData(0, TestStatus.Inconclusive)]
    public async Task TestResourceExhaustion_ShouldSeparateTransportFailuresFromResourceClaims(int statusCode, TestStatus expectedStatus)
    {
        var config = new McpServerConfig { Endpoint = "https://test.com/mcp", Transport = "http" };
        _httpClient
            .Setup(client => client.CallAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<object>(),
                It.IsAny<AuthenticationConfig>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse { StatusCode = statusCode, IsSuccess = statusCode == 200 });

        var result = await _validator.TestResourceExhaustionAsync(config, ["connections"], CancellationToken.None);

        result.Status.Should().Be(expectedStatus);
        result.ResourceUsage.WithinAcceptableLimits.Should().BeNull();
        result.ResourceUsage.ResourceExhaustionEvents.Should().BeEmpty();
        if (expectedStatus == TestStatus.Failed)
            result.PerformanceBottlenecks.Should().ContainSingle(eventText => eventText.Contains("50/50 failed", StringComparison.Ordinal));
        else result.PerformanceBottlenecks.Should().BeEmpty();
        _httpClient.Verify(client => client.CallAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>(),
            It.IsAny<AuthenticationConfig>(), It.IsAny<CancellationToken>()), Times.Exactly(50));
    }
}
