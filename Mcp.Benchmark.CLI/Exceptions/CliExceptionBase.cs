using System.Text.RegularExpressions;
using Mcp.Benchmark.Core.Exceptions;

namespace Mcp.Benchmark.CLI.Exceptions;

/// <summary>
/// Base class for CLI exceptions that carry an explicit process exit code.
/// Messages are sanitized to remove ANSI escape sequences before displaying to users.
/// </summary>
public abstract partial class CliExceptionBase : McpValidationException
{
    protected CliExceptionBase(string message, int exitCode = 1, string? errorCode = null, Exception? innerException = null)
        : base(Sanitize(message), errorCode, innerException)
    {
        ExitCode = exitCode;
    }

    /// <summary>
    /// Gets the exit code that should be returned to the shell when this exception is raised.
    /// </summary>
    public int ExitCode { get; }

    private static string Sanitize(string? input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return string.Empty;
        }

        return AnsiRegex().Replace(input, string.Empty);
    }

    [GeneratedRegex("\\x1B\\[[0-9;]*m", RegexOptions.Compiled)]
    private static partial Regex AnsiRegex();
}
