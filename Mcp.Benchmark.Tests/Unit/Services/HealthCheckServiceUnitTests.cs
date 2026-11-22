using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Models;
using System.Text.Json;
using Mcp.Benchmark.Infrastructure.Health;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using Moq;
using FluentAssertions;
using Xunit;

namespace Mcp.Benchmark.Tests.Unit;

public class HealthCheckServiceUnitTests
{
    private readonly Mock<IMcpHttpClient> _httpClientMock;
    private readonly Mock<ILogger<HealthCheckService>> _loggerMock;
    private readonly Mock<ITelemetryService> _telemetryServiceMock;
    private readonly HealthCheckService _service;

    public HealthCheckServiceUnitTests()
    {
        _httpClientMock = new Mock<IMcpHttpClient>();
        _loggerMock = new Mock<ILogger<HealthCheckService>>();
        _telemetryServiceMock = new Mock<ITelemetryService>();
        _service = new HealthCheckService(_httpClientMock.Object, _loggerMock.Object, _telemetryServiceMock.Object);
    }

    [Theory]
    [InlineData(McpResultType.Unknown, true, "valid", false)]
    [InlineData(McpResultType.Complete, true, "valid", true)]
    [InlineData(McpResultType.Complete, false, "valid", false)]
    [InlineData(McpResultType.Complete, null, "valid", false)]
    [InlineData(McpResultType.InputRequired, true, "valid", false)]
    [InlineData(McpResultType.Complete, true, "wrong-version", false)]
    [InlineData(McpResultType.Complete, true, "missing", false)]
    public async Task ModernHealth_ShouldRequireCompletedValidatedDiscovery(McpResultType resultType, bool? valid, string body, bool expected)
    {
        _httpClientMock.Setup(client => client.CallAsync(It.IsAny<string>(), "server/discover", null,
            It.IsAny<AuthenticationConfig?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Mcp.Benchmark.Core.Models.JsonRpcResponse
            {
                StatusCode = 200, IsSuccess = true, ProtocolSemanticsValid = valid, ResultType = resultType,
                RawJson = body == "missing" ? "{}" : JsonSerializer.Serialize(new
                {
                    jsonrpc = "2.0", id = "discovery", result = new
                    {
                        supportedVersions = new[] { body == "wrong-version" ? "2025-06-18" : "2026-07-28" },
                        capabilities = new { }, resultType = "complete", cacheScope = "public", ttlMs = 0
                    }
                })
            });

        var result = await _service.PerformHealthCheckAsync(new McpServerConfig
        {
            Endpoint = "https://example.test/mcp", Transport = "http", ProtocolVersion = "2026-07-28"
        });

        result.IsHealthy.Should().Be(expected);
        result.Disposition.Should().Be(expected ? HealthCheckDisposition.Healthy : HealthCheckDisposition.Unhealthy);
        result.ProtocolVersion.Should().Be(expected ? "2026-07-28" : null);
    }

    [Fact]
    public async Task PerformHealthCheckAsync_WithValidEndpoint_ShouldReturnHealthy()
    {
        // Arrange
        var config = new McpServerConfig { Endpoint = "http://localhost:8080", Transport = "http" };
        const string initializePayloadJson = """
        {
            "protocolVersion": "2024-11-05",
            "capabilities": {},
            "serverInfo": {
                "name": "TestServer",
                "version": "1.0.0"
            }
        }
        """;

        var sdkInitialize = JsonSerializer.Deserialize<InitializeResult>(initializePayloadJson)
            ?? throw new InvalidOperationException("Failed to deserialize InitializeResult for test");

        var initResult = new TransportResult<InitializeResult>
        {
            IsSuccessful = true,
            Payload = sdkInitialize,
            Transport = new TransportMetadata
            {
                Duration = TimeSpan.FromMilliseconds(42)
            }
        };

        _httpClientMock.Setup(x => x.ValidateInitializeAsync(config.Endpoint, It.IsAny<CancellationToken>()))
            .ReturnsAsync(initResult);

        // Act
        var result = await _service.PerformHealthCheckAsync(config);

        // Assert
        result.IsHealthy.Should().BeTrue();
        result.Disposition.Should().Be(HealthCheckDisposition.Healthy);
        result.ProtocolVersion.Should().Be("2024-11-05");
        result.ServerVersion.Should().Be("1.0.0");
        result.InitializationDetails.Should().NotBeNull();
        result.InitializationDetails!.Transport.Duration.TotalMilliseconds.Should().Be(42);
        result.InitializationDetails.Payload!.ServerInfo!.Name.Should().Be("TestServer");
        _telemetryServiceMock.Verify(x => x.TrackEvent("HealthCheckStarted", It.IsAny<IDictionary<string, string>>(), It.IsAny<IDictionary<string, double>>()), Times.Once);
    }

    [Fact]
    public async Task PerformHealthCheckAsync_WithUnreachableEndpoint_ShouldReturnUnhealthy()
    {
        // Arrange
        var config = new McpServerConfig { Endpoint = "http://localhost:8080", Transport = "http" };
        var initResult = new TransportResult<InitializeResult>
        {
            IsSuccessful = false,
            Error = "Connection refused",
            Transport = new TransportMetadata
            {
                Duration = TimeSpan.FromMilliseconds(42)
            }
        };

        _httpClientMock.Setup(x => x.ValidateInitializeAsync(config.Endpoint, It.IsAny<CancellationToken>()))
            .ReturnsAsync(initResult);

        // Act
        var result = await _service.PerformHealthCheckAsync(config);

        // Assert
        result.IsHealthy.Should().BeFalse();
        result.Disposition.Should().Be(HealthCheckDisposition.Unhealthy);
        result.ErrorMessage.Should().Be("Connection refused");
        result.InitializationDetails.Should().NotBeNull();
    }

    [Fact]
    public async Task PerformHealthCheckAsync_WithStdioTransport_ShouldHandleGracefully()
    {
        // Arrange
        var config = new McpServerConfig { Transport = "stdio", Endpoint = "test-command" };

        // Act
        var result = await _service.PerformHealthCheckAsync(config);

        // Assert
        // STDIO health check now attempts to use the injected IMcpHttpClient.
        // With a mock client, it may fail gracefully rather than block outright.
        result.Should().NotBeNull();
        result.ErrorMessage.Should().NotBeNull();
    }
}
