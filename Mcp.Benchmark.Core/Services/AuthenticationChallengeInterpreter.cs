using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.Core.Services;

/// <summary>
/// Centralizes interpretation of HTTP authentication challenges so validators do not
/// each need to understand status-code and header parsing details.
/// </summary>
public static class AuthenticationChallengeInterpreter
{
    private const int MaxChallengeLength = 16 * 1024;

    public static AuthenticationChallengeObservation Inspect(JsonRpcResponse? response, double? durationMs = null)
    {
        if (response == null)
        {
            return AuthenticationChallengeObservation.None;
        }

        var headerValue = TryGetHeaderValue(response.Headers, "WWW-Authenticate");
        var requiresAuthentication = ValidationReliability.IsAuthenticationStatusCode(response.StatusCode);
        var hasChallengeHeader = !string.IsNullOrWhiteSpace(headerValue);
        var parsed = ParseBearerChallenge(headerValue);

        return new AuthenticationChallengeObservation(
            response.StatusCode,
            requiresAuthentication,
            requiresAuthentication && parsed.IsValid && parsed.IsPresent,
            headerValue,
            durationMs ?? response.ElapsedMs ?? 0.0,
            parsed.GetParameter("resource_metadata"),
            parsed.GetParameter("authorization_uri"),
            parsed.GetParameter("realm"),
            parsed.GetParameter("error"),
            parsed.GetParameter("error_description"),
            parsed.GetParameter("scope"),
            parsed.IsPresent && parsed.IsValid,
            parsed.IsValid);
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
            WwwAuthenticateHeader = SanitizeForEvidence(observation),
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
        target.WwwAuthenticateHeader = SanitizeForEvidence(observation);
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
        return ExtractParameter(headerValue, parameterName);
    }

    public static string? ExtractParameter(string? headerValue, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(headerValue) || string.IsNullOrWhiteSpace(parameterName))
        {
            return null;
        }

        var parsed = ParseBearerChallenge(headerValue);
        return parsed.IsValid ? parsed.GetParameter(parameterName) : null;
    }

    public static string? SanitizeForEvidence(AuthenticationChallengeObservation observation)
    {
        if (!observation.HasWwwAuthenticateHeader)
        {
            return null;
        }

        if (!observation.ChallengeSyntaxValid)
        {
            return "[INVALID AUTHENTICATION CHALLENGE REDACTED]";
        }

        if (!observation.UsesBearerChallenge)
        {
            return "[NON-BEARER AUTHENTICATION CHALLENGE REDACTED]";
        }

        var parameters = new List<string>();
        AddSanitizedParameter(parameters, "realm", observation.Realm, false);
        AddSanitizedParameter(parameters, "error", observation.Error, false);
        AddSanitizedParameter(parameters, "scope", observation.Scope, false);
        AddSanitizedParameter(parameters, "resource_metadata", observation.ResourceMetadataUrl, true);
        AddSanitizedParameter(parameters, "authorization_uri", observation.AuthorizationUri, true);
        if (!string.IsNullOrWhiteSpace(observation.ErrorDescription))
        {
            parameters.Add("error_description=\"[REDACTED]\"");
        }

        return parameters.Count == 0 ? "Bearer" : $"Bearer {string.Join(", ", parameters)}";
    }

    private static ParsedBearerChallenge ParseBearerChallenge(string? headerValue)
    {
        if (string.IsNullOrWhiteSpace(headerValue))
        {
            return ParsedBearerChallenge.Absent;
        }

        if (headerValue.Length > MaxChallengeLength || !TrySplitChallengeSegments(headerValue, out var segments))
        {
            return ParsedBearerChallenge.Invalid;
        }

        Dictionary<string, string>? bearerParameters = null;
        string? activeScheme = null;
        var activeValid = true;
        foreach (var segment in segments)
        {
            var trimmed = segment.Trim();
            if (trimmed.Length == 0)
            {
                return ParsedBearerChallenge.Invalid;
            }

            var firstWhitespace = trimmed.IndexOfAny([' ', '\t']);
            var equals = FindUnquotedEquals(trimmed);
            var startsParameter = activeScheme != null &&
                                  equals > 0 &&
                                  IsToken(trimmed[..equals].Trim());
            string parameterText;
            if (startsParameter)
            {
                if (activeScheme == null)
                {
                    return ParsedBearerChallenge.Invalid;
                }

                parameterText = trimmed;
            }
            else
            {
                var scheme = firstWhitespace < 0 ? trimmed : trimmed[..firstWhitespace];
                if (!IsToken(scheme))
                {
                    return ParsedBearerChallenge.Invalid;
                }

                activeScheme = scheme;
                activeValid = true;
                parameterText = firstWhitespace < 0 ? string.Empty : trimmed[(firstWhitespace + 1)..].Trim();
                if (string.Equals(activeScheme, "Bearer", StringComparison.OrdinalIgnoreCase))
                {
                    if (bearerParameters != null)
                    {
                        return ParsedBearerChallenge.Invalid;
                    }

                    bearerParameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                }
            }

            if (parameterText.Length == 0)
            {
                continue;
            }

            if (!TryParseParameter(parameterText, out var name, out var value))
            {
                activeValid = false;
            }
            else if (string.Equals(activeScheme, "Bearer", StringComparison.OrdinalIgnoreCase) &&
                     !bearerParameters!.TryAdd(name, value))
            {
                activeValid = false;
            }

            if (!activeValid && string.Equals(activeScheme, "Bearer", StringComparison.OrdinalIgnoreCase))
            {
                return ParsedBearerChallenge.Invalid;
            }
        }

        return bearerParameters == null
            ? ParsedBearerChallenge.Absent
            : new ParsedBearerChallenge(true, true, bearerParameters);
    }

    private static bool TrySplitChallengeSegments(string value, out List<string> segments)
    {
        segments = new List<string>();
        var start = 0;
        var quoted = false;
        var escaped = false;
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (quoted && character == '\\')
            {
                escaped = true;
            }
            else if (character == '"')
            {
                quoted = !quoted;
            }
            else if (!quoted && character == ',')
            {
                segments.Add(value[start..index]);
                start = index + 1;
            }
        }

        if (quoted || escaped)
        {
            return false;
        }

        segments.Add(value[start..]);
        return true;
    }

    private static bool TryParseParameter(string value, out string name, out string parameterValue)
    {
        name = string.Empty;
        parameterValue = string.Empty;
        var equals = FindUnquotedEquals(value);
        if (equals <= 0)
        {
            return false;
        }

        name = value[..equals].Trim();
        var rawValue = value[(equals + 1)..].Trim();
        if (!IsToken(name) || rawValue.Length == 0)
        {
            return false;
        }

        if (rawValue[0] != '"')
        {
            if (!IsToken(rawValue))
            {
                return false;
            }

            parameterValue = rawValue;
            return true;
        }

        var result = new System.Text.StringBuilder(rawValue.Length);
        var escaped = false;
        for (var index = 1; index < rawValue.Length; index++)
        {
            var character = rawValue[index];
            if (escaped)
            {
                result.Append(character);
                escaped = false;
            }
            else if (character == '\\')
            {
                escaped = true;
            }
            else if (character == '"')
            {
                if (rawValue[(index + 1)..].Trim().Length != 0)
                {
                    return false;
                }

                parameterValue = result.ToString();
                return true;
            }
            else
            {
                result.Append(character);
            }
        }

        return false;
    }

    private static int FindUnquotedEquals(string value) => value.IndexOf('=');

    private static bool IsToken(string value) =>
        value.Length > 0 && value.All(character =>
            char.IsAsciiLetterOrDigit(character) || character is '!' or '#' or '$' or '%' or '&' or '\'' or '*' or '+' or '-' or '.' or '^' or '_' or '`' or '|' or '~');

    private static void AddSanitizedParameter(List<string> target, string name, string? value, bool redactUri)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var evidenceValue = redactUri ? RedactUriQuery(value) : value;
        if (evidenceValue.Length > 1024)
        {
            evidenceValue = evidenceValue[..1024];
        }

        var escaped = evidenceValue
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
        target.Add($"{name}=\"{escaped}\"");
    }

    private static string RedactUriQuery(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Query))
        {
            return value;
        }

        return new UriBuilder(uri) { Query = "__REDACTED__" }.Uri.AbsoluteUri;
    }

    private sealed record ParsedBearerChallenge(bool IsPresent, bool IsValid, IReadOnlyDictionary<string, string> Parameters)
    {
        public static readonly ParsedBearerChallenge Absent = new(false, true, new Dictionary<string, string>());
        public static readonly ParsedBearerChallenge Invalid = new(true, false, new Dictionary<string, string>());

        public string? GetParameter(string name) =>
            Parameters.TryGetValue(name, out var value) ? value : null;
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
    bool UsesBearerChallenge,
    bool ChallengeSyntaxValid)
{
    public static readonly AuthenticationChallengeObservation None = new(0, false, false, null, 0.0, null, null, null, null, null, null, false, true);

    public bool HasWwwAuthenticateHeader => !string.IsNullOrWhiteSpace(WwwAuthenticateHeader);

    public bool IsBareAuthenticationRejection => RequiresAuthentication && !HasWwwAuthenticateHeader;

    public double SecurityScore => HasWwwAuthenticateHeader ? 100.0 : 85.0;
}