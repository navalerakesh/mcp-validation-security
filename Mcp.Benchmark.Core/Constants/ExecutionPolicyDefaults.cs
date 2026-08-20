namespace Mcp.Benchmark.Core.Constants;

public static class ExecutionPolicyDefaults
{
    public const int MinimumPositiveValue = 1;
    public const int DefaultMaxRequests = 256;
    public const int MaximumRequests = 100_000;
    public const int DefaultMaxConcurrency = 4;
    public const int MaximumConcurrency = 128;
    public const int DefaultTimeoutSeconds = 120;
    public const int MaximumTimeoutSeconds = int.MaxValue / 1000;
    public const int DefaultMaxResponseBytes = 1024 * 1024;
    public const int MaximumResponseBytes = 16 * 1024 * 1024;
    public const int DefaultMaxSubprocessOutputBytes = 256 * 1024;
}