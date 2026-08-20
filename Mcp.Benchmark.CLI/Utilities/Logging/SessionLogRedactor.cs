using System.Text.RegularExpressions;
using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.CLI.Utilities.Logging;

internal static partial class SessionLogRedactor
{
    [GeneratedRegex("Bearer\\s+[A-Za-z0-9\\-._~+/]+=*", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex BearerTokenRegex();

    [GeneratedRegex("(?i)(\\\"(?:token|password|secret|apiKey|api-key)\\\"\\s*:\\s*\\\")[^\\\"]+(\\\")", RegexOptions.Compiled)]
    private static partial Regex JsonSecretRegex();

    [GeneratedRegex("(?i)((?:set-cookie|cookie|mcp-session-id|access[_-]?token|refresh[_-]?token|id[_-]?token|client[_-]?secret|password)\\s*[:=]\\s*)([^\r\n]+)", RegexOptions.Compiled)]
    private static partial Regex SensitiveFieldRegex();

    [GeneratedRegex("(?i)(\"(?:access_token|refresh_token|id_token|client_secret|authorization|cookie|set-cookie|mcp-session-id)\"\\s*:\\s*\")[^\"]+(\")", RegexOptions.Compiled)]
    private static partial Regex JsonSensitiveFieldRegex();

    [GeneratedRegex("(?i)\\b(?:ghp_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{20,}|eyJ[A-Za-z0-9_-]{10,}\\.[A-Za-z0-9_-]{10,}\\.[A-Za-z0-9_-]{10,})\\b", RegexOptions.Compiled)]
    private static partial Regex KnownTokenRegex();

    [GeneratedRegex("-----BEGIN ([A-Z0-9 ]+ )?PRIVATE KEY-----[\\s\\S]*?-----END ([A-Z0-9 ]+ )?PRIVATE KEY-----", RegexOptions.Compiled)]
    private static partial Regex PrivateKeyRegex();

    [GeneratedRegex("(?i)((?:authorization|token|secret|api[-_]?key)\\s*[:=]\\s*)([^\\s,;]+)", RegexOptions.Compiled)]
    private static partial Regex HeaderSecretRegex();

    [GeneratedRegex("(?i)([?&](?:access[_-]?token|token|password|secret|api[-_]?key)=)([^&\\s]+)", RegexOptions.Compiled)]
    private static partial Regex QuerySecretRegex();

    [GeneratedRegex("(?i)(https?://)[^/@\\s]+@", RegexOptions.Compiled)]
    private static partial Regex UriUserInfoRegex();

    [GeneratedRegex("(?i)(--(?:token|password|secret|api[-_]?key)\\s+)(?:\"[^\"]*\"|'[^']*'|\\S+)", RegexOptions.Compiled)]
    private static partial Regex FlagSecretRegex();

    public static string Redact(string? message, RedactionLevel redactionLevel)
    {
        if (string.IsNullOrEmpty(message))
        {
            return string.Empty;
        }

        var redacted = BearerTokenRegex().Replace(message, "Bearer __REDACTED__");
        redacted = JsonSecretRegex().Replace(redacted, "$1__REDACTED__$2");
        redacted = HeaderSecretRegex().Replace(redacted, "$1__REDACTED__");
        redacted = FlagSecretRegex().Replace(redacted, "$1__REDACTED__");
        redacted = UriUserInfoRegex().Replace(redacted, "$1__REDACTED__@");
        redacted = SensitiveFieldRegex().Replace(redacted, "$1__REDACTED__");
        redacted = JsonSensitiveFieldRegex().Replace(redacted, "$1__REDACTED__$2");
        redacted = KnownTokenRegex().Replace(redacted, "__TOKEN_REDACTED__");
        redacted = PrivateKeyRegex().Replace(redacted, "__PRIVATE_KEY_REDACTED__");

        if (redactionLevel == RedactionLevel.Strict)
        {
            redacted = QuerySecretRegex().Replace(redacted, "$1__REDACTED__");
        }

        return redacted;
    }
}