using System;

namespace Mcp.Benchmark.Core.Models;

/// <summary>
/// Represents failures that occur while building the validation session before any test logic runs.
/// Captures the appropriate validation status so callers can translate setup failures into user-facing results.
/// </summary>
public sealed class ValidationSessionException : Exception
{
    public ValidationSessionException(string message, ValidationStatus status, Exception? innerException = null)
        : base(message, innerException)
    {
        Status = status;
    }

    public ValidationStatus Status { get; }
}
