using System.Security.Cryptography;
using System.Text;
using Mcp.Benchmark.Infrastructure.Authentication;

namespace Mcp.Benchmark.Tests.Unit.Authentication;

public sealed class OAuthAuthorizationTransactionTests
{
    [Fact]
    public void Create_ShouldEmitStatePkceS256ExactRedirectAndResource()
    {
        var redirectUri = new Uri("http://127.0.0.1:48123/callback");
        var transaction = OAuthAuthorizationTransaction.Create(
            new Uri("https://login.example.test/authorize"),
            "client-id",
            redirectUri,
            ["tools:read", "resources:read"],
            new Uri("https://mcp.example.test/mcp"));
        var query = ParseQuery(transaction.AuthorizationUri);

        query["response_type"].Should().Be("code");
        query["client_id"].Should().Be("client-id");
        query["redirect_uri"].Should().Be(redirectUri.AbsoluteUri);
        query["scope"].Should().Be("resources:read tools:read");
        query["resource"].Should().Be("https://mcp.example.test/mcp");
        query["state"].Should().NotBeNullOrWhiteSpace();
        query["code_challenge_method"].Should().Be("S256");
        query["code_challenge"].Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void ValidateAndConsumeCallback_MatchingStateAndRedirect_ShouldReleaseVerifierOnce()
    {
        var redirectUri = new Uri("https://client.example.test/callback");
        var transaction = OAuthAuthorizationTransaction.Create(
            new Uri("https://login.example.test/authorize"),
            "client-id",
            redirectUri,
            ["tools:read"]);
        var query = ParseQuery(transaction.AuthorizationUri);

        var result = transaction.ValidateAndConsumeCallback(redirectUri, query["state"]);
        var replay = transaction.ValidateAndConsumeCallback(redirectUri, query["state"]);

        result.IsValid.Should().BeTrue();
        result.CodeVerifier.Should().NotBeNullOrWhiteSpace();
        Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(result.CodeVerifier!)))
            .Should().Be(query["code_challenge"]);
        replay.Status.Should().Be(OAuthCallbackValidationStatus.AlreadyConsumed);
        replay.CodeVerifier.Should().BeNull();
    }

    [Theory]
    [InlineData("https://client.example.test/callback/other", true, 1)]
    [InlineData("https://client.example.test/callback", false, 2)]
    public void ValidateAndConsumeCallback_Mismatch_ShouldFailClosed(
        string callbackUri,
        bool useCorrectState,
        int expected)
    {
        var redirectUri = new Uri("https://client.example.test/callback");
        var transaction = OAuthAuthorizationTransaction.Create(
            new Uri("https://login.example.test/authorize"),
            "client-id",
            redirectUri,
            ["tools:read"]);
        var expectedState = ParseQuery(transaction.AuthorizationUri)["state"];

        var result = transaction.ValidateAndConsumeCallback(
            new Uri(callbackUri),
            useCorrectState ? expectedState : "wrong-state");

        result.Status.Should().Be((OAuthCallbackValidationStatus)expected);
        result.CodeVerifier.Should().BeNull();
    }

    [Fact]
    public void ValidateAndConsumeCallback_ExpiredTransaction_ShouldFailClosed()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.Parse("2026-08-19T00:00:00Z"));
        var redirectUri = new Uri("https://client.example.test/callback");
        var transaction = OAuthAuthorizationTransaction.Create(
            new Uri("https://login.example.test/authorize"),
            "client-id",
            redirectUri,
            ["tools:read"],
            lifetime: TimeSpan.FromMinutes(1),
            timeProvider: clock);
        var state = ParseQuery(transaction.AuthorizationUri)["state"];
        clock.Advance(TimeSpan.FromMinutes(2));

        var result = transaction.ValidateAndConsumeCallback(redirectUri, state);

        result.Status.Should().Be(OAuthCallbackValidationStatus.Expired);
        result.CodeVerifier.Should().BeNull();
    }

    [Fact]
    public void Create_NonLoopbackHttpRedirect_ShouldReject()
    {
        var action = () => OAuthAuthorizationTransaction.Create(
            new Uri("https://login.example.test/authorize"),
            "client-id",
            new Uri("http://client.example.test/callback"),
            ["tools:read"]);

        action.Should().Throw<ArgumentException>().WithMessage("*redirect URI must use HTTPS*");
    }

    [Fact]
    public void Create_ShouldPreserveAuthorizationEndpointQueryFormSemantics()
    {
        var transaction = OAuthAuthorizationTransaction.Create(
            new Uri("https://login.example.test/authorize?tenant=alpha+beta"),
            "client-id",
            new Uri("https://client.example.test/callback"),
            ["tools:read"]);

        transaction.AuthorizationUri.Query.Should().Contain("tenant=alpha+beta").And.NotContain("tenant=alpha%2Bbeta");
    }

    [Fact]
    public void Create_WhenEndpointPredeterminesSecurityParameter_ShouldReject()
    {
        var action = () => OAuthAuthorizationTransaction.Create(
            new Uri("https://login.example.test/authorize?state=attacker"),
            "client-id",
            new Uri("https://client.example.test/callback"),
            ["tools:read"]);

        action.Should().Throw<ArgumentException>().WithMessage("*must not predefine generated OAuth parameters*");
    }

    private static Dictionary<string, string> ParseQuery(Uri uri)
    {
        return uri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .ToDictionary(
                part => Uri.UnescapeDataString(part[0]),
                part => Uri.UnescapeDataString(part[1]),
                StringComparer.Ordinal);
    }

    private static string Base64UrlEncode(ReadOnlySpan<byte> value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration) => _now = _now.Add(duration);
    }
}
