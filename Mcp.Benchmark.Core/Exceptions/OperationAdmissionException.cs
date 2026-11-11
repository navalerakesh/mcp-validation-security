namespace Mcp.Benchmark.Core.Exceptions;

public sealed class OperationAdmissionException(string message) : InvalidOperationException(message);