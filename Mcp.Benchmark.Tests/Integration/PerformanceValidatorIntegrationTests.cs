using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Constants;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Infrastructure.Validators;
using Microsoft.Extensions.Logging;
using Moq;
using FluentAssertions;
using System.Threading;
using Xunit;

namespace Mcp.Benchmark.Tests.Integration;

public class PerformanceValidatorIntegrationTests
{
    private readonly PerformanceValidator _validator;
    private readonly Mock<IMcpHttpClient> _httpClient;

    public PerformanceValidatorIntegrationTests()
    {
        _httpClient = new Mock<IMcpHttpClient>();
        _validator = new PerformanceValidator(new Mock<ILogger<PerformanceValidator>>().Object, _httpClient.Object);
    }

    [Fact]
    public async Task PerformLoadTesting_WithAuthRequired_ShouldSkip()
    {
        var config = new McpServerConfig { Endpoint = "https://test.com/mcp", Transport = "http" };
        _httpClient.Setup(x => x.CallAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>(), It.IsAny<AuthenticationConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse { StatusCode = 401, IsSuccess = false, Headers = new Dictionary<string, string> { { "WWW-Authenticate", "Bearer" } } });

        var result = await _validator.PerformLoadTestingAsync(config, new PerformanceTestingConfig(), CancellationToken.None);

        result.Status.Should().Be(TestStatus.Skipped);
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
}
