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
        var requiresAuthentication = ValidationReliability.IsAuthenticationStatusCode(response.StatusCode);
        var hasChallengeHeader = !string.IsNullOrWhiteSpace(headerValue);

        return new AuthenticationChallengeObservation(
            response.StatusCode,
            requiresAuthentication,
            requiresAuthentication && hasChallengeHeader,
            headerValue,
            durationMs ?? response.ElapsedMs ?? 0.0,
            ExtractParameter(headerValue, "resource_metadata"),
            ExtractParameter(headerValue, "authorization_uri"),
            ExtractParameter(headerValue, "realm"),
            ExtractParameter(headerValue, "error"),
            ExtractParameter(headerValue, "error_description"),
            ExtractParameter(headerValue, "scope"),
            headerValue?.Contains("Bearer", StringComparison.OrdinalIgnoreCase) == true);
    }

    public static AuthDiscoveryInfo? CreateDiscoveryInfo(AuthenticationChallengeObservation observation, IEnumerable<string>? issues = null)
    {
        if (!observation.RequiresAuthentication)
        {
            return null;
        }

        var discoveredIssues = issues?
            .Where(note => !string.IsNullOrWhiteSpace(note))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? new List<string>();

        if (!observation.HasWwwAuthenticateHeader)
        {
            discoveredIssues.Add("Authentication required but no WWW-Authenticate challenge was provided.");
        }

        return new AuthDiscoveryInfo
        {
            WwwAuthenticateHeader = observation.WwwAuthenticateHeader,
            DiscoveryTimeMs = observation.DurationMs,
            Issues = discoveredIssues
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

        if (!observation.RequiresAuthentication)
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

    public static string? ExtractParameter(string? headerValue, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(headerValue) || string.IsNullOrWhiteSpace(parameterName))
        {
            return null;
        }

        var quotedValue = ExtractQuotedParameter(headerValue, parameterName);
        if (!string.IsNullOrWhiteSpace(quotedValue))
        {
            return quotedValue;
        }

        var marker = $"{parameterName}=";
        var start = headerValue.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            return null;
        }

        start += marker.Length;
        var end = headerValue.IndexOf(',', start);
        var rawValue = end >= start ? headerValue[start..end] : headerValue[start..];
        rawValue = rawValue.Trim();
        return rawValue.Length > 0 ? rawValue : null;
    }
}

public sealed record AuthenticationChallengeObservation(
    int StatusCode,
    bool RequiresAuthentication,
    bool IsAuthenticationChallenge,
    string? WwwAuthenticateHeader,
    double DurationMs,
    string? ResourceMetadataUrl,
    string? AuthorizationUri,
    string? Realm,
    string? Error,
    string? ErrorDescription,
    string? Scope,
    bool UsesBearerChallenge)
{
    public static readonly AuthenticationChallengeObservation None = new(0, false, false, null, 0.0, null, null, null, null, null, null, false);

    public bool HasWwwAuthenticateHeader => !string.IsNullOrWhiteSpace(WwwAuthenticateHeader);

    public bool IsBareAuthenticationRejection => RequiresAuthentication && !HasWwwAuthenticateHeader;

    public double SecurityScore => HasWwwAuthenticateHeader ? 100.0 : 85.0;
}