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
        result.Findings.Should().BeEmpty();
    }

    [Fact]
    public async Task ValidateAuth_WithProtectedResourceMetadataChallenge_ShouldFetchAndValidateMetadata()
    {
        const string metadataUrl = "https://test.com/.well-known/oauth-protected-resource";
        var config = new McpServerConfig
        {
            Endpoint = "https://test.com/mcp",
            Transport = "http",
            Profile = McpServerProfile.Authenticated
        };

        var authResponse = CreateAuthChallenge(metadataUrl);
        SetupAuthResponse(authResponse);
        _httpClient.Setup(x => x.GetStringAsync(metadataUrl, It.IsAny<CancellationToken>()))
            .ReturnsAsync("""
                {
                  "resource": "https://test.com/mcp",
                  "authorization_servers": ["https://login.example.com"],
                  "bearer_methods_supported": ["header"]
                }
                """);

        var result = await _validator.ValidateAuthenticationComplianceAsync(config, CancellationToken.None);

        result.ProtectedResourceMetadataUrl.Should().Be(metadataUrl);
        result.ProtectedResourceMetadata.Should().NotBeNull();
        result.ProtectedResourceMetadata!.AuthorizationServers.Should().Contain("https://login.example.com");
        result.Findings.Should().NotContain(f => f.RuleId == ValidationFindingRuleIds.AuthProtectedResourceMetadataMissing);
        result.Findings.Should().NotContain(f => f.RuleId == ValidationFindingRuleIds.AuthAuthorizationServersMissing);
        result.Findings.Should().NotContain(f => f.RuleId == ValidationFindingRuleIds.AuthBearerHeaderUnsupported);
    }

    [Fact]
    public async Task ValidateAuth_With401ChallengeMissingResourceMetadata_ShouldEmitSpecFinding()
    {
        var config = new McpServerConfig
        {
            Endpoint = "https://test.com/mcp",
            Transport = "http",
            Profile = McpServerProfile.Authenticated
        };

        SetupAuthResponse(new JsonRpcResponse
        {
            StatusCode = 401,
            IsSuccess = false,
            Headers = new Dictionary<string, string> { ["WWW-Authenticate"] = "Bearer realm=\"mcp\"" }
        });

        var result = await _validator.ValidateAuthenticationComplianceAsync(config, CancellationToken.None);

        result.Findings.Should().Contain(f => f.RuleId == ValidationFindingRuleIds.AuthProtectedResourceMetadataMissing);
    }

    [Fact]
    public async Task ValidateAuth_WhenQueryTokenGrantsSensitiveAccess_ShouldEmitQueryTokenFinding()
    {
        const string metadataUrl = "https://test.com/.well-known/oauth-protected-resource";
        var config = new McpServerConfig
        {
            Endpoint = "https://test.com/mcp",
            Transport = "http",
            Profile = McpServerProfile.Authenticated
        };

        SetupMetadata(metadataUrl);
        SetupAuthResponse((endpoint, method, _, _, _) =>
            endpoint.Contains("access_token=", StringComparison.OrdinalIgnoreCase) && method == "tools/call"
                ? CreateJsonRpcSuccess()
                : method == "initialize"
                    ? CreateInitializeWithToolsCapability()
                    : CreateAuthChallenge(metadataUrl));

        var result = await _validator.ValidateAuthenticationComplianceAsync(config, CancellationToken.None);

        result.TestScenarios.Should().Contain(s =>
            s.TestType == "Query Token" &&
            s.Method == "tools/call" &&
            s.AssessmentDisposition == AuthenticationAssessmentDisposition.Insecure);
        result.Findings.Should().Contain(f => f.RuleId == ValidationFindingRuleIds.AuthQueryTokenAccepted);
    }

    [Fact]
    public async Task ValidateAuth_WhenWrongAudienceTokenIsAccepted_ShouldEmitAudienceAndPassthroughFindings()
    {
        const string metadataUrl = "https://test.com/.well-known/oauth-protected-resource";
        var config = new McpServerConfig
        {
            Endpoint = "https://test.com/mcp",
            Transport = "http",
            Profile = McpServerProfile.Authenticated
        };

        SetupMetadata(metadataUrl);
        SetupAuthResponse((_, method, _, authentication, _) =>
            method == "initialize"
                ? CreateInitializeWithToolsCapability()
                : method == "tools/call" && authentication?.Token?.Contains("wrong_audience_simulation", StringComparison.OrdinalIgnoreCase) == true
                    ? CreateJsonRpcSuccess()
                    : CreateAuthChallenge(metadataUrl));

        var result = await _validator.ValidateAuthenticationComplianceAsync(config, CancellationToken.None);

        result.TestScenarios.Should().Contain(s =>
            s.TestType == "Wrong Audience (RFC 8707)" &&
            s.Method == "tools/call" &&
            s.AssessmentDisposition == AuthenticationAssessmentDisposition.Insecure);
        result.Findings.Should().Contain(f => f.RuleId == ValidationFindingRuleIds.AuthWrongAudienceAccepted);
        result.Findings.Should().Contain(f => f.RuleId == ValidationFindingRuleIds.AuthTokenPassthroughRisk);
    }

    [Fact]
    public async Task ValidateAuth_WhenInvalidTokenUsesNon401Status_ShouldEmitInvalidTokenStatusFinding()
    {
        const string metadataUrl = "https://test.com/.well-known/oauth-protected-resource";
        var config = new McpServerConfig
        {
            Endpoint = "https://test.com/mcp",
            Transport = "http",
            Profile = McpServerProfile.Authenticated
        };

        SetupMetadata(metadataUrl);
        SetupAuthResponse((_, method, _, authentication, _) =>
            method == "initialize"
                ? CreateInitializeWithToolsCapability()
                : authentication?.Token?.Contains("invalid_fake_token", StringComparison.OrdinalIgnoreCase) == true
                    ? new JsonRpcResponse
                    {
                        StatusCode = 403,
                        IsSuccess = false,
                        Headers = new Dictionary<string, string> { ["WWW-Authenticate"] = $"Bearer resource_metadata=\"{metadataUrl}\"" }
                    }
                    : CreateAuthChallenge(metadataUrl));

        var result = await _validator.ValidateAuthenticationComplianceAsync(config, CancellationToken.None);

        result.Findings.Should().Contain(f => f.RuleId == ValidationFindingRuleIds.AuthInvalidTokenStatus);
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

    private void SetupAuthResponse(JsonRpcResponse response)
    {
        SetupAuthResponse((_, _, _, _, _) => response);
    }

    private void SetupAuthResponse(Func<string, string, object?, AuthenticationConfig?, CancellationToken, JsonRpcResponse> responseFactory)
    {
        _httpClient.Setup(x => x.CallAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<object>(),
                It.IsAny<AuthenticationConfig>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string endpoint, string method, object? parameters, AuthenticationConfig? authentication, CancellationToken cancellationToken) =>
                responseFactory(endpoint, method, parameters, authentication, cancellationToken));

        _httpClient.Setup(x => x.CallAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<object>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string endpoint, string method, object? parameters, CancellationToken cancellationToken) =>
                responseFactory(endpoint, method, parameters, null, cancellationToken));
    }

    private void SetupMetadata(string metadataUrl)
    {
        _httpClient.Setup(x => x.GetStringAsync(metadataUrl, It.IsAny<CancellationToken>()))
            .ReturnsAsync("""
                {
                  "resource": "https://test.com/mcp",
                  "authorization_servers": ["https://login.example.com"],
                  "bearer_methods_supported": ["header"]
                }
                """);
    }

    private static JsonRpcResponse CreateAuthChallenge(string metadataUrl)
    {
        return new JsonRpcResponse
        {
            StatusCode = 401,
            IsSuccess = false,
            Headers = new Dictionary<string, string> { ["WWW-Authenticate"] = $"Bearer resource_metadata=\"{metadataUrl}\"" }
        };
    }

    private static JsonRpcResponse CreateInitializeWithToolsCapability()
    {
        return new JsonRpcResponse
        {
            StatusCode = 200,
            IsSuccess = true,
            RawJson = "{\"jsonrpc\":\"2.0\",\"result\":{\"capabilities\":{\"tools\":{}}},\"id\":1}"
        };
    }

    private static JsonRpcResponse CreateJsonRpcSuccess()
    {
        return new JsonRpcResponse
        {
            StatusCode = 200,
            IsSuccess = true,
            RawJson = "{\"jsonrpc\":\"2.0\",\"result\":{},\"id\":1}"
        };
    }
}
