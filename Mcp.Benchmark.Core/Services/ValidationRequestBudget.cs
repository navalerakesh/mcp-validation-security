using Mcp.Benchmark.Core.Exceptions;

namespace Mcp.Benchmark.Core.Services;

public static class ValidationRequestBudget
{
    public static int NormalizeLimit(int limit) => Math.Clamp(limit,
        Constants.ExecutionPolicyDefaults.MinimumPositiveValue, Constants.ExecutionPolicyDefaults.MaximumRequests);

    public static void Admit(ref int admittedRequests, int limit)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        while (true)
        {
            var count = Volatile.Read(ref admittedRequests);
            if (count < 0) throw new InvalidOperationException("Admitted request count must not be negative.");
            if (count >= limit)
                throw new OperationAdmissionException($"Execution request budget exceeded ({limit}).");
            if (Interlocked.CompareExchange(ref admittedRequests, count + 1, count) == count) return;
        }
    }
}
