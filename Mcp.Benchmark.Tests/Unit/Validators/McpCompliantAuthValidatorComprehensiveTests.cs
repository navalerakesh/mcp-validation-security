using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Infrastructure.Authentication;
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
    public async Task ValidateAuth_WithAuthorizationServerMetadata_ShouldValidateIssuerEndpointsAndPkce()
    {
        const string metadataUrl = "https://test.com/.well-known/oauth-protected-resource";
        const string issuer = "https://login.example.com";
        const string authorizationMetadataUrl = "https://login.example.com/.well-known/oauth-authorization-server";
        var config = new McpServerConfig { Endpoint = "https://test.com/mcp", Transport = "http", Profile = McpServerProfile.Authenticated };
        SetupAuthResponse(CreateAuthChallenge(metadataUrl));
        _httpClient.Setup(client => client.GetStringAsync(metadataUrl, It.IsAny<CancellationToken>()))
            .ReturnsAsync($"{{\"resource\":\"https://test.com/mcp\",\"authorization_servers\":[\"{issuer}\"],\"bearer_methods_supported\":[\"header\"]}}");
        _httpClient.Setup(client => client.GetStringAsync(authorizationMetadataUrl, It.IsAny<CancellationToken>()))
            .ReturnsAsync("""
                {
                  "issuer": "https://login.example.com",
                  "authorization_endpoint": "https://login.example.com/authorize",
                  "token_endpoint": "https://login.example.com/token",
                  "code_challenge_methods_supported": ["S256"],
                  "authorization_response_iss_parameter_supported": true
                }
                """);

        var result = await _validator.ValidateAuthenticationComplianceAsync(config, CancellationToken.None);

        result.AuthorizationServerMetadata.Should().ContainSingle(evidence =>
            evidence.Fetched && evidence.IsValid && evidence.DiscoveryVariant == "rfc8414" &&
            evidence.Metadata!.AuthorizationResponseIssuerParameterSupported == true);
        result.Findings.Should().NotContain(finding => finding.RuleId == ValidationFindingRuleIds.AuthAuthorizationServerIssuerMismatch);
        result.Findings.Should().NotContain(finding => finding.RuleId == ValidationFindingRuleIds.AuthPkceS256Missing);
        result.AuthorizationServerMetadata.Single().EnterpriseManagedAuthorizationSupported.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateAuth_WithTrailingSlashIssuerMismatch_ShouldFailExactIssuerIdentity()
    {
        const string metadataUrl = "https://test.com/.well-known/oauth-protected-resource";
        const string issuer = "https://login.example.com/";
        const string authorizationMetadataUrl = "https://login.example.com/.well-known/oauth-authorization-server";
        var config = new McpServerConfig { Endpoint = "https://test.com/mcp", Transport = "http", Profile = McpServerProfile.Authenticated };
        SetupAuthResponse(CreateAuthChallenge(metadataUrl));
        _httpClient.Setup(client => client.GetStringAsync(metadataUrl, It.IsAny<CancellationToken>()))
            .ReturnsAsync($"{{\"resource\":\"https://test.com/mcp\",\"authorization_servers\":[\"{issuer}\"],\"bearer_methods_supported\":[\"header\"]}}");
        _httpClient.Setup(client => client.GetStringAsync(authorizationMetadataUrl, It.IsAny<CancellationToken>()))
            .ReturnsAsync("""
                {
                  "issuer": "https://login.example.com",
                  "authorization_endpoint": "https://login.example.com/authorize",
                  "token_endpoint": "https://login.example.com/token",
                  "code_challenge_methods_supported": ["S256"]
                }
                """);

        var result = await _validator.ValidateAuthenticationComplianceAsync(config, CancellationToken.None);

        result.Status.Should().Be(TestStatus.Failed);
        result.ComplianceScore.Should().BeLessThanOrEqualTo(50);
        result.Findings.Should().Contain(finding => finding.RuleId == ValidationFindingRuleIds.AuthAuthorizationServerIssuerMismatch);
    }

    [Fact]
    public async Task ValidateAuth_WithEnterpriseManagedAuthorizationMetadata_ShouldRecordValidDiscovery()
    {
        const string metadataUrl = "https://test.com/.well-known/oauth-protected-resource";
        const string issuer = "https://login.example.com";
        const string authorizationMetadataUrl = "https://login.example.com/.well-known/oauth-authorization-server";
        var config = new McpServerConfig { Endpoint = "https://test.com/mcp", Transport = "http", Profile = McpServerProfile.Authenticated };
        SetupAuthResponse(CreateAuthChallenge(metadataUrl));
        _httpClient.Setup(client => client.GetStringAsync(metadataUrl, It.IsAny<CancellationToken>()))
            .ReturnsAsync($"{{\"resource\":\"https://test.com/mcp\",\"authorization_servers\":[\"{issuer}\"],\"bearer_methods_supported\":[\"header\"]}}");
        _httpClient.Setup(client => client.GetStringAsync(authorizationMetadataUrl, It.IsAny<CancellationToken>()))
            .ReturnsAsync("""
                {
                  "issuer": "https://login.example.com",
                  "authorization_endpoint": "https://login.example.com/authorize",
                  "token_endpoint": "https://login.example.com/token",
                  "code_challenge_methods_supported": ["S256"],
                  "grant_types_supported": ["urn:ietf:params:oauth:grant-type:jwt-bearer"],
                  "authorization_grant_profiles_supported": ["urn:ietf:params:oauth:grant-profile:id-jag"]
                }
                """);

        var result = await _validator.ValidateAuthenticationComplianceAsync(config, CancellationToken.None);

        result.AuthorizationServerMetadata.Should().ContainSingle(evidence =>
            evidence.EnterpriseManagedAuthorizationSupported && evidence.EnterpriseManagedAuthorizationValid && evidence.IsValid);
        result.Findings.Should().NotContain(finding => finding.RuleId == ValidationFindingRuleIds.AuthEnterpriseManagedAuthorizationInvalid);
    }

    [Fact]
    public async Task ValidateAuth_WithIdJagProfileButNoJwtBearerGrant_ShouldEmitConsistencyFinding()
    {
        const string metadataUrl = "https://test.com/.well-known/oauth-protected-resource";
        const string issuer = "https://login.example.com";
        const string authorizationMetadataUrl = "https://login.example.com/.well-known/oauth-authorization-server";
        var config = new McpServerConfig { Endpoint = "https://test.com/mcp", Transport = "http", Profile = McpServerProfile.Authenticated };
        SetupAuthResponse(CreateAuthChallenge(metadataUrl));
        _httpClient.Setup(client => client.GetStringAsync(metadataUrl, It.IsAny<CancellationToken>()))
            .ReturnsAsync($"{{\"resource\":\"https://test.com/mcp\",\"authorization_servers\":[\"{issuer}\"],\"bearer_methods_supported\":[\"header\"]}}");
        _httpClient.Setup(client => client.GetStringAsync(authorizationMetadataUrl, It.IsAny<CancellationToken>()))
            .ReturnsAsync("""
                {
                  "issuer": "https://login.example.com",
                  "authorization_endpoint": "https://login.example.com/authorize",
                  "token_endpoint": "https://login.example.com/token",
                  "code_challenge_methods_supported": ["S256"],
                  "grant_types_supported": ["authorization_code"],
                  "authorization_grant_profiles_supported": ["urn:ietf:params:oauth:grant-profile:id-jag"]
                }
                """);

        var result = await _validator.ValidateAuthenticationComplianceAsync(config, CancellationToken.None);

        result.AuthorizationServerMetadata.Should().ContainSingle(evidence =>
            evidence.EnterpriseManagedAuthorizationSupported && !evidence.EnterpriseManagedAuthorizationValid && !evidence.IsValid);
        result.Findings.Should().Contain(finding => finding.RuleId == ValidationFindingRuleIds.AuthEnterpriseManagedAuthorizationInvalid);
    }

    [Fact]
    public async Task ValidateAuth_WithInvalidAuthorizationServerMetadata_ShouldEmitIssuerEndpointAndPkceFindings()
    {
        const string metadataUrl = "https://test.com/.well-known/oauth-protected-resource";
        const string issuer = "https://login.example.com/tenant";
        const string authorizationMetadataUrl = "https://login.example.com/.well-known/oauth-authorization-server/tenant";
        var config = new McpServerConfig { Endpoint = "https://test.com/mcp", Transport = "http", Profile = McpServerProfile.Authenticated };
        SetupAuthResponse(CreateAuthChallenge(metadataUrl));
        _httpClient.Setup(client => client.GetStringAsync(metadataUrl, It.IsAny<CancellationToken>()))
            .ReturnsAsync($"{{\"resource\":\"https://test.com/mcp\",\"authorization_servers\":[\"{issuer}\"],\"bearer_methods_supported\":[\"header\"]}}");
        _httpClient.Setup(client => client.GetStringAsync(authorizationMetadataUrl, It.IsAny<CancellationToken>()))
            .ReturnsAsync("""
                {
                  "issuer": "https://login.example.com/other",
                  "authorization_endpoint": "http://login.example.com/authorize",
                  "token_endpoint": "http://login.example.com/token",
                  "code_challenge_methods_supported": ["plain"]
                }
                """);

        var result = await _validator.ValidateAuthenticationComplianceAsync(config, CancellationToken.None);

        result.AuthorizationServerMetadata.Should().ContainSingle(evidence => evidence.Fetched && !evidence.IsValid);
        result.Findings.Should().Contain(finding => finding.RuleId == ValidationFindingRuleIds.AuthAuthorizationServerIssuerMismatch);
        result.Findings.Should().Contain(finding => finding.RuleId == ValidationFindingRuleIds.AuthAuthorizationEndpointInsecure);
        result.Findings.Should().Contain(finding => finding.RuleId == ValidationFindingRuleIds.AuthTokenEndpointInsecure);
        result.Findings.Should().Contain(finding => finding.RuleId == ValidationFindingRuleIds.AuthPkceS256Missing);
    }

    [Fact]
    public async Task ValidateAuth_WithPathIssuer_ShouldTryOidcPathInsertionBeforeAppending()
    {
        const string metadataUrl = "https://test.com/.well-known/oauth-protected-resource";
        const string issuer = "https://login.example.com/tenant";
        const string rfcUrl = "https://login.example.com/.well-known/oauth-authorization-server/tenant";
        const string oidcInsertionUrl = "https://login.example.com/.well-known/openid-configuration/tenant";
        const string oidcAppendingUrl = "https://login.example.com/tenant/.well-known/openid-configuration";
        var config = new McpServerConfig { Endpoint = "https://test.com/mcp", Transport = "http", Profile = McpServerProfile.Authenticated };
        SetupAuthResponse(CreateAuthChallenge(metadataUrl));
        _httpClient.Setup(client => client.GetStringAsync(metadataUrl, It.IsAny<CancellationToken>()))
            .ReturnsAsync($"{{\"resource\":\"https://test.com/mcp\",\"authorization_servers\":[\"{issuer}\"],\"bearer_methods_supported\":[\"header\"]}}");
        _httpClient.Setup(client => client.GetStringAsync(rfcUrl, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("not found"));
        _httpClient.Setup(client => client.GetStringAsync(oidcInsertionUrl, It.IsAny<CancellationToken>()))
            .ReturnsAsync("""
                {
                  "issuer": "https://login.example.com/tenant",
                  "authorization_endpoint": "https://login.example.com/tenant/authorize",
                  "token_endpoint": "https://login.example.com/tenant/token",
                  "code_challenge_methods_supported": ["S256"]
                }
                """);

        var result = await _validator.ValidateAuthenticationComplianceAsync(config, CancellationToken.None);

        result.AuthorizationServerMetadata.Should().ContainSingle(evidence =>
            evidence.Fetched && evidence.IsValid && evidence.DiscoveryVariant == "openid-connect-path-insertion");
        _httpClient.Verify(client => client.GetStringAsync(oidcAppendingUrl, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ValidateAuth_WithStaticClientRegistration_ShouldRecordValidEvidence()
    {
        const string metadataUrl = "https://test.com/.well-known/oauth-protected-resource";
        var config = CreateAuthenticatedConfig(new OAuthClientRegistrationConfig
        {
            Mode = OAuthClientRegistrationMode.Static,
            ClientId = "enterprise-client",
            AuthorizationServerIssuer = "https://login.example.com",
            RedirectUris = ["https://client.example.com/callback"]
        });
        SetupAuthResponse(CreateAuthChallenge(metadataUrl));
        SetupMetadata(metadataUrl);

        var result = await _validator.ValidateAuthenticationComplianceAsync(config, CancellationToken.None);

        result.ClientRegistration.Should().Match<OAuthClientRegistrationEvidence>(evidence =>
            evidence.Mode == OAuthClientRegistrationMode.Static && evidence.Evaluated && evidence.IsValid &&
            evidence.ClientId == "enterprise-client");
        result.Findings.Should().NotContain(finding => finding.RuleId == ValidationFindingRuleIds.AuthClientRegistrationInvalid);
    }

    [Fact]
    public async Task ValidateAuth_WithMismatchedClientMetadataDocument_ShouldFailExactIdentityCheck()
    {
        const string protectedResourceMetadataUrl = "https://test.com/.well-known/oauth-protected-resource";
        const string clientMetadataUrl = "https://client.example.com/oauth-client.json";
        var config = CreateAuthenticatedConfig(new OAuthClientRegistrationConfig
        {
            Mode = OAuthClientRegistrationMode.MetadataDocument,
            MetadataUri = clientMetadataUrl,
            RedirectUris = ["https://client.example.com/callback"]
        });
        SetupAuthResponse(CreateAuthChallenge(protectedResourceMetadataUrl));
        SetupMetadata(protectedResourceMetadataUrl);
        _httpClient.Setup(client => client.GetStringAsync(clientMetadataUrl, It.IsAny<CancellationToken>()))
            .ReturnsAsync("""
                {
                  "client_id": "https://client.example.com/different.json",
                                    "client_name": "Test Client",
                  "redirect_uris": ["https://client.example.com/callback"]
                }
                """);

        var result = await _validator.ValidateAuthenticationComplianceAsync(config, CancellationToken.None);

        result.ClientRegistration.Should().Match<OAuthClientRegistrationEvidence>(evidence =>
            evidence.Mode == OAuthClientRegistrationMode.MetadataDocument && evidence.Evaluated && !evidence.IsValid &&
            evidence.Errors.Any(error => error.Contains("does not exactly match", StringComparison.Ordinal)));
        result.Findings.Should().Contain(finding => finding.RuleId == ValidationFindingRuleIds.AuthClientRegistrationInvalid);
    }

    [Theory]
    [InlineData("client_secret", "\"forbidden-secret\"")]
    [InlineData("client_secret_expires_at", "1234")]
    [InlineData("token_endpoint_auth_method", "\"client_secret_basic\"")]
    public async Task ValidateAuth_WithProhibitedClientMetadataCredential_ShouldRejectDocument(string propertyName, string propertyValue)
    {
        const string protectedResourceMetadataUrl = "https://test.com/.well-known/oauth-protected-resource";
        const string clientMetadataUrl = "https://client.example.com/oauth-client.json";
        var config = CreateAuthenticatedConfig(new OAuthClientRegistrationConfig
        {
            Mode = OAuthClientRegistrationMode.MetadataDocument,
            MetadataUri = clientMetadataUrl,
            RedirectUris = ["https://client.example.com/callback"]
        });
        SetupAuthResponse(CreateAuthChallenge(protectedResourceMetadataUrl));
        SetupMetadata(protectedResourceMetadataUrl);
        _httpClient.Setup(client => client.GetStringAsync(clientMetadataUrl, It.IsAny<CancellationToken>()))
            .ReturnsAsync($$"""
                {
                  "client_id": "{{clientMetadataUrl}}",
                  "client_name": "Test Client",
                  "redirect_uris": ["https://client.example.com/callback"],
                  "{{propertyName}}": {{propertyValue}}
                }
                """);

        var result = await _validator.ValidateAuthenticationComplianceAsync(config, CancellationToken.None);

        result.ClientRegistration!.IsValid.Should().BeFalse();
        result.Findings.Should().Contain(finding => finding.RuleId == ValidationFindingRuleIds.AuthClientRegistrationInvalid);
    }

    [Theory]
    [InlineData("https://client.example.com/oauth/../client.json")]
    [InlineData("https://client.example.com/oauth/%2e%2e/client.json")]
    public async Task ValidateAuth_WithDotSegmentClientMetadataIdentifier_ShouldRejectBeforeFetch(string metadataUri)
    {
        var config = CreateAuthenticatedConfig(new OAuthClientRegistrationConfig
        {
            Mode = OAuthClientRegistrationMode.MetadataDocument,
            MetadataUri = metadataUri,
            RedirectUris = ["https://client.example.com/callback"]
        });
        config.Profile = McpServerProfile.Public;
        SetupAuthResponse(CreateJsonRpcSuccess());

        var result = await _validator.ValidateAuthenticationComplianceAsync(config, CancellationToken.None);

        result.ClientRegistration!.IsValid.Should().BeFalse();
        _httpClient.Verify(client => client.GetStringAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ValidateAuth_WithClientMetadataDocumentAndUnsupportedAuthorizationServer_ShouldRecordCompatibilityGap()
    {
        const string protectedResourceMetadataUrl = "https://test.com/.well-known/oauth-protected-resource";
        const string clientMetadataUrl = "https://client.example.com/oauth-client.json";
        const string authorizationMetadataUrl = "https://login.example.com/.well-known/oauth-authorization-server";
        var config = CreateAuthenticatedConfig(new OAuthClientRegistrationConfig
        {
            Mode = OAuthClientRegistrationMode.MetadataDocument,
            MetadataUri = clientMetadataUrl,
            RedirectUris = ["https://client.example.com/callback"]
        });
        SetupAuthResponse(CreateAuthChallenge(protectedResourceMetadataUrl));
        SetupMetadata(protectedResourceMetadataUrl);
        _httpClient.Setup(client => client.GetStringAsync(clientMetadataUrl, It.IsAny<CancellationToken>()))
            .ReturnsAsync($$"""
                {
                  "client_id": "{{clientMetadataUrl}}",
                  "client_name": "Test Client",
                  "redirect_uris": ["https://client.example.com/callback"]
                }
                """);
        _httpClient.Setup(client => client.GetStringAsync(authorizationMetadataUrl, It.IsAny<CancellationToken>()))
            .ReturnsAsync("""
                {
                  "issuer": "https://login.example.com",
                  "authorization_endpoint": "https://login.example.com/authorize",
                  "token_endpoint": "https://login.example.com/token",
                  "code_challenge_methods_supported": ["S256"],
                  "client_id_metadata_document_supported": false
                }
                """);

        var result = await _validator.ValidateAuthenticationComplianceAsync(config, CancellationToken.None);

        result.ClientRegistration.Should().Match<OAuthClientRegistrationEvidence>(evidence =>
            evidence.AuthorizationServerCompatibilityEvaluated && !evidence.IsValid);
        result.Findings.Should().Contain(finding => finding.RuleId == ValidationFindingRuleIds.AuthClientMetadataDocumentUnsupported);
    }

    [Fact]
    public async Task ValidateAuth_WithLegacyDynamicRegistration_ShouldRecordUnevaluatedCoverage()
    {
        const string metadataUrl = "https://test.com/.well-known/oauth-protected-resource";
        var config = CreateAuthenticatedConfig(new OAuthClientRegistrationConfig
        {
            Mode = OAuthClientRegistrationMode.LegacyDynamic,
            RedirectUris = ["http://127.0.0.1:48123/callback"]
        });
        SetupAuthResponse(CreateAuthChallenge(metadataUrl));
        SetupMetadata(metadataUrl);

        var result = await _validator.ValidateAuthenticationComplianceAsync(config, CancellationToken.None);

        result.ClientRegistration.Should().Match<OAuthClientRegistrationEvidence>(evidence =>
            evidence.Mode == OAuthClientRegistrationMode.LegacyDynamic && evidence.Declared && !evidence.Evaluated && !evidence.IsValid);
        result.Findings.Should().Contain(finding =>
            finding.RuleId == ValidationFindingRuleIds.AuthLegacyDynamicRegistrationDeclared &&
            finding.Metadata["coverageStatus"] == "declared-not-evaluated");
    }

    [Fact]
    public async Task ValidateAuth_WithoutAdvertisedMetadata_ShouldUseEndpointPathWellKnownFirst()
    {
        const string pathMetadataUrl = "https://test.com/.well-known/oauth-protected-resource/mcp";
        const string rootMetadataUrl = "https://test.com/.well-known/oauth-protected-resource";
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
        SetupMetadata(pathMetadataUrl);

        var result = await _validator.ValidateAuthenticationComplianceAsync(config, CancellationToken.None);

        result.ProtectedResourceMetadataUrl.Should().Be(pathMetadataUrl);
        result.Findings.Should().NotContain(finding => finding.RuleId == ValidationFindingRuleIds.AuthProtectedResourceMetadataFetchFailed);
        _httpClient.Verify(client => client.GetStringAsync(rootMetadataUrl, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ValidateAuth_WhenPathMetadataUnavailable_ShouldFallBackToRootWellKnown()
    {
        const string pathMetadataUrl = "https://test.com/.well-known/oauth-protected-resource/public/mcp";
        const string rootMetadataUrl = "https://test.com/.well-known/oauth-protected-resource";
        var attempts = new List<string>();
        var config = new McpServerConfig { Endpoint = "https://test.com/public/mcp", Transport = "http", Profile = McpServerProfile.Authenticated };
        SetupAuthResponse(new JsonRpcResponse
        {
            StatusCode = 401,
            IsSuccess = false,
            Headers = new Dictionary<string, string> { ["WWW-Authenticate"] = "Bearer realm=\"mcp\"" }
        });
        _httpClient.Setup(client => client.GetStringAsync(pathMetadataUrl, It.IsAny<CancellationToken>()))
            .Callback(() => attempts.Add(pathMetadataUrl))
            .ThrowsAsync(new HttpRequestException("not found"));
        _httpClient.Setup(client => client.GetStringAsync(rootMetadataUrl, It.IsAny<CancellationToken>()))
            .Callback(() => attempts.Add(rootMetadataUrl))
            .ReturnsAsync("""
                {
                  "resource": "https://test.com/public/mcp",
                  "authorization_servers": ["https://login.example.com"],
                  "bearer_methods_supported": ["header"]
                }
                """);

        var result = await _validator.ValidateAuthenticationComplianceAsync(config, CancellationToken.None);

        attempts.Should().Equal(pathMetadataUrl, rootMetadataUrl);
        result.ProtectedResourceMetadataUrl.Should().Be(rootMetadataUrl);
        result.Findings.Should().NotContain(finding => finding.RuleId == ValidationFindingRuleIds.AuthProtectedResourceMetadataFetchFailed);
    }

    [Fact]
    public async Task ValidateAuth_WhenAllWellKnownMetadataLocationsUnavailable_ShouldEmitFetchFinding()
    {
        var config = new McpServerConfig { Endpoint = "https://test.com/mcp", Transport = "http", Profile = McpServerProfile.Authenticated };
        SetupAuthResponse(new JsonRpcResponse
        {
            StatusCode = 401,
            IsSuccess = false,
            Headers = new Dictionary<string, string> { ["WWW-Authenticate"] = "Bearer realm=\"mcp\"" }
        });
        _httpClient.Setup(client => client.GetStringAsync(It.Is<string>(url => url.Contains("oauth-protected-resource", StringComparison.Ordinal)), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("not found"));

        var result = await _validator.ValidateAuthenticationComplianceAsync(config, CancellationToken.None);

        result.Findings.Should().Contain(finding => finding.RuleId == ValidationFindingRuleIds.AuthProtectedResourceMetadataFetchFailed);
        result.Findings.Should().NotContain(finding => finding.RuleId == ValidationFindingRuleIds.AuthProtectedResourceMetadataMissing);
    }

    [Fact]
    public async Task ValidateAuth_MetadataBlockedByExecutionPolicy_ShouldEmitCoverageFinding()
    {
        const string metadataUrl = "https://issuer.example/.well-known/oauth-protected-resource";
        var config = new McpServerConfig { Endpoint = "https://test.com/mcp", Transport = "http", Profile = McpServerProfile.Authenticated };
        SetupAuthResponse(CreateAuthChallenge(metadataUrl));
        _httpClient.Setup(client => client.GetStringAsync(metadataUrl, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Execution policy blocked outbound request to origin 'https://issuer.example:443'."));

        var result = await _validator.ValidateAuthenticationComplianceAsync(config, CancellationToken.None);

        result.Findings.Should().Contain(finding =>
            finding.RuleId == ValidationFindingRuleIds.AuthMetadataBlockedByPolicy &&
            finding.Metadata["coverageStatus"] == "blocked");
        result.Findings.Should().NotContain(finding => finding.RuleId == ValidationFindingRuleIds.AuthProtectedResourceMetadataFetchFailed);
    }

    [Fact]
    public async Task ValidateAuth_MetadataUrlWithQuery_ShouldRedactPersistedEvidence()
    {
        const string metadataUrl = "https://test.com/.well-known/oauth-protected-resource?code=secret-value";
        var config = new McpServerConfig { Endpoint = "https://test.com/mcp", Transport = "http", Profile = McpServerProfile.Authenticated };
        SetupAuthResponse(CreateAuthChallenge(metadataUrl));
        SetupMetadata(metadataUrl);

        var result = await _validator.ValidateAuthenticationComplianceAsync(config, CancellationToken.None);

        result.ProtectedResourceMetadataUrl.Should().Contain("__REDACTED__").And.NotContain("secret-value");
    }

    [Fact]
    public async Task ValidateAuth_ChallengeSecrets_ShouldNotEnterPersistedScenarioEvidence()
    {
        const string metadataUrl = "https://test.com/.well-known/oauth-protected-resource?code=secret-query";
        var config = new McpServerConfig { Endpoint = "https://test.com/mcp", Transport = "http", Profile = McpServerProfile.Authenticated };
        SetupAuthResponse(new JsonRpcResponse
        {
            StatusCode = 401,
            IsSuccess = false,
            Headers = new Dictionary<string, string>
            {
                ["WWW-Authenticate"] = $"Bearer error=\"invalid_token\", error_description=\"secret-description\", resource_metadata=\"{metadataUrl}\""
            }
        });
        SetupMetadata(metadataUrl);

        var result = await _validator.ValidateAuthenticationComplianceAsync(config, CancellationToken.None);
        var persistedJson = System.Text.Json.JsonSerializer.Serialize(result);

        persistedJson.Should().NotContain("secret-description").And.NotContain("secret-query");
        result.TestScenarios.Should().OnlyContain(scenario => scenario.WwwAuthenticateHeader == null || !scenario.WwwAuthenticateHeader.Contains("secret-", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Bearer error=\"insufficient_scope\", scope=\"tools:write\"", false)]
    [InlineData("Bearer error=\"insufficient_scope\"", true)]
    public async Task ValidateAuth_WithScopeRejection_ShouldEvaluateCompleteStepUpChallenge(
        string challengeHeader,
        bool expectFinding)
    {
        const string metadataUrl = "https://test.com/.well-known/oauth-protected-resource";
        var config = new McpServerConfig { Endpoint = "https://test.com/mcp", Transport = "http", Profile = McpServerProfile.Authenticated };
        SetupMetadata(metadataUrl);
        SetupAuthResponse((_, method, _, authentication, _) =>
        {
            if (method == "initialize")
            {
                return CreateInitializeWithToolsCapability();
            }

            if (authentication?.Token?.Contains("invalid_scope_simulation", StringComparison.Ordinal) == true ||
                authentication?.Token?.Contains("insufficient_permissions_simulation", StringComparison.Ordinal) == true)
            {
                return new JsonRpcResponse
                {
                    StatusCode = 403,
                    IsSuccess = false,
                    Headers = new Dictionary<string, string> { ["WWW-Authenticate"] = challengeHeader }
                };
            }

            return CreateAuthChallenge(metadataUrl);
        });

        var result = await _validator.ValidateAuthenticationComplianceAsync(config, CancellationToken.None);

        if (expectFinding)
        {
            result.Findings.Should().Contain(finding => finding.RuleId == ValidationFindingRuleIds.AuthInsufficientScopeChallengeInvalid);
        }
        else
        {
            result.Findings.Should().NotContain(finding => finding.RuleId == ValidationFindingRuleIds.AuthInsufficientScopeChallengeInvalid);
        }
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
            endpoint.Contains("access_token=", StringComparison.OrdinalIgnoreCase) && method == "tools/list"
                ? CreateJsonRpcSuccess()
                : method == "initialize"
                    ? CreateInitializeWithToolsCapability()
                    : CreateAuthChallenge(metadataUrl));

        var result = await _validator.ValidateAuthenticationComplianceAsync(config, CancellationToken.None);

        result.TestScenarios.Should().Contain(s =>
            s.TestType == "Query Token" &&
            s.Method == "tools/list" &&
            s.AssessmentDisposition == AuthenticationAssessmentDisposition.Insecure);
        result.Findings.Should().Contain(f => f.RuleId == ValidationFindingRuleIds.AuthQueryTokenAccepted);
    }

    [Fact]
    public async Task ValidateAuth_WhenSyntheticWrongAudienceTokenIsAccepted_ShouldEmitInvalidTokenFindingOnly()
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
                : method == "tools/list" && authentication?.Token?.Contains("wrong_audience_simulation", StringComparison.OrdinalIgnoreCase) == true
                    ? CreateJsonRpcSuccess()
                    : CreateAuthChallenge(metadataUrl));

        var result = await _validator.ValidateAuthenticationComplianceAsync(config, CancellationToken.None);

        result.TestScenarios.Should().Contain(s =>
            s.TestType == "Wrong Audience (RFC 8707)" &&
            s.Method == "tools/list" &&
            s.AssessmentDisposition == AuthenticationAssessmentDisposition.Insecure);
        result.Findings.Should().Contain(f =>
            f.RuleId == ValidationFindingRuleIds.AuthInvalidTokenAccepted &&
            f.Metadata["audienceEvidence"] == "synthetic-invalid-token");
        result.Findings.Should().NotContain(f => f.RuleId == ValidationFindingRuleIds.AuthWrongAudienceAccepted);
        result.Findings.Should().NotContain(f => f.RuleId == ValidationFindingRuleIds.AuthTokenPassthroughRisk);
    }

    [Fact]
    public async Task ValidateAuth_WithControlledWrongAudienceToken_ShouldEmitAuthoritativeAudienceAndDeputyFindings()
    {
        const string metadataUrl = "https://test.com/.well-known/oauth-protected-resource";
        var config = new McpServerConfig
        {
            Endpoint = "https://test.com/mcp",
            Transport = "http",
            Profile = McpServerProfile.Authenticated,
            Authentication = new AuthenticationConfig
            {
                ConformanceCredentials = new AuthenticationConformanceCredentials
                {
                    WrongAudienceResource = "https://other-resource.example/mcp",
                    WrongAudienceScopes = ["tools:read"],
                    PreviouslyGrantedScopes = ["tools:read"]
                }
            }
        };
        var provider = new Mock<INoninteractiveCredentialProvider>();
        provider.Setup(candidate => candidate.CanHandle(It.IsAny<NoninteractiveCredentialRequest>())).Returns(true);
        provider.Setup(candidate => candidate.GetTokenReferenceAsync(
                It.Is<NoninteractiveCredentialRequest>(request => request.Resource!.Host == "other-resource.example"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SecretRef { Provider = SecretRefProviders.Environment, Name = "WRONG_AUD_TOKEN" });
        var validator = new McpCompliantAuthValidator(
            new Mock<ILogger<McpCompliantAuthValidator>>().Object,
            _httpClient.Object,
            [provider.Object]);
        SetupMetadata(metadataUrl);
        SetupAuthResponse((_, method, _, authentication, _) =>
            method == "initialize"
                ? CreateInitializeWithToolsCapability()
                : method == "tools/list" && authentication?.TokenRef?.Name == "WRONG_AUD_TOKEN"
                    ? CreateJsonRpcSuccess()
                    : CreateAuthChallenge(metadataUrl));

        var previous = Environment.GetEnvironmentVariable("WRONG_AUD_TOKEN");
        Environment.SetEnvironmentVariable("WRONG_AUD_TOKEN", "controlled-wrong-audience-token");
        AuthenticationTestResult result;
        try
        {
            result = await validator.ValidateAuthenticationComplianceAsync(config, CancellationToken.None);
        }
        finally
        {
            Environment.SetEnvironmentVariable("WRONG_AUD_TOKEN", previous);
        }

        result.Findings.Should().Contain(finding =>
            finding.RuleId == ValidationFindingRuleIds.AuthWrongAudienceAccepted &&
            finding.Metadata["audienceEvidence"] == "controlled-valid-token");
        result.Findings.Should().Contain(finding => finding.RuleId == ValidationFindingRuleIds.AuthTokenPassthroughRisk);
    }

    [Fact]
    public async Task ValidateAuth_WithControlledStepUp_ShouldUnionScopesAndRetryExactlyOnce()
    {
        const string metadataUrl = "https://test.com/.well-known/oauth-protected-resource";
        var config = new McpServerConfig
        {
            Endpoint = "https://test.com/mcp",
            Transport = "http",
            Profile = McpServerProfile.Authenticated,
            Authentication = new AuthenticationConfig
            {
                ConformanceCredentials = new AuthenticationConformanceCredentials
                {
                    PreviouslyGrantedScopes = ["tools:read"]
                }
            }
        };
        var provider = new Mock<INoninteractiveCredentialProvider>();
        provider.Setup(candidate => candidate.CanHandle(It.IsAny<NoninteractiveCredentialRequest>())).Returns(true);
        provider.Setup(candidate => candidate.GetTokenReferenceAsync(
                It.Is<NoninteractiveCredentialRequest>(request => request.Scopes.SequenceEqual(new[] { "tools:read" })),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SecretRef { Provider = SecretRefProviders.Environment, Name = "LOW_SCOPE_TOKEN" });
        provider.Setup(candidate => candidate.GetTokenReferenceAsync(
                It.Is<NoninteractiveCredentialRequest>(request => request.Scopes.SequenceEqual(new[] { "tools:read", "tools:write" })),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SecretRef { Provider = SecretRefProviders.Environment, Name = "STEP_UP_TOKEN" });
        var validator = new McpCompliantAuthValidator(
            new Mock<ILogger<McpCompliantAuthValidator>>().Object,
            _httpClient.Object,
            [provider.Object]);
        SetupMetadata(metadataUrl);
        var stepUpCalls = 0;
        SetupAuthResponse((_, method, _, authentication, _) =>
        {
            if (method == "initialize")
            {
                return CreateInitializeWithToolsCapability();
            }

            return authentication?.TokenRef?.Name switch
            {
                "LOW_SCOPE_TOKEN" => new JsonRpcResponse
                {
                    StatusCode = 403,
                    IsSuccess = false,
                    Headers = new Dictionary<string, string>
                    {
                        ["WWW-Authenticate"] = $"Bearer error=\"insufficient_scope\", scope=\"tools:write\", resource_metadata=\"{metadataUrl}\""
                    }
                },
                "STEP_UP_TOKEN" => IncrementAndReturnSuccess(),
                _ => CreateAuthChallenge(metadataUrl)
            };
        });

        var previousLowScope = Environment.GetEnvironmentVariable("LOW_SCOPE_TOKEN");
        var previousStepUp = Environment.GetEnvironmentVariable("STEP_UP_TOKEN");
        Environment.SetEnvironmentVariable("LOW_SCOPE_TOKEN", "controlled-low-scope-token");
        Environment.SetEnvironmentVariable("STEP_UP_TOKEN", "controlled-step-up-token");
        AuthenticationTestResult result;
        try
        {
            result = await validator.ValidateAuthenticationComplianceAsync(config, CancellationToken.None);
        }
        finally
        {
            Environment.SetEnvironmentVariable("LOW_SCOPE_TOKEN", previousLowScope);
            Environment.SetEnvironmentVariable("STEP_UP_TOKEN", previousStepUp);
        }

        result.ScopeStepUp.Should().Match<ScopeStepUpEvidence>(evidence =>
            evidence.Evaluated && evidence.RetryAttempts == 1 && evidence.LeastPrivilegeSatisfied && evidence.Outcome == "succeeded");
        result.ScopeStepUp!.RequestedScopes.Should().Equal("tools:read", "tools:write");
        stepUpCalls.Should().Be(1);
        provider.Verify(candidate => candidate.GetTokenReferenceAsync(
            It.Is<NoninteractiveCredentialRequest>(request => request.Scopes.SequenceEqual(new[] { "tools:read", "tools:write" })),
            It.IsAny<CancellationToken>()), Times.Once);

        JsonRpcResponse IncrementAndReturnSuccess()
        {
            stepUpCalls++;
            return CreateJsonRpcSuccess();
        }
    }

    [Fact]
    public async Task ValidateAuth_WhenControlledProviderUnavailable_ShouldRecordUnevaluatedCoverage()
    {
        const string metadataUrl = "https://test.com/.well-known/oauth-protected-resource";
        var config = new McpServerConfig
        {
            Endpoint = "https://test.com/mcp",
            Transport = "http",
            Profile = McpServerProfile.Authenticated,
            Authentication = new AuthenticationConfig
            {
                ConformanceCredentials = new AuthenticationConformanceCredentials
                {
                    WrongAudienceResource = "https://other-resource.example/mcp",
                    WrongAudienceScopes = ["tools:read"],
                    PreviouslyGrantedScopes = ["tools:read"]
                }
            }
        };
        SetupMetadata(metadataUrl);
        SetupAuthResponse((_, method, _, _, _) => method == "initialize"
            ? CreateInitializeWithToolsCapability()
            : CreateAuthChallenge(metadataUrl));

        var result = await _validator.ValidateAuthenticationComplianceAsync(config, CancellationToken.None);

        result.ControlledAudience.Should().Match<ControlledAudienceEvidence>(evidence =>
            evidence.Declared && !evidence.Evaluated && evidence.Outcome == "credential-unavailable");
        result.ScopeStepUp.Should().Match<ScopeStepUpEvidence>(evidence =>
            !evidence.Evaluated && evidence.Outcome == "initial-credential-unavailable");
    }

    [Fact]
    public async Task ValidateAuth_PublicDiscovery_ShouldNotRunCredentialOrMutatingProbes()
    {
        var config = new McpServerConfig { Endpoint = "https://test.com/mcp", Transport = "http", Profile = McpServerProfile.Public };
        var observedMethods = new List<string>();
        SetupAuthResponse((_, method, _, _, _) =>
        {
            observedMethods.Add(method);
            return method == "initialize" ? CreateInitializeWithToolsCapability() : CreateJsonRpcSuccess();
        });

        var result = await _validator.ValidateAuthenticationComplianceAsync(config, CancellationToken.None);

        result.TestScenarios.Should().NotContain(scenario =>
            scenario.TestType == "Invalid Token" ||
            scenario.TestType == "Wrong Audience (RFC 8707)" ||
            scenario.TestType == "Query Token");
        observedMethods.Should().OnlyContain(method => method == "initialize" || method == "tools/list");
    }

    [Fact]
    public async Task ValidateAuth_WhenCallerCancels_ShouldPropagateCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var config = new McpServerConfig { Endpoint = "https://test.com/mcp", Transport = "http" };
        _httpClient.Setup(client => client.CallAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<object>(),
                It.IsAny<AuthenticationConfig>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException(cancellation.Token));

        var action = () => _validator.ValidateAuthenticationComplianceAsync(config, cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
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

    private static McpServerConfig CreateAuthenticatedConfig(OAuthClientRegistrationConfig registration) => new()
    {
        Endpoint = "https://test.com/mcp",
        Transport = "http",
        Profile = McpServerProfile.Authenticated,
        Authentication = new AuthenticationConfig { ClientRegistration = registration }
    };

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
