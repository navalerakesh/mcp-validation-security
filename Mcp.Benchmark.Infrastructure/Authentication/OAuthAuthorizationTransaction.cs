using System.Security.Cryptography;
using System.Text;

namespace Mcp.Benchmark.Infrastructure.Authentication;

internal enum OAuthCallbackValidationStatus
{
    Valid,
    InvalidRedirectUri,
    InvalidState,
    Expired,
    AlreadyConsumed
}

internal sealed record OAuthCallbackValidationResult(
    OAuthCallbackValidationStatus Status,
    string? CodeVerifier,
    string? Error)
{
    public bool IsValid => Status == OAuthCallbackValidationStatus.Valid;
}

internal sealed class OAuthAuthorizationTransaction
{
    private const int StateBytes = 32;
    private const int CodeVerifierBytes = 64;
    private static readonly TimeSpan DefaultLifetime = TimeSpan.FromMinutes(10);

    private readonly object _sync = new();
    private readonly byte[] _stateHash;
    private readonly TimeProvider _timeProvider;
    private string? _codeVerifier;
    private bool _consumed;

    private OAuthAuthorizationTransaction(
        Uri authorizationUri,
        Uri redirectUri,
        byte[] stateHash,
        string codeVerifier,
        DateTimeOffset expiresUtc,
        TimeProvider timeProvider)
    {
        AuthorizationUri = authorizationUri;
        RedirectUri = redirectUri;
        _stateHash = stateHash;
        _codeVerifier = codeVerifier;
        ExpiresUtc = expiresUtc;
        _timeProvider = timeProvider;
    }

    public Uri AuthorizationUri { get; }

    public Uri RedirectUri { get; }

    public DateTimeOffset ExpiresUtc { get; }

    public static OAuthAuthorizationTransaction Create(
        Uri authorizationEndpoint,
        string clientId,
        Uri redirectUri,
        IEnumerable<string> scopes,
        Uri? resource = null,
        TimeSpan? lifetime = null,
        TimeProvider? timeProvider = null)
    {
        ValidateEndpoint(authorizationEndpoint, nameof(authorizationEndpoint));
        ValidateRedirectUri(redirectUri);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        ArgumentNullException.ThrowIfNull(scopes);

        var normalizedScopes = scopes
            .Where(scope => !string.IsNullOrWhiteSpace(scope))
            .Select(scope => scope.Trim())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(scope => scope, StringComparer.Ordinal)
            .ToArray();
        if (normalizedScopes.Length == 0)
        {
            throw new ArgumentException("At least one OAuth scope is required.", nameof(scopes));
        }

        if (resource != null)
        {
            ValidateEndpoint(resource, nameof(resource));
        }

        var clock = timeProvider ?? TimeProvider.System;
        var effectiveLifetime = lifetime ?? DefaultLifetime;
        if (effectiveLifetime <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(lifetime));
        }

        var state = CreateBase64UrlSecret(StateBytes);
        var verifier = CreateBase64UrlSecret(CodeVerifierBytes);
        var challenge = Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var query = new List<KeyValuePair<string, string>>
        {
            new("response_type", "code"),
            new("client_id", clientId.Trim()),
            new("redirect_uri", redirectUri.AbsoluteUri),
            new("scope", string.Join(' ', normalizedScopes)),
            new("state", state),
            new("code_challenge", challenge),
            new("code_challenge_method", "S256")
        };
        if (resource != null)
        {
            query.Add(new("resource", resource.AbsoluteUri));
        }

        var existingQuery = authorizationEndpoint.Query.TrimStart('?');
        var generatedNames = query.Select(pair => pair.Key).ToHashSet(StringComparer.Ordinal);
        if (GetQueryParameterNames(existingQuery).Any(generatedNames.Contains))
        {
            throw new ArgumentException("Authorization endpoint query must not predefine generated OAuth parameters.", nameof(authorizationEndpoint));
        }

        var generatedQuery = string.Join("&", query.Select(pair =>
            $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
        var combinedQuery = string.IsNullOrEmpty(existingQuery)
            ? generatedQuery
            : $"{existingQuery}&{generatedQuery}";
        var authorizationUri = new Uri($"{authorizationEndpoint.GetLeftPart(UriPartial.Path)}?{combinedQuery}");

        return new OAuthAuthorizationTransaction(
            authorizationUri,
            redirectUri,
            SHA256.HashData(Encoding.ASCII.GetBytes(state)),
            verifier,
            clock.GetUtcNow().Add(effectiveLifetime),
            clock);
    }

    public OAuthCallbackValidationResult ValidateAndConsumeCallback(Uri actualRedirectUri, string? returnedState)
    {
        ArgumentNullException.ThrowIfNull(actualRedirectUri);
        lock (_sync)
        {
            if (_consumed)
            {
                return new OAuthCallbackValidationResult(
                    OAuthCallbackValidationStatus.AlreadyConsumed,
                    null,
                    "The OAuth authorization transaction was already consumed.");
            }

            if (_timeProvider.GetUtcNow() > ExpiresUtc)
            {
                Consume();
                return new OAuthCallbackValidationResult(
                    OAuthCallbackValidationStatus.Expired,
                    null,
                    "The OAuth authorization transaction expired.");
            }

            if (!string.Equals(RedirectUri.AbsoluteUri, actualRedirectUri.AbsoluteUri, StringComparison.Ordinal))
            {
                Consume();
                return new OAuthCallbackValidationResult(
                    OAuthCallbackValidationStatus.InvalidRedirectUri,
                    null,
                    "The OAuth callback redirect URI did not exactly match the registered redirect URI.");
            }

            if (string.IsNullOrEmpty(returnedState))
            {
                Consume();
                return new OAuthCallbackValidationResult(
                    OAuthCallbackValidationStatus.InvalidState,
                    null,
                    "The OAuth callback state was missing.");
            }

            var returnedStateHash = SHA256.HashData(Encoding.ASCII.GetBytes(returnedState));
            if (!CryptographicOperations.FixedTimeEquals(_stateHash, returnedStateHash))
            {
                Consume();
                return new OAuthCallbackValidationResult(
                    OAuthCallbackValidationStatus.InvalidState,
                    null,
                    "The OAuth callback state did not match the authorization transaction.");
            }

            var verifier = _codeVerifier;
            Consume();
            return new OAuthCallbackValidationResult(OAuthCallbackValidationStatus.Valid, verifier, null);
        }
    }

    private void Consume()
    {
        _consumed = true;
        _codeVerifier = null;
    }

    private static void ValidateEndpoint(Uri uri, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(uri, parameterName);
        if (!uri.IsAbsoluteUri || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("OAuth endpoints and resource indicators must use absolute HTTPS URIs.", parameterName);
        }

        if (!string.IsNullOrEmpty(uri.Fragment) || !string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new ArgumentException("OAuth URIs must not contain fragments or user-info.", parameterName);
        }
    }

    private static void ValidateRedirectUri(Uri redirectUri)
    {
        ArgumentNullException.ThrowIfNull(redirectUri);
        if (!redirectUri.IsAbsoluteUri || !string.IsNullOrEmpty(redirectUri.Fragment) || !string.IsNullOrEmpty(redirectUri.UserInfo))
        {
            throw new ArgumentException("OAuth redirect URI must be absolute and must not contain user-info or a fragment.", nameof(redirectUri));
        }

        var isHttps = string.Equals(redirectUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
        var isLoopbackHttp = string.Equals(redirectUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) && redirectUri.IsLoopback;
        if (!isHttps && !isLoopbackHttp)
        {
            throw new ArgumentException("OAuth redirect URI must use HTTPS, except loopback HTTP for native clients.", nameof(redirectUri));
        }
    }

    private static string CreateBase64UrlSecret(int byteCount)
    {
        return Base64UrlEncode(RandomNumberGenerator.GetBytes(byteCount));
    }

    private static IEnumerable<string> GetQueryParameterNames(string query)
    {
        if (string.IsNullOrEmpty(query))
        {
            return Array.Empty<string>();
        }

        return query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2)[0])
            .Select(name => Uri.UnescapeDataString(name.Replace("+", " ", StringComparison.Ordinal)));
    }

    private static string Base64UrlEncode(ReadOnlySpan<byte> value)
    {
        return Convert.ToBase64String(value)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}
