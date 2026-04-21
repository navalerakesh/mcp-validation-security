using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Infrastructure.Health;
using Mcp.Benchmark.Infrastructure.Services.Telemetry;
using Microsoft.Extensions.Logging;
using Moq;
using FluentAssertions;
using Xunit;
using ModelContextProtocol.Protocol;
using McpServerCapabilities = ModelContextProtocol.Protocol.ServerCapabilities;

namespace Mcp.Benchmark.Tests.Unit.Services;

/// <summary>
/// Comprehensive tests for HealthCheckService covering HTTP and STDIO paths.
/// </summary>
public class HealthCheckServiceComprehensiveTests
{
    private readonly Mock<IMcpHttpClient> _httpClient;
    private readonly HealthCheckService _service;

    public HealthCheckServiceComprehensiveTests()
    {
        _httpClient = new Mock<IMcpHttpClient>();
        _service = new HealthCheckService(
            _httpClient.Object,
            new Mock<ILogger<HealthCheckService>>().Object,
            new Mock<ITelemetryService>().Object);
    }

    [Fact]
    public async Task HealthCheck_WithHttpSuccess_ShouldBeHealthy()
    {
        var config = new McpServerConfig { Endpoint = "https://test.com/mcp", Transport = "http" };
        _httpClient.Setup(x => x.ValidateInitializeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransportResult<InitializeResult>
            {
                IsSuccessful = true,
                Transport = new TransportMetadata { Duration = TimeSpan.FromMilliseconds(100) },
                Payload = new InitializeResult 
                { 
                    ProtocolVersion = "2025-03-26", 
                    ServerInfo = new Implementation { Name = "Test", Version = "1.0" },
                    Capabilities = new McpServerCapabilities()
                }
            });

        var result = await _service.PerformHealthCheckAsync(config);

        result.IsHealthy.Should().BeTrue();
        result.Disposition.Should().Be(HealthCheckDisposition.Healthy);
        result.ResponseTimeMs.Should().BeGreaterThan(0);
        result.ServerVersion.Should().Be("1.0");
    }

    [Fact]
    public async Task HealthCheck_WithHttpFailure_ShouldBeUnhealthy()
    {
        var config = new McpServerConfig { Endpoint = "https://test.com/mcp", Transport = "http" };
        _httpClient.Setup(x => x.ValidateInitializeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransportResult<InitializeResult>
            {
                IsSuccessful = false,
                Error = "Connection refused",
                Transport = new TransportMetadata { Duration = TimeSpan.FromMilliseconds(50) }
            });

        var result = await _service.PerformHealthCheckAsync(config);

        result.IsHealthy.Should().BeFalse();
        result.Disposition.Should().Be(HealthCheckDisposition.Unhealthy);
        result.AllowsDeferredValidation.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Connection refused");
    }

    [Fact]
    public async Task HealthCheck_WithProtectedEndpoint_ShouldBeDeferredProtected()
    {
        var config = new McpServerConfig { Endpoint = "https://test.com/mcp", Transport = "http" };
        _httpClient.Setup(x => x.ValidateInitializeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransportResult<InitializeResult>
            {
                IsSuccessful = false,
                Error = "401 Unauthorized",
                Transport = new TransportMetadata
                {
                    Duration = TimeSpan.FromMilliseconds(40),
                    StatusCode = 401
                }
            });

        var result = await _service.PerformHealthCheckAsync(config);

        result.IsHealthy.Should().BeFalse();
        result.Disposition.Should().Be(HealthCheckDisposition.Protected);
        result.AllowsDeferredValidation.Should().BeTrue();
        result.ErrorMessage.Should().Contain("401");
    }

    [Fact]
    public async Task HealthCheck_WithRateLimit_ShouldBeDeferredTransientFailure()
    {
        var config = new McpServerConfig { Endpoint = "https://test.com/mcp", Transport = "http" };
        _httpClient.Setup(x => x.ValidateInitializeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransportResult<InitializeResult>
            {
                IsSuccessful = false,
                Error = "HTTP 429 Too Many Requests",
                Transport = new TransportMetadata
                {
                    Duration = TimeSpan.FromMilliseconds(60),
                    StatusCode = 429
                }
            });

        var result = await _service.PerformHealthCheckAsync(config);

        result.IsHealthy.Should().BeFalse();
        result.Disposition.Should().Be(HealthCheckDisposition.TransientFailure);
        result.AllowsDeferredValidation.Should().BeTrue();
        result.ErrorMessage.Should().Contain("429");
    }

    [Fact]
    public async Task HealthCheck_WithNoEndpoint_ShouldBeUnhealthy()
    {
        var config = new McpServerConfig { Transport = "http" };

        var result = await _service.PerformHealthCheckAsync(config);

        result.IsHealthy.Should().BeFalse();
        result.ErrorMessage.Should().Contain("endpoint");
    }

    [Fact]
    public async Task HealthCheck_WithStdioTransport_ShouldUseStdioPath()
    {
        var config = new McpServerConfig { Endpoint = "npx test-server", Transport = "stdio" };
        _httpClient.Setup(x => x.ValidateInitializeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransportResult<InitializeResult>
            {
                IsSuccessful = true,
                Transport = new TransportMetadata { Duration = TimeSpan.FromMilliseconds(200) },
                Payload = new InitializeResult 
                { 
                    ProtocolVersion = "2025-03-26",
                    ServerInfo = new Implementation { Name = "Stdio", Version = "1.0" },
                    Capabilities = new McpServerCapabilities()
                }
            });

        var result = await _service.PerformHealthCheckAsync(config);

        result.IsHealthy.Should().BeTrue();
    }

    [Fact]
    public async Task HealthCheck_WithStdioFailure_ShouldReportError()
    {
        var config = new McpServerConfig { Endpoint = "npx bad-server", Transport = "stdio" };
        _httpClient.Setup(x => x.ValidateInitializeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Process crashed"));

        var result = await _service.PerformHealthCheckAsync(config);

        result.IsHealthy.Should().BeFalse();
        result.ErrorMessage.Should().Contain("STDIO");
    }

    [Fact]
    public async Task HealthCheck_WithTimeout_ShouldReportTimeout()
    {
        var config = new McpServerConfig { Endpoint = "https://slow.com/mcp", Transport = "http" };
        _httpClient.Setup(x => x.ValidateInitializeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        var result = await _service.PerformHealthCheckAsync(config);

        result.IsHealthy.Should().BeFalse();
        result.Disposition.Should().Be(HealthCheckDisposition.TransientFailure);
        result.AllowsDeferredValidation.Should().BeTrue();
        result.ErrorMessage.Should().Contain("timed out");
    }

    [Fact]
    public async Task HealthCheck_WithUnexpectedException_ShouldReportError()
    {
        var config = new McpServerConfig { Endpoint = "https://test.com/mcp", Transport = "http" };
        _httpClient.Setup(x => x.ValidateInitializeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Unexpected error"));

        var result = await _service.PerformHealthCheckAsync(config);

        result.IsHealthy.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Unexpected");
    }
}
