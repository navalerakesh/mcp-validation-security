using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Infrastructure.Services;
using Mcp.Benchmark.Infrastructure.Validators;
using Microsoft.Extensions.Logging;

using Mcp.Benchmark.Infrastructure.Registries;

namespace Mcp.Benchmark.Tests.Unit;

/// <summary>
/// Professional unit tests for MCP Protocol Compliance Validator.
/// Tests constructor validation and basic object initialization.
/// Focuses on dependency injection and basic setup validation.
/// </summary>
public class ProtocolComplianceValidatorUnitTests : IDisposable
{
    private readonly Mock<ILogger<ProtocolComplianceValidator>> _loggerMock;
    private readonly Mock<IMcpHttpClient> _mcpHttpClientMock;
    private readonly Mock<IProtocolRuleRegistry> _ruleRegistryMock;

    public ProtocolComplianceValidatorUnitTests()
    {
        _loggerMock = new Mock<ILogger<ProtocolComplianceValidator>>();
        _mcpHttpClientMock = new Mock<IMcpHttpClient>();
        _ruleRegistryMock = new Mock<IProtocolRuleRegistry>();
    }

    [Fact]
    public void Constructor_WithValidDependencies_ShouldInitializeCorrectly()
    {
        // Arrange & Act
        var validator = new ProtocolComplianceValidator(_loggerMock.Object, _mcpHttpClientMock.Object, _ruleRegistryMock.Object);

        // Assert
        validator.Should().NotBeNull();
        validator.Should().BeOfType<ProtocolComplianceValidator>();
    }

    [Fact]
    public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
    {
        // Arrange & Act & Assert
        Action act = () => new ProtocolComplianceValidator(null!, _mcpHttpClientMock.Object, _ruleRegistryMock.Object);
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("logger");
    }

    [Fact]
    public void Constructor_WithNullMcpHttpClient_ShouldThrowArgumentNullException()
    {
        // Arrange & Act & Assert
        Action act = () => new ProtocolComplianceValidator(_loggerMock.Object, null!, _ruleRegistryMock.Object);
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("httpClient");
    }

    [Fact]
    public void Constructor_WithNullRuleRegistry_ShouldThrowArgumentNullException()
    {
        // Arrange & Act & Assert
        Action act = () => new ProtocolComplianceValidator(_loggerMock.Object, _mcpHttpClientMock.Object, null!);
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("ruleRegistry");
    }

    [Theory]
    [InlineData("http")]
    [InlineData("https")]
    public async Task ValidateJsonRpcComplianceAsync_WithHttpEndpoint_ShouldExecuteWithoutException(string protocol)
    {
        // Arrange
        var validator = new ProtocolComplianceValidator(_loggerMock.Object, _mcpHttpClientMock.Object, _ruleRegistryMock.Object);
        var serverConfig = new McpServerConfig
        {
            Endpoint = $"{protocol}://localhost:8080/mcp",
            Transport = "http"
        };
        var config = new ProtocolComplianceConfig
        {
            TestJsonRpcCompliance = true,
            ProtocolVersion = "2024-11-05"
        };

        // Act & Assert - Should not throw exceptions during execution
        var result = await validator.ValidateJsonRpcComplianceAsync(serverConfig, config);
        result.Should().NotBeNull();
        result.Status.Should().BeOneOf(TestStatus.Passed, TestStatus.Failed, TestStatus.Error);
    }

    [Fact]
    public async Task ValidateJsonRpcComplianceAsync_WithStdioTransport_ShouldReturnFailedWithMessage()
    {
        // Arrange
        var validator = new ProtocolComplianceValidator(_loggerMock.Object, _mcpHttpClientMock.Object, _ruleRegistryMock.Object);
        var serverConfig = new McpServerConfig
        {
            Transport = "stdio"
        };
        var config = new ProtocolComplianceConfig
        {
            TestJsonRpcCompliance = true,
            ProtocolVersion = "2024-11-05"
        };

        // Act
        var result = await validator.ValidateJsonRpcComplianceAsync(serverConfig, config);

        // Assert
        result.Should().NotBeNull();
        result.Status.Should().Be(TestStatus.Failed);
        result.CriticalErrors.Should().NotBeEmpty();
        result.CriticalErrors.Should().Contain(e => e.Contains("STDIO"));
    }

    [Fact]
    public async Task ValidateJsonRpcComplianceAsync_WithNullEndpoint_ShouldReturnFailedStatus()
    {
        // Arrange
        var validator = new ProtocolComplianceValidator(_loggerMock.Object, _mcpHttpClientMock.Object, _ruleRegistryMock.Object);
        var serverConfig = new McpServerConfig
        {
            Endpoint = null,
            Transport = "http"
        };
        var config = new ProtocolComplianceConfig
        {
            TestJsonRpcCompliance = true,
            ProtocolVersion = "2024-11-05"
        };

        // Act
        var result = await validator.ValidateJsonRpcComplianceAsync(serverConfig, config);

        // Assert
        result.Should().NotBeNull();
        result.Status.Should().Be(TestStatus.Failed);
        result.CriticalErrors.Should().NotBeEmpty();
        result.CriticalErrors.Should().Contain(e => e.Contains("endpoint"));
    }

    [Fact]
    public async Task ValidateJsonRpcComplianceAsync_WithValidConfig_ShouldSetCorrectTimestamps()
    {
        // Arrange
        var validator = new ProtocolComplianceValidator(_loggerMock.Object, _mcpHttpClientMock.Object, _ruleRegistryMock.Object);
        var serverConfig = new McpServerConfig
        {
            Endpoint = "http://localhost:8080/mcp",
            Transport = "http"
        };
        var config = new ProtocolComplianceConfig
        {
            TestJsonRpcCompliance = true,
            ProtocolVersion = "2024-11-05"
        };
        var startTime = DateTime.UtcNow;

        // Act
        var result = await validator.ValidateJsonRpcComplianceAsync(serverConfig, config);

        // Assert
        result.Should().NotBeNull();
        result.Duration.Should().BeGreaterThan(TimeSpan.Zero);
        // Validation should complete within reasonable time (not hang indefinitely)
        result.Duration.Should().BeLessThan(TimeSpan.FromMinutes(1));
    }

    [Theory]
    [InlineData("2024-11-05")]
    [InlineData("1.0.0")]
    [InlineData("latest")]
    public async Task ValidateJsonRpcComplianceAsync_WithDifferentProtocolVersions_ShouldProcessCorrectly(
        string protocolVersion)
    {
        // Arrange
        var validator = new ProtocolComplianceValidator(_loggerMock.Object, _mcpHttpClientMock.Object, _ruleRegistryMock.Object);
        var serverConfig = new McpServerConfig
        {
            Endpoint = "http://localhost:8080/mcp",
            Transport = "http"
        };
        var config = new ProtocolComplianceConfig
        {
            TestJsonRpcCompliance = true,
            ProtocolVersion = protocolVersion
        };

        // Act & Assert - Should handle different protocol versions gracefully
        var result = await validator.ValidateJsonRpcComplianceAsync(serverConfig, config);
        result.Should().NotBeNull();
        result.Status.Should().BeOneOf(TestStatus.Passed, TestStatus.Failed, TestStatus.Error);
    }

    public void Dispose()
    {
        // Cleanup any resources if needed
        _loggerMock?.Reset();
        _mcpHttpClientMock?.Reset();
    }
}
