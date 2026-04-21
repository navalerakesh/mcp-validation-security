using System.Linq;
using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.Core.Services;

public static class PerformanceMeasurementEvaluator
{
    public static bool HasObservedMetrics(PerformanceTestResult performance)
    {
        ArgumentNullException.ThrowIfNull(performance);

        return performance.LoadTesting.TotalRequests > 0
            || performance.LoadTesting.SuccessfulRequests > 0
            || performance.LoadTesting.FailedRequests > 0
            || performance.LoadTesting.ConnectionErrors.Count > 0
            || performance.ResponseTimes.OperationBenchmarks.Count > 0;
    }

    public static string GetUnavailableReason(PerformanceTestResult performance, string fallbackReason)
    {
        ArgumentNullException.ThrowIfNull(performance);

        if (!string.IsNullOrWhiteSpace(performance.Message))
        {
            return performance.Message;
        }

        var criticalError = performance.CriticalErrors.FirstOrDefault(error => !string.IsNullOrWhiteSpace(error));
        return criticalError ?? fallbackReason;
    }
}