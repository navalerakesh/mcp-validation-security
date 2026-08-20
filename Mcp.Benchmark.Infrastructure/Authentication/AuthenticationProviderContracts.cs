using System.Collections.Concurrent;
using System.Security.Cryptography;
using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.Infrastructure.Authentication;

public sealed class NoninteractiveCredentialRequest
{
    public required AuthMetadata Metadata { get; init; }
    public required IReadOnlyList<string> Scopes { get; init; }
    public Uri? Resource { get; init; }
    public string? TenantId { get; init; }
    public string? ClientId { get; init; }

    public override string ToString() =>
        $"NoninteractiveCredentialRequest {{ Scopes = {Scopes.Count}, Resource = {Resource}, TenantId = {TenantId}, ClientId = {ClientId} }}";
}

public interface INoninteractiveCredentialProvider
{
    string ProviderName { get; }
    bool CanHandle(NoninteractiveCredentialRequest request);
    Task<SecretRef?> GetTokenReferenceAsync(NoninteractiveCredentialRequest request, CancellationToken cancellationToken);
}

public sealed class AuthorizationCodeFlowRequest
{
    public required Uri AuthorizationEndpoint { get; init; }
    public required Uri TokenEndpoint { get; init; }
    public required string ExpectedIssuer { get; init; }
    public bool AuthorizationResponseIssuerParameterSupported { get; init; }
    public required string ClientId { get; init; }
    public required Uri RedirectUri { get; init; }
    public required IReadOnlyList<string> Scopes { get; init; }
    public Uri? Resource { get; init; }
}

public sealed record AuthorizationCodeChallenge(
    string TransactionId,
    Uri AuthorizationUri,
    Uri RedirectUri,
    DateTimeOffset ExpiresUtc)
{
    public override string ToString() =>
        $"AuthorizationCodeChallenge {{ TransactionId = [REDACTED], AuthorizationUri = [REDACTED], RedirectUri = {RedirectUri}, ExpiresUtc = {ExpiresUtc:O} }}";
}

public sealed class AuthorizationCodeTokenExchangeRequest
{
    public required Uri TokenEndpoint { get; init; }
    public required string ClientId { get; init; }
    public required Uri RedirectUri { get; init; }
    public required string AuthorizationCode { get; init; }
    public required string CodeVerifier { get; init; }
    public Uri? Resource { get; init; }

    public override string ToString() =>
        $"AuthorizationCodeTokenExchangeRequest {{ TokenEndpoint = {TokenEndpoint}, ClientId = {ClientId}, RedirectUri = {RedirectUri}, AuthorizationCode = [REDACTED], CodeVerifier = [REDACTED], Resource = {Resource} }}";
}

public interface IAuthorizationCodeTokenExchangeProvider
{
    string ProviderName { get; }
    bool CanHandle(Uri tokenEndpoint);
    Task<SecretRef?> ExchangeAsync(AuthorizationCodeTokenExchangeRequest request, CancellationToken cancellationToken);
}

public enum AuthorizationCodeCompletionStatus
{
    Succeeded,
    UnknownTransaction,
    InvalidRedirectUri,
    InvalidState,
    MissingIssuer,
    IssuerMismatch,
    Expired,
    MissingAuthorizationCode,
    TokenExchangeFailed
}

public sealed record AuthorizationCodeCompletionResult(
    AuthorizationCodeCompletionStatus Status,
    SecretRef? TokenReference,
    string? Error)
{
    public bool IsSuccessful => Status == AuthorizationCodeCompletionStatus.Succeeded;
}

public interface IAuthorizationCodeFlowCoordinator
{
    AuthorizationCodeChallenge Begin(AuthorizationCodeFlowRequest request);

    Task<AuthorizationCodeCompletionResult> CompleteAsync(
        string transactionId,
        Uri actualRedirectUri,
        string? returnedState,
        string? authorizationCode,
        CancellationToken cancellationToken);

    Task<AuthorizationCodeCompletionResult> CompleteAsync(
        string transactionId,
        Uri actualRedirectUri,
        string? returnedState,
        string? returnedIssuer,
        string? authorizationCode,
        CancellationToken cancellationToken);
}

public sealed class AuthorizationCodeFlowCoordinator : IAuthorizationCodeFlowCoordinator
{
    private const int MaxPendingTransactions = 128;
    private readonly IReadOnlyList<IAuthorizationCodeTokenExchangeProvider> _providers;
    private readonly ConcurrentDictionary<string, PendingAuthorizationCodeFlow> _transactions = new(StringComparer.Ordinal);
    private readonly TimeProvider _timeProvider;
    private readonly object _beginSync = new();

