using System;
using System.Net.Http.Headers;
using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.Infrastructure.Http;

/// <summary>
/// Shared helper for building Authorization headers from <see cref="AuthenticationConfig"/>.
/// Centralizes bearer token normalization logic so HTTP callers and SDK transports stay consistent.
/// </summary>
internal static class McpAuthenticationHelper
{
    /// <summary>
    /// Builds an Authorization header value (e.g. "Bearer &lt;token&gt;") based on the authentication config.
    /// </summary>
    /// <returns>
    /// A string suitable for an HTTP Authorization header, or <c>null</c> if no auth should be applied,
    /// or <c>string.Empty</c> when an explicit empty token was provided ("no auth, do not fall back").
    /// </returns>
    public static string? BuildAuthorizationHeaderValue(AuthenticationConfig? authentication)
    {
        if (authentication == null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(authentication.Token))
        {
            // Explicit auth config with empty token means: "no auth".
            // Callers can use this to avoid falling back to any global/default auth.
            return string.Empty;
        }

        var token = NormalizeBearerToken(authentication.Token);

        if (authentication.Type?.Equals("bearer", StringComparison.OrdinalIgnoreCase) == true)
        {
            return $"Bearer {token}";
        }

        // For unknown types, respect the raw token string once normalized.
        return token;
    }

    /// <summary>
    /// Normalizes a bearer token by trimming and removing any leading "Bearer " prefix.
    /// </summary>
    public static string NormalizeBearerToken(string token)
    {
        var normalized = token?.Trim() ?? string.Empty;
        if (normalized.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized.Substring("Bearer ".Length).Trim();
        }

        return normalized;
    }

    /// <summary>
    /// Applies an Authorization header to <see cref="HttpRequestHeaders"/> when the provided
    /// header value is non-empty.
    /// </summary>
    public static void ApplyAuthorizationHeader(HttpRequestHeaders headers, string? headerValue)
    {
        if (headers == null)
        {
            throw new ArgumentNullException(nameof(headers));
        }

        headers.Authorization = null;

        if (string.IsNullOrWhiteSpace(headerValue))
        {
            return;
        }

        headers.Authorization = AuthenticationHeaderValue.Parse(headerValue);
    }
}
