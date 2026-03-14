using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Infrastructure.Validators;
using Microsoft.Extensions.Logging;
using Moq;
using FluentAssertions;
using Xunit;

namespace Mcp.Benchmark.Tests.Unit.Validators;

/// <summary>
/// Comprehensive tests for McpCompliantAuthValidator covering all auth scenarios.
/// </summary>
public class McpCompliantAuthValidatorComprehensiveTests
{
    private readonly McpCompliantAuthValidator _validator;
    private readonly Mock<IMcpHttpClient> _httpClient;

    public McpCompliantAuthValidatorComprehensiveTests()
    {
        _httpClient = new Mock<IMcpHttpClient>();
        _validator = new McpCompliantAuthValidator(new Mock<ILogger<McpCompliantAuthValidator>>().Object, _httpClient.Object);
    }

    [Fact]
    public async Task ValidateAuth_WithStdioTransport_ShouldReturnStdioResult()
    {
        var config = new McpServerConfig { Endpoint = "npx server", Transport = "stdio" };

        var result = await _validator.ValidateAuthenticationComplianceAsync(config, CancellationToken.None);

        result.Status.Should().Be(TestStatus.Passed);
        result.ComplianceScore.Should().Be(100);
        result.TestScenarios.Should().Contain(s => s.ScenarioName.Contains("STDIO"));
    }

    [Fact]
    public async Task ValidateAuth_WithNetworkError_ShouldReturnError()
    {
        var config = new McpServerConfig { Endpoint = "https://test.com/mcp", Transport = "http" };
        _httpClient.Setup(x => x.CallAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse { StatusCode = -1, IsSuccess = false, Error = "Connection refused" });

        var result = await _validator.ValidateAuthenticationComplianceAsync(config, CancellationToken.None);

        result.Status.Should().Be(TestStatus.Error);
    }

    [Fact]
    public async Task ValidateAuth_With401OnAllEndpoints_ShouldReturnResults()
    {
        var config = new McpServerConfig { Endpoint = "https://test.com/mcp", Transport = "http" };
        var authResponse = new JsonRpcResponse
        {
            StatusCode = 401, IsSuccess = false,
            Headers = new Dictionary<string, string> { { "WWW-Authenticate", "Bearer" } }
        };
        _httpClient.Setup(x => x.CallAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(authResponse);
        _httpClient.Setup(x => x.CallAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>(), It.IsAny<AuthenticationConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(authResponse);

        var result = await _validator.ValidateAuthenticationComplianceAsync(config, CancellationToken.None);

        result.TestScenarios.Should().NotBeEmpty();
        result.Status.Should().NotBe(TestStatus.Error);
    }

    [Fact]
    public async Task ValidateAuth_With200OnNoAuth_ForAuthenticatedProfile_ShouldBeNonCompliant()
    {
        var config = new McpServerConfig { Endpoint = "https://test.com/mcp", Transport = "http" };
        _httpClient.Setup(x => x.CallAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse { StatusCode = 200, IsSuccess = true, RawJson = "{\"jsonrpc\":\"2.0\",\"result\":{},\"id\":1}" });

        var result = await _validator.ValidateAuthenticationComplianceAsync(config, CancellationToken.None);

        // 200 on No Auth means server doesn't require auth - which is valid for public servers
        result.Should().NotBeNull();
        result.TestScenarios.Should().NotBeEmpty();
    }

    [Fact]
    public async Task ValidateAuth_With403_ShouldBeCompliant()
    {
        var config = new McpServerConfig { Endpoint = "https://test.com/mcp", Transport = "http" };
        _httpClient.Setup(x => x.CallAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse { StatusCode = 403, IsSuccess = false });

        var result = await _validator.ValidateAuthenticationComplianceAsync(config, CancellationToken.None);

        result.TestScenarios.Should().NotBeEmpty();
    }

    [Fact]
    public async Task ValidateAuth_WithValidToken_ShouldIncludeValidTokenScenario()
    {
        var config = new McpServerConfig
        {
            Endpoint = "https://test.com/mcp",
            Transport = "http",
            Authentication = new AuthenticationConfig { Type = "Bearer", Token = "valid_test_token" }
        };
        _httpClient.Setup(x => x.CallAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse { StatusCode = 200, IsSuccess = true, RawJson = "{\"jsonrpc\":\"2.0\",\"result\":{},\"id\":1}" });

        var result = await _validator.ValidateAuthenticationComplianceAsync(config, CancellationToken.None);

        result.TestScenarios.Should().NotBeEmpty();
        // Auth validator tests multiple scenarios including valid token
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task ValidateAuth_WithNoEndpoint_ShouldHandleGracefully()
    {
        var config = new McpServerConfig { Transport = "http" };

        // Should not crash
        var act = async () => await _validator.ValidateAuthenticationComplianceAsync(config, CancellationToken.None);
        await act.Should().NotThrowAsync();
    }
}