    public AuthorizationCodeFlowCoordinator(
        IEnumerable<IAuthorizationCodeTokenExchangeProvider> providers,
        TimeProvider? timeProvider = null)
    {
        _providers = providers?.ToArray() ?? throw new ArgumentNullException(nameof(providers));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public AuthorizationCodeChallenge Begin(AuthorizationCodeFlowRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!Uri.TryCreate(request.ExpectedIssuer, UriKind.Absolute, out var expectedIssuer) ||
            !string.Equals(expectedIssuer.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(expectedIssuer.Fragment))
        {
            throw new ArgumentException("Expected issuer must be an absolute HTTPS URI without a fragment.", nameof(request));
        }

        IAuthorizationCodeTokenExchangeProvider? provider;
        try
        {
            provider = _providers.FirstOrDefault(candidate => candidate.CanHandle(request.TokenEndpoint));
        }
        catch (Exception)
        {
            throw new InvalidOperationException("Authorization-code token exchange provider selection failed.");
        }

        if (provider == null)
        {
            throw new InvalidOperationException("No authorization-code token exchange provider can handle the declared token endpoint.");
        }
        lock (_beginSync)
        {
            SweepExpiredTransactions();
            if (_transactions.Count >= MaxPendingTransactions)
            {
                throw new InvalidOperationException($"The authorization flow limit of {MaxPendingTransactions} pending transactions was reached.");
            }

            var transaction = OAuthAuthorizationTransaction.Create(
                request.AuthorizationEndpoint,
                request.ClientId,
                request.RedirectUri,
                request.Scopes,
                request.Resource,
                timeProvider: _timeProvider);
            for (var attempt = 0; attempt < 3; attempt++)
            {
                var transactionId = CreateTransactionId();
                if (_transactions.TryAdd(transactionId, new PendingAuthorizationCodeFlow(request, transaction, provider)))
                {
                    return new AuthorizationCodeChallenge(
                        transactionId,
                        transaction.AuthorizationUri,
                        transaction.RedirectUri,
                        transaction.ExpiresUtc);
                }
            }

            throw new InvalidOperationException("Could not allocate a unique authorization transaction identifier.");
        }
    }

    public async Task<AuthorizationCodeCompletionResult> CompleteAsync(
        string transactionId,
        Uri actualRedirectUri,
        string? returnedState,
        string? authorizationCode,
        CancellationToken cancellationToken) =>
        await CompleteAsync(
            transactionId,
            actualRedirectUri,
            returnedState,
            returnedIssuer: null,
            authorizationCode,
            cancellationToken);

    public async Task<AuthorizationCodeCompletionResult> CompleteAsync(
        string transactionId,
        Uri actualRedirectUri,
        string? returnedState,
        string? returnedIssuer,
        string? authorizationCode,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(transactionId);
        ArgumentNullException.ThrowIfNull(actualRedirectUri);
        cancellationToken.ThrowIfCancellationRequested();
        if (!_transactions.TryRemove(transactionId, out var pending))
        {
            return Failure(AuthorizationCodeCompletionStatus.UnknownTransaction, "The authorization transaction was unknown or already consumed.");
        }

        var callback = pending.Transaction.ValidateAndConsumeCallback(actualRedirectUri, returnedState);
        if (!callback.IsValid)
        {
            return Failure(MapStatus(callback.Status), callback.Error!);
        }

        if (string.IsNullOrEmpty(returnedIssuer))
        {
            if (pending.Request.AuthorizationResponseIssuerParameterSupported)
            {
                return Failure(AuthorizationCodeCompletionStatus.MissingIssuer, "The authorization response omitted the advertised issuer parameter.");
            }
        }
        else if (!string.Equals(returnedIssuer, pending.Request.ExpectedIssuer, StringComparison.Ordinal))
        {
            return Failure(AuthorizationCodeCompletionStatus.IssuerMismatch, "The authorization response issuer did not exactly match the validated authorization-server issuer.");
        }

        if (string.IsNullOrWhiteSpace(authorizationCode))
        {
            return Failure(AuthorizationCodeCompletionStatus.MissingAuthorizationCode, "The OAuth callback did not include an authorization code.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var tokenReference = await pending.Provider.ExchangeAsync(
                new AuthorizationCodeTokenExchangeRequest
                {
                    TokenEndpoint = pending.Request.TokenEndpoint,
                    ClientId = pending.Request.ClientId,
                    RedirectUri = pending.Request.RedirectUri,
                    AuthorizationCode = authorizationCode,
                    CodeVerifier = callback.CodeVerifier!,
                    Resource = pending.Request.Resource
                },
                cancellationToken);
                 return tokenReference == null ||
                     string.IsNullOrWhiteSpace(tokenReference.Provider) ||
                     string.IsNullOrWhiteSpace(tokenReference.Name)
                ? Failure(AuthorizationCodeCompletionStatus.TokenExchangeFailed, "The token exchange provider did not return a secret reference.")
                : new AuthorizationCodeCompletionResult(AuthorizationCodeCompletionStatus.Succeeded, tokenReference, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return Failure(AuthorizationCodeCompletionStatus.TokenExchangeFailed, "The token exchange provider failed.");
        }
    }

    private void SweepExpiredTransactions()
    {
        var now = _timeProvider.GetUtcNow();
        foreach (var transaction in _transactions)
        {
            if (transaction.Value.Transaction.ExpiresUtc <= now)
            {
                _transactions.TryRemove(transaction.Key, out _);
            }
        }
    }

    private static AuthorizationCodeCompletionResult Failure(AuthorizationCodeCompletionStatus status, string error) =>
        new(status, null, error);

    private static AuthorizationCodeCompletionStatus MapStatus(OAuthCallbackValidationStatus status) => status switch
    {
        OAuthCallbackValidationStatus.InvalidRedirectUri => AuthorizationCodeCompletionStatus.InvalidRedirectUri,
        OAuthCallbackValidationStatus.InvalidState => AuthorizationCodeCompletionStatus.InvalidState,
        OAuthCallbackValidationStatus.Expired => AuthorizationCodeCompletionStatus.Expired,
        _ => AuthorizationCodeCompletionStatus.UnknownTransaction
    };

    private static string CreateTransactionId() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private sealed record PendingAuthorizationCodeFlow(
        AuthorizationCodeFlowRequest Request,
        OAuthAuthorizationTransaction Transaction,
        IAuthorizationCodeTokenExchangeProvider Provider);
}