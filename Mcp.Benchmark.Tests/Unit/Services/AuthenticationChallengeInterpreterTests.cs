using FluentAssertions;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Services;

namespace Mcp.Benchmark.Tests.Unit.Services;

public class AuthenticationChallengeInterpreterTests
{
    [Fact]
    public void Inspect_ShouldParseCaseInsensitiveChallengeMetadata()
    {
        var response = new JsonRpcResponse
        {
            StatusCode = 401,
            ElapsedMs = 42.5,
            Headers = new Dictionary<string, string>
            {
                ["www-authenticate"] = "Bearer resource_metadata=\"https://example.com/.well-known/oauth-protected-resource\", authorization_uri=\"https://login.example.com/oauth2/authorize\""
            }
        };

        var observation = AuthenticationChallengeInterpreter.Inspect(response);

        observation.RequiresAuthentication.Should().BeTrue();
        observation.IsAuthenticationChallenge.Should().BeTrue();
        observation.HasWwwAuthenticateHeader.Should().BeTrue();
        observation.WwwAuthenticateHeader.Should().Contain("Bearer");
        observation.ResourceMetadataUrl.Should().Be("https://example.com/.well-known/oauth-protected-resource");
        observation.AuthorizationUri.Should().Be("https://login.example.com/oauth2/authorize");
        observation.UsesBearerChallenge.Should().BeTrue();
        observation.DurationMs.Should().Be(42.5);
        observation.SecurityScore.Should().Be(100.0);
    }

    [Fact]
    public void Inspect_ShouldParseOauthChallengeErrorScopeAndRealmParameters()
    {
        var response = new JsonRpcResponse
        {
            StatusCode = 401,
            Headers = new Dictionary<string, string>
            {
                ["WWW-Authenticate"] = "Bearer realm=\"mcp\", error=\"invalid_token\", error_description=\"expired\", scope=\"tools:read\""
            }
        };

        var observation = AuthenticationChallengeInterpreter.Inspect(response);

        observation.Realm.Should().Be("mcp");
        observation.Error.Should().Be("invalid_token");
        observation.ErrorDescription.Should().Be("expired");
        observation.Scope.Should().Be("tools:read");
    }

    [Fact]
    public void CreateSecurityResult_ShouldPreserveDiscoveryMetadataAndDeduplicateIssues()
    {
        var metadata = new AuthMetadata
        {
            AuthorizationServers = new List<string> { "https://login.example.com/oauth2/authorize" },
            ScopesSupported = new List<string> { "default" }
        };

        var discovery = new AuthDiscoveryInfo
        {
            WwwAuthenticateHeader = "Bearer",
            Metadata = metadata,
            DiscoveryTimeMs = 12.5,
            Issues = new List<string> { "challenge observed", "challenge observed" }
        };

        var result = AuthenticationChallengeInterpreter.CreateSecurityResult(discovery);

        result.Should().NotBeNull();
        result!.AuthenticationRequired.Should().BeTrue();
        result.HasProperAuthHeaders.Should().BeTrue();
        result.AuthMetadata.Should().BeSameAs(metadata);
        result.ChallengeDurationMs.Should().Be(12.5);
        result.Findings.Should().ContainSingle().Which.Should().Be("challenge observed");
    }

    [Fact]
    public void Inspect_WithBare403_ShouldRequireAuthenticationWithoutClaimingChallenge()
    {
        var response = new JsonRpcResponse
        {
            StatusCode = 403,
            Headers = new Dictionary<string, string>()
        };

        var observation = AuthenticationChallengeInterpreter.Inspect(response);
        var discovery = AuthenticationChallengeInterpreter.CreateDiscoveryInfo(observation);
        var security = AuthenticationChallengeInterpreter.CreateSecurityResult(discovery);

        observation.RequiresAuthentication.Should().BeTrue();
        observation.IsAuthenticationChallenge.Should().BeFalse();
        observation.IsBareAuthenticationRejection.Should().BeTrue();
        observation.HasWwwAuthenticateHeader.Should().BeFalse();
        observation.SecurityScore.Should().Be(85.0);
        discovery.Should().NotBeNull();
        discovery!.Issues.Should().Contain(note => note.Contains("no WWW-Authenticate challenge", StringComparison.OrdinalIgnoreCase));
        security.Should().NotBeNull();
        security!.AuthenticationRequired.Should().BeTrue();
        security.HasProperAuthHeaders.Should().BeFalse();
    }

    [Theory]
    [InlineData("Digest realm=\"legacy\", Bearer error=\"insufficient_scope\", scope=\"tools:read resources:read\"", true, true, "tools:read resources:read")]
    [InlineData("Bearer error_description=\"expired, retry\", scope=\"tools:\\\"read\"", true, true, "tools:\"read")]
    [InlineData("Bearer not_scope=\"admin\"", true, true, null)]
    [InlineData("NotBearer scope=\"admin\"", true, false, null)]
    [InlineData("Bearer scope=\"read\", scope=\"write\"", false, false, null)]
    [InlineData("Bearer scope=\"unterminated", false, false, null)]
    [InlineData("Bearer error=\"insufficient_scope\", scope = \"read\"", true, true, "read")]
    [InlineData("Bearer realm=foo/bar", false, false, null)]
    public void Inspect_ShouldParseOnlyUnambiguousBearerChallengeParameters(
        string header,
        bool syntaxValid,
        bool usesBearer,
        string? expectedScope)
    {
        var observation = AuthenticationChallengeInterpreter.Inspect(new JsonRpcResponse
        {
            StatusCode = 403,
            Headers = new Dictionary<string, string> { ["WWW-Authenticate"] = header }
        });

        observation.ChallengeSyntaxValid.Should().Be(syntaxValid);
        observation.UsesBearerChallenge.Should().Be(usesBearer);
        observation.Scope.Should().Be(expectedScope);
    }

    [Fact]
    public void Inspect_OversizedChallenge_ShouldRejectWithoutParsingEvidence()
    {
        var observation = AuthenticationChallengeInterpreter.Inspect(new JsonRpcResponse
        {
            StatusCode = 401,
            Headers = new Dictionary<string, string>
            {
                ["WWW-Authenticate"] = $"Bearer scope=\"{new string('a', 20_000)}\""
            }
        });

        observation.ChallengeSyntaxValid.Should().BeFalse();
        observation.Scope.Should().BeNull();
    }

    [Fact]
    public void CreateDiscoveryInfo_ShouldPersistOnlySanitizedChallengeEvidence()
    {
        var observation = AuthenticationChallengeInterpreter.Inspect(new JsonRpcResponse
        {
            StatusCode = 401,
            Headers = new Dictionary<string, string>
            {
                ["WWW-Authenticate"] = "Bearer error_description=\"secret-description\", resource_metadata=\"https://example.test/metadata?code=secret-query\""
            }
        });

        var discovery = AuthenticationChallengeInterpreter.CreateDiscoveryInfo(observation);
        var security = AuthenticationChallengeInterpreter.CreateSecurityResult(discovery);

        discovery!.WwwAuthenticateHeader.Should().Contain("[REDACTED]").And.NotContain("secret-description").And.NotContain("secret-query");
        security!.WwwAuthenticateHeader.Should().NotContain("secret-description").And.NotContain("secret-query");
    }
}