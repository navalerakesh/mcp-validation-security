using System.Text.RegularExpressions;
using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.CLI.Utilities.Logging;

internal static partial class SessionLogRedactor
{
    [GeneratedRegex("Bearer\\s+[A-Za-z0-9\\-._~+/]+=*", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex BearerTokenRegex();

    [GeneratedRegex("(?i)(\\\"(?:token|password|secret|apiKey|api-key)\\\"\\s*:\\s*\\\")[^\\\"]+(\\\")", RegexOptions.Compiled)]
    private static partial Regex JsonSecretRegex();

    [GeneratedRegex("(?i)((?:authorization|token|secret|api[-_]?key)\\s*[:=]\\s*)([^\\s,;]+)", RegexOptions.Compiled)]
    private static partial Regex HeaderSecretRegex();

    [GeneratedRegex("(?i)(access[_-]?token=)([^&\\s]+)", RegexOptions.Compiled)]
    private static partial Regex QueryTokenRegex();

    public static string Redact(string? message, RedactionLevel redactionLevel)
    {
        if (string.IsNullOrEmpty(message))
        {
            return string.Empty;
        }

        var redacted = BearerTokenRegex().Replace(message, "Bearer __REDACTED__");
        redacted = JsonSecretRegex().Replace(redacted, "$1__REDACTED__$2");
        redacted = HeaderSecretRegex().Replace(redacted, "$1__REDACTED__");

        if (redactionLevel == RedactionLevel.Strict)
        {
            redacted = QueryTokenRegex().Replace(redacted, "$1__REDACTED__");
        }

        return redacted;
    }
}