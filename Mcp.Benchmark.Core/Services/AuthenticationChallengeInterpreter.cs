using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.Core.Services;

/// <summary>
/// Centralizes interpretation of HTTP authentication challenges so validators do not
/// each need to understand status-code and header parsing details.
/// </summary>
public static class AuthenticationChallengeInterpreter
{
    public static AuthenticationChallengeObservation Inspect(JsonRpcResponse? response, double? durationMs = null)
    {
        if (response == null)
        {
            return AuthenticationChallengeObservation.None;
        }

        var headerValue = TryGetHeaderValue(response.Headers, "WWW-Authenticate");

        return new AuthenticationChallengeObservation(
            response.StatusCode,
            ValidationReliability.IsAuthenticationStatusCode(response.StatusCode),
            headerValue,
            durationMs ?? response.ElapsedMs ?? 0.0,
            ExtractQuotedParameter(headerValue, "resource_metadata"),
            ExtractQuotedParameter(headerValue, "authorization_uri"),
            headerValue?.Contains("Bearer", StringComparison.OrdinalIgnoreCase) == true);
    }

    public static AuthDiscoveryInfo? CreateDiscoveryInfo(AuthenticationChallengeObservation observation, IEnumerable<string>? issues = null)
    {
        if (!observation.IsAuthenticationChallenge)
        {
            return null;
        }

        return new AuthDiscoveryInfo
        {
            WwwAuthenticateHeader = observation.WwwAuthenticateHeader,
            DiscoveryTimeMs = observation.DurationMs,
            Issues = issues?
                .Where(note => !string.IsNullOrWhiteSpace(note))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList() ?? new List<string>()
        };
    }

    public static AuthenticationSecurityResult? CreateSecurityResult(AuthDiscoveryInfo? discovery)
    {
        if (discovery == null)
        {
            return null;
        }

        var result = new AuthenticationSecurityResult
        {
            AuthMetadata = discovery.Metadata,
            Findings = discovery.Issues
                .Where(note => !string.IsNullOrWhiteSpace(note))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList()
        };

        Apply(result, Inspect(new JsonRpcResponse
        {
            StatusCode = 401,
            Headers = string.IsNullOrWhiteSpace(discovery.WwwAuthenticateHeader)
                ? new Dictionary<string, string>()
                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["WWW-Authenticate"] = discovery.WwwAuthenticateHeader
                }
        }, discovery.DiscoveryTimeMs));

        return result;
    }

    public static void Apply(AuthenticationSecurityResult target, AuthenticationChallengeObservation observation)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (!observation.IsAuthenticationChallenge)
        {
            return;
        }

        target.AuthenticationRequired = true;
        target.RejectsUnauthenticated = true;
        target.CorrectStatusCodes = true;
        target.ErrorResponsesCompliant = true;
        target.HasProperAuthHeaders = observation.HasWwwAuthenticateHeader;
        target.ChallengeStatusCode = observation.StatusCode;
        target.WwwAuthenticateHeader = observation.WwwAuthenticateHeader;
        target.SecurityScore = observation.SecurityScore;

        if (observation.DurationMs > 0)
        {
            target.ChallengeDurationMs = observation.DurationMs;
        }
    }

    public static string? TryGetHeaderValue(IReadOnlyDictionary<string, string>? headers, string headerName)
    {
        if (headers == null || string.IsNullOrWhiteSpace(headerName))
        {
            return null;
        }

        if (headers.TryGetValue(headerName, out var value) && !string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        foreach (var header in headers)
        {
            if (string.Equals(header.Key, headerName, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(header.Value))
            {
                return header.Value;
            }
        }

        return null;
    }

    public static string? ExtractQuotedParameter(string? headerValue, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(headerValue) || string.IsNullOrWhiteSpace(parameterName))
        {
            return null;
        }

        var marker = $"{parameterName}=\"";
        var start = headerValue.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            return null;
        }

        start += marker.Length;
        var end = headerValue.IndexOf('"', start);
        return end > start ? headerValue[start..end] : null;
    }
}

public sealed record AuthenticationChallengeObservation(
    int StatusCode,
    bool IsAuthenticationChallenge,
    string? WwwAuthenticateHeader,
    double DurationMs,
    string? ResourceMetadataUrl,
    string? AuthorizationUri,
    bool UsesBearerChallenge)
{
    public static readonly AuthenticationChallengeObservation None = new(0, false, null, 0.0, null, null, false);

    public bool HasWwwAuthenticateHeader => !string.IsNullOrWhiteSpace(WwwAuthenticateHeader);

    public double SecurityScore => HasWwwAuthenticateHeader ? 100.0 : 85.0;
}