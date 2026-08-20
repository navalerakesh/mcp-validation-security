using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Infrastructure.Authentication;
using Moq;

namespace Mcp.Benchmark.Tests.Unit.Authentication;

public sealed class AuthorizationCodeFlowCoordinatorTests
{
    [Fact]
    public async Task CompleteAsync_ValidCallback_ShouldExchangeOnceAndReturnSecretReference()
    {
        var provider = new Mock<IAuthorizationCodeTokenExchangeProvider>(MockBehavior.Strict);
        provider.SetupGet(candidate => candidate.ProviderName).Returns("test");
        provider.Setup(candidate => candidate.CanHandle(new Uri("https://login.example.test/token"))).Returns(true);
        AuthorizationCodeTokenExchangeRequest? exchangeRequest = null;
        provider.Setup(candidate => candidate.ExchangeAsync(It.IsAny<AuthorizationCodeTokenExchangeRequest>(), It.IsAny<CancellationToken>()))
            .Callback<AuthorizationCodeTokenExchangeRequest, CancellationToken>((request, _) => exchangeRequest = request)
            .ReturnsAsync(new SecretRef { Provider = SecretRefProviders.Environment, Name = "MCPVAL_TOKEN" });
        var coordinator = new AuthorizationCodeFlowCoordinator([provider.Object]);
        var request = CreateRequest();
        var challenge = coordinator.Begin(request);
        var state = ParseQuery(challenge.AuthorizationUri)["state"];

        var result = await coordinator.CompleteAsync(
            challenge.TransactionId,
            request.RedirectUri,
            state,
            "authorization-code",
            CancellationToken.None);
        var replay = await coordinator.CompleteAsync(
            challenge.TransactionId,
            request.RedirectUri,
            state,
            "authorization-code",
            CancellationToken.None);

        result.IsSuccessful.Should().BeTrue();
        result.TokenReference.Should().BeEquivalentTo(new SecretRef { Provider = SecretRefProviders.Environment, Name = "MCPVAL_TOKEN" });
        exchangeRequest.Should().NotBeNull();
        exchangeRequest!.AuthorizationCode.Should().Be("authorization-code");
        exchangeRequest.CodeVerifier.Should().NotBeNullOrWhiteSpace();
        replay.Status.Should().Be(AuthorizationCodeCompletionStatus.UnknownTransaction);
        provider.Verify(candidate => candidate.ExchangeAsync(It.IsAny<AuthorizationCodeTokenExchangeRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CompleteAsync_InvalidState_ShouldNotReachTokenExchangeProvider()
    {
        var provider = CreateProvider();
        var coordinator = new AuthorizationCodeFlowCoordinator([provider.Object]);
        var request = CreateRequest();
        var challenge = coordinator.Begin(request);

        var result = await coordinator.CompleteAsync(
            challenge.TransactionId,
            request.RedirectUri,
            "wrong-state",
            "authorization-code",
            CancellationToken.None);

        result.Status.Should().Be(AuthorizationCodeCompletionStatus.InvalidState);
        result.TokenReference.Should().BeNull();
        provider.Verify(candidate => candidate.ExchangeAsync(It.IsAny<AuthorizationCodeTokenExchangeRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CompleteAsync_ExpiredTransaction_ShouldNotReachTokenExchangeProvider()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.Parse("2026-08-19T00:00:00Z"));
        var provider = CreateProvider();
        var coordinator = new AuthorizationCodeFlowCoordinator([provider.Object], clock);
        var request = CreateRequest();
        var challenge = coordinator.Begin(request);
        var state = ParseQuery(challenge.AuthorizationUri)["state"];
        clock.Advance(TimeSpan.FromMinutes(11));

        var result = await coordinator.CompleteAsync(
            challenge.TransactionId,
            request.RedirectUri,
            state,
            "authorization-code",
            CancellationToken.None);

        result.Status.Should().Be(AuthorizationCodeCompletionStatus.Expired);
        provider.Verify(candidate => candidate.ExchangeAsync(It.IsAny<AuthorizationCodeTokenExchangeRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(false, "https://login.example.test/", AuthorizationCodeCompletionStatus.IssuerMismatch)]
    [InlineData(true, null, AuthorizationCodeCompletionStatus.MissingIssuer)]
    public async Task CompleteAsync_InvalidAuthorizationResponseIssuer_ShouldNotReachTokenExchangeProvider(
        bool issuerParameterSupported,
        string? returnedIssuer,
        AuthorizationCodeCompletionStatus expectedStatus)
    {
        var provider = CreateProvider();
        var coordinator = new AuthorizationCodeFlowCoordinator([provider.Object]);
        var request = CreateRequest(issuerParameterSupported);
        var challenge = coordinator.Begin(request);
        var state = ParseQuery(challenge.AuthorizationUri)["state"];

        var result = await coordinator.CompleteAsync(
            challenge.TransactionId,
            request.RedirectUri,
            state,
            returnedIssuer,
            "authorization-code",
            CancellationToken.None);

        result.Status.Should().Be(expectedStatus);
        result.TokenReference.Should().BeNull();
        provider.Verify(candidate => candidate.ExchangeAsync(It.IsAny<AuthorizationCodeTokenExchangeRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void Begin_WithoutMatchingProvider_ShouldFailBeforeAllocatingTransaction()
    {
        var provider = new Mock<IAuthorizationCodeTokenExchangeProvider>();
        provider.Setup(candidate => candidate.CanHandle(It.IsAny<Uri>())).Returns(false);
        var coordinator = new AuthorizationCodeFlowCoordinator([provider.Object]);

        var action = () => coordinator.Begin(CreateRequest());

        action.Should().Throw<InvalidOperationException>().WithMessage("*No authorization-code token exchange provider*");
    }

    [Fact]
    public void TokenExchangeRequest_ToString_ShouldRedactTransientSecrets()
    {
        var request = new AuthorizationCodeTokenExchangeRequest
        {
            TokenEndpoint = new Uri("https://login.example.test/token"),
            ClientId = "client-id",
            RedirectUri = new Uri("https://client.example.test/callback"),
            AuthorizationCode = "secret-code",
            CodeVerifier = "secret-verifier"
        };

        request.ToString().Should().NotContain("secret-code").And.NotContain("secret-verifier").And.Contain("[REDACTED]");
    }

    [Fact]
    public void Challenge_ToString_ShouldNotExposeState()
    {
        var coordinator = new AuthorizationCodeFlowCoordinator([CreateProvider().Object]);
        var challenge = coordinator.Begin(CreateRequest());
        var state = ParseQuery(challenge.AuthorizationUri)["state"];

        challenge.ToString().Should().NotContain(state).And.NotContain(challenge.TransactionId).And.Contain("AuthorizationUri = [REDACTED]");
    }

    [Fact]
    public void Begin_ShouldBoundPendingTransactionsAndEvictExpiredEntries()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.Parse("2026-08-19T00:00:00Z"));
        var coordinator = new AuthorizationCodeFlowCoordinator([CreateProvider().Object], clock);
        for (var index = 0; index < 128; index++)
        {
            coordinator.Begin(CreateRequest());
        }

        var action = () => coordinator.Begin(CreateRequest());
        action.Should().Throw<InvalidOperationException>().WithMessage("*limit of 128*");

        clock.Advance(TimeSpan.FromMinutes(11));
        coordinator.Begin(CreateRequest()).Should().NotBeNull();
    }

    [Fact]
    public async Task CompleteAsync_ProviderFailure_ShouldNotExposeProviderMessage()
    {
        var provider = CreateProvider();
        provider.Setup(candidate => candidate.ExchangeAsync(It.IsAny<AuthorizationCodeTokenExchangeRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("secret-code=provider-sensitive-value"));
        var coordinator = new AuthorizationCodeFlowCoordinator([provider.Object]);
        var request = CreateRequest();
        var challenge = coordinator.Begin(request);
        var state = ParseQuery(challenge.AuthorizationUri)["state"];

        var result = await coordinator.CompleteAsync(
            challenge.TransactionId,
            request.RedirectUri,
            state,
            "authorization-code",
            CancellationToken.None);

        result.Status.Should().Be(AuthorizationCodeCompletionStatus.TokenExchangeFailed);
        result.Error.Should().Be("The token exchange provider failed.").And.NotContain("provider-sensitive-value");
    }

    [Fact]
    public async Task CompleteAsync_PreCancelled_ShouldNotConsumeTransaction()
    {
        var provider = CreateProvider();
        provider.Setup(candidate => candidate.ExchangeAsync(It.IsAny<AuthorizationCodeTokenExchangeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SecretRef { Name = "TOKEN" });
        var coordinator = new AuthorizationCodeFlowCoordinator([provider.Object]);
        var request = CreateRequest();
        var challenge = coordinator.Begin(request);
        var state = ParseQuery(challenge.AuthorizationUri)["state"];
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var cancelled = () => coordinator.CompleteAsync(
            challenge.TransactionId,
            request.RedirectUri,
            state,
            "authorization-code",
            cancellation.Token);
        await cancelled.Should().ThrowAsync<OperationCanceledException>();

        var retry = await coordinator.CompleteAsync(
            challenge.TransactionId,
            request.RedirectUri,
            state,
            "authorization-code",
            CancellationToken.None);
        retry.IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void Begin_ProviderSelectionFailure_ShouldSanitizeException()
    {
        var provider = new Mock<IAuthorizationCodeTokenExchangeProvider>();
        provider.Setup(candidate => candidate.CanHandle(It.IsAny<Uri>()))
            .Throws(new InvalidOperationException("provider-secret-value"));
        var coordinator = new AuthorizationCodeFlowCoordinator([provider.Object]);

        var action = () => coordinator.Begin(CreateRequest());

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("Authorization-code token exchange provider selection failed.")
            .Which.Message.Should().NotContain("provider-secret-value");
    }

    private static Mock<IAuthorizationCodeTokenExchangeProvider> CreateProvider()
    {
        var provider = new Mock<IAuthorizationCodeTokenExchangeProvider>();
        provider.Setup(candidate => candidate.CanHandle(It.IsAny<Uri>())).Returns(true);
        return provider;
    }

    private static AuthorizationCodeFlowRequest CreateRequest(bool issuerParameterSupported = false) => new()
    {
        AuthorizationEndpoint = new Uri("https://login.example.test/authorize"),
        TokenEndpoint = new Uri("https://login.example.test/token"),
        ExpectedIssuer = "https://login.example.test",
        AuthorizationResponseIssuerParameterSupported = issuerParameterSupported,
        ClientId = "client-id",
        RedirectUri = new Uri("https://client.example.test/callback"),
        Scopes = ["tools:read"],
        Resource = new Uri("https://mcp.example.test/mcp")
    };

    private static Dictionary<string, string> ParseQuery(Uri uri) =>
        uri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .ToDictionary(
                part => Uri.UnescapeDataString(part[0]),
                part => Uri.UnescapeDataString(part[1]),
                StringComparer.Ordinal);

    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration) => _now = _now.Add(duration);
    }
}