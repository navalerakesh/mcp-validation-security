namespace Mcp.Benchmark.Core.Exceptions;

/// <summary>
/// Base class for reusable MCP validation/runtime exceptions shared across hosts.
/// Keeps user-facing messages neutral while exposing machine-readable error codes.
/// </summary>
public abstract class McpValidationException : Exception
{
    protected McpValidationException(string message, string? errorCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        ErrorCode = string.IsNullOrWhiteSpace(errorCode)
            ? DefaultErrorCode
            : errorCode!;
    }

    /// <summary>
    /// Machine-readable error code that downstream hosts can translate into UX-specific behaviors.
    /// </summary>
    public string ErrorCode { get; }

    /// <summary>
    /// Indicates whether this error may automatically resolve on retry (e.g., transient network faults).
    /// Hosts can surface this flag to help users decide next steps.
    /// </summary>
    public virtual bool IsTransient => false;

    protected virtual string DefaultErrorCode => "MCP_VALIDATION_ERROR";
}
