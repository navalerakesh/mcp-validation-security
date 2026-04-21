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
}