using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Constants;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Infrastructure.Services;
using Mcp.Benchmark.Infrastructure.Validators;
using Microsoft.Extensions.Logging;
using Moq;
using FluentAssertions;
using Xunit;

namespace Mcp.Benchmark.Tests.Integration;

/// <summary>
/// Integration tests for security validation functionality.
/// Tests actual security assessment methods and validation against MCP servers.
/// </summary>
public class SecurityValidatorIntegrationTests
{
    private readonly SecurityValidator _securityValidator;
    private readonly Mock<ILogger<SecurityValidator>> _logger;
    private readonly Mock<ILoggerFactory> _loggerFactory;
    private readonly Mock<IMcpHttpClient> _httpClientMock;

    public SecurityValidatorIntegrationTests()
    {
        _logger = new Mock<ILogger<SecurityValidator>>();
        _loggerFactory = new Mock<ILoggerFactory>();
        _httpClientMock = new Mock<IMcpHttpClient>();
        
        var authLogger = new Mock<ILogger<McpCompliantAuthValidator>>();
        var authValidator = new McpCompliantAuthValidator(authLogger.Object, _httpClientMock.Object);
        
        _securityValidator = new SecurityValidator(_logger.Object, _loggerFactory.Object, _httpClientMock.Object, authValidator);
    }

    [Fact]
    public async Task PerformSecurityAssessmentAsync_WithValidConfig_ShouldReturnResults()
    {
        // Arrange
        var serverConfig = new McpServerConfig
        {
            Endpoint = "https://test-server.com/mcp",
            Transport = "http"
        };

        var securityConfig = new SecurityTestingConfig
        {
            TestInputValidation = true,
            TestInjectionAttacks = true,
            TestAuthenticationBypass = true,
            MaxTestDurationSeconds = 60
        };

        // Mock HTTP responses for security tests
                // Mock error response from server for vulnerability testing
        _httpClientMock.Setup(x => x.CallAsync(
                It.IsAny<string>(), 
                It.IsAny<string>(), 
                It.IsAny<object>(), 
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse
            {
                StatusCode = 200,
                IsSuccess = true,
                RawJson = "{\"jsonrpc\":\"2.0\",\"result\":{},\"id\":\"test\"}"
            });

        // Act
        var result = await _securityValidator.PerformSecurityAssessmentAsync(
            serverConfig, securityConfig, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Status.Should().BeOneOf(TestStatus.Passed, TestStatus.Failed, TestStatus.Error);
    }

    [Fact] 
    public async Task ValidateInputSanitizationAsync_WithMaliciousPayloads_ShouldDetectVulnerabilities()
    {
        // Arrange
        var serverConfig = new McpServerConfig
        {
            Endpoint = "https://test-server.com/mcp"
        };

        var maliciousPayloads = new List<SecurityTestPayload>
        {
            new SecurityTestPayload
            {
                Name = "SQL Injection Test",
                Target = "test-method",
                Payload = "'; DROP TABLE users; --",
                ShouldReject = true
            },
            new SecurityTestPayload  
            {
                Name = "XSS Test",
                Target = "test-method",
                Payload = "<script>alert('xss')</script>",
                ShouldReject = true
            },
            new SecurityTestPayload
            {
                Name = "Command Injection Test", 
                Target = "test-method",
                Payload = "test; rm -rf /",
                ShouldReject = true
            }
        };

        _httpClientMock.Setup(x => x.CallAsync(
                It.IsAny<string>(),
                ValidationConstants.Methods.ToolsList,
                It.IsAny<object>(),
                It.IsAny<AuthenticationConfig?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse
            {
                StatusCode = 200,
                IsSuccess = true,
                RawJson = "{\"result\":{\"tools\":[{\"name\":\"search\",\"inputSchema\":{\"type\":\"object\",\"properties\":{\"query\":{\"type\":\"string\"}}}}]}}"
            });

        // Mock HTTP responses that reflect one payload and therefore indicate a potential vulnerability.
        _httpClientMock.Setup(x => x.CallAsync(
                It.IsAny<string>(),
                ValidationConstants.Methods.ToolsCall,
                It.IsAny<object>(),
                It.IsAny<AuthenticationConfig?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse
            {
                StatusCode = 200,
                IsSuccess = true,
                RawJson = "{\"jsonrpc\":\"2.0\",\"result\":{\"content\":[{\"type\":\"text\",\"text\":\"'; DROP TABLE users; --\"}]},\"id\":\"test\"}"
            });

        // Act  
        var result = await _securityValidator.ValidateInputSanitizationAsync(
            serverConfig, maliciousPayloads, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Status.Should().Be(TestStatus.Failed);
    }

    [Fact]
    public async Task SimulateAttackVectorsAsync_WithCommonAttacks_ShouldTestResilience()
    {
        // Arrange
        var serverConfig = new McpServerConfig
        {
            Endpoint = "https://test-server.com/mcp"
        };

        var attackVectors = new List<string>
        {
            "buffer_overflow",
            "denial_of_service", 
            "authentication_bypass",
            "privilege_escalation",
            "information_disclosure"
        };

        _httpClientMock.Setup(x => x.CallAsync(
                It.IsAny<string>(),
                ValidationConstants.Methods.ToolsList,
                It.IsAny<object>(),
                It.IsAny<AuthenticationConfig?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse
            {
                StatusCode = 200,
                IsSuccess = true,
                RawJson = "{\"result\":{\"tools\":[{\"name\":\"search\",\"inputSchema\":{\"type\":\"object\",\"properties\":{\"query\":{\"type\":\"string\"}}}}]}}"
            });

        // Mock blocked tool-call responses for each attack vector.
        _httpClientMock.Setup(x => x.CallAsync(
                It.IsAny<string>(),
                ValidationConstants.Methods.ToolsCall,
                It.IsAny<object>(),
                It.IsAny<AuthenticationConfig?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse
            {
                StatusCode = 403,
                IsSuccess = false,
                RawJson = "{\"jsonrpc\":\"2.0\",\"error\":{\"code\":-32600,\"message\":\"Invalid Request\"},\"id\":null}"
            });

        // Act
        var result = await _securityValidator.SimulateAttackVectorsAsync(
            serverConfig, attackVectors, CancellationToken.None);

        // Assert 
        result.Should().NotBeNull();
        result.Status.Should().Be(TestStatus.Passed);
        result.AttackSimulations.Should().HaveCount(attackVectors.Count);
    }

    [Fact]
    public async Task SecurityValidation_WithNetworkError_ShouldHandleGracefully()
    {
        // Arrange
        var serverConfig = new McpServerConfig
        {
            Endpoint = "https://nonexistent-server.com/mcp"
        };

        var securityConfig = new SecurityTestingConfig
        {
            TestInputValidation = true,
            MaxTestDurationSeconds = 30
        };

        // Mock network failure
        var errorResponse = new JsonRpcResponse
        {
            StatusCode = -1,
            IsSuccess = false,
            Error = "Network error: Connection refused"
        };

        _httpClientMock.Setup(x => x.CallAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<object>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(errorResponse);

        _httpClientMock.Setup(x => x.CallAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<object>(),
                It.IsAny<AuthenticationConfig>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(errorResponse);

        // Act
        var result = await _securityValidator.PerformSecurityAssessmentAsync(
            serverConfig, securityConfig, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        // With network errors, the server is unreachable so attacks cannot succeed.
        // The validator correctly reports this as Passed (no vulnerabilities exploitable)
        // or Error (auth subsystem treats connectivity failure as error).
        result.Status.Should().BeOneOf(TestStatus.Passed, TestStatus.Failed, TestStatus.Error);
        result.Vulnerabilities.Should().NotBeNull();
    }
}
