using Mcp.Benchmark.Core.Abstractions;
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
        result.Findings.Should().Contain(f => f.RuleId == "MCP.GUIDELINE.PERFORMANCE.RECALIBRATED_AFTER_TRANSIENT_LIMITS");
    }
}
