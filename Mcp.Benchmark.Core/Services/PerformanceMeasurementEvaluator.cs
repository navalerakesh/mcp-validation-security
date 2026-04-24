using System.Linq;
using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.Core.Services;

public static class PerformanceMeasurementEvaluator
{
    public static bool HasObservedMetrics(PerformanceTestResult performance)
    {
        ArgumentNullException.ThrowIfNull(performance);

        return performance.LoadTesting.SuccessfulRequests > 0
            || performance.LoadTesting.ConnectionErrors.Count > 0
            || performance.ResponseTimes.OperationBenchmarks.Count > 0
            || performance.ResponseTimes.OverallAverageResponseTimeMs > 0
            || performance.LoadTesting.AverageResponseTimeMs > 0
            || performance.LoadTesting.MedianResponseTimeMs > 0
            || performance.LoadTesting.P95ResponseTimeMs > 0
            || performance.LoadTesting.P99ResponseTimeMs > 0
            || performance.LoadTesting.MaxResponseTimeMs > 0
            || performance.LoadTesting.MinResponseTimeMs > 0
            || performance.LoadTesting.RequestsPerSecond > 0
            || performance.Throughput.RequestsPerSecond > 0
            || performance.Throughput.BytesPerSecond > 0
            || performance.Throughput.TransactionsPerMinute > 0
            || performance.Throughput.PeakThroughput > 0
            || performance.ResourceUsage.Network.AverageNetworkLatencyMs > 0
            || performance.ResourceUsage.Network.TotalBytesSent > 0
            || performance.ResourceUsage.Network.TotalBytesReceived > 0;
    }

    public static string GetUnavailableReason(PerformanceTestResult performance, string fallbackReason)
    {
        ArgumentNullException.ThrowIfNull(performance);

        if (!string.IsNullOrWhiteSpace(performance.Message))
        {
            return performance.Message;
        }

        var criticalError = performance.CriticalErrors.FirstOrDefault(error => !string.IsNullOrWhiteSpace(error));
        if (!string.IsNullOrWhiteSpace(criticalError))
        {
            return criticalError;
        }

        if (performance.LoadTesting.TotalRequests > 0 &&
            performance.LoadTesting.SuccessfulRequests == 0 &&
            !HasObservedMetrics(performance))
        {
            return $"Performance probe attempted {performance.LoadTesting.TotalRequests} request(s) but captured no timing samples because every request failed.";
        }

        return fallbackReason;
    }
}