namespace Mcp.Benchmark.CLI.Exceptions;

/// <summary>
/// Exception used for invalid command usage or malformed user input.
/// </summary>
public sealed class CliUsageException : CliExceptionBase
{
    public CliUsageException(string message, Exception? innerException = null)
        : base(message, exitCode: 64, errorCode: "MCP_USAGE_ERROR", innerException)
    {
    }
}

/// <summary>
/// Exception used for operational failures that should surface with a non-zero exit code.
/// </summary>
public sealed class CliOperationException : CliExceptionBase
{
    public CliOperationException(string message, int exitCode = 1, Exception? innerException = null)
        : base(message, exitCode, errorCode: "MCP_OPERATION_ERROR", innerException)
    {
    }
}
