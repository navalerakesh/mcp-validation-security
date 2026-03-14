using System;
using Microsoft.Extensions.Logging;
using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Constants;

using Mcp.Benchmark.Infrastructure.Strategies.Scoring;
using Mcp.Benchmark.Infrastructure.Utilities;

namespace Mcp.Benchmark.Infrastructure.Validators;

/// <summary>
/// Performance validator implementation for MCP server load testing and performance benchmarking.
/// Measures server performance under various load conditions and identifies bottlenecks.
/// </summary>
public class PerformanceValidator : BaseValidator<PerformanceValidator>, IPerformanceValidator
{
    private readonly IMcpHttpClient _httpClient;
    private readonly IScoringStrategy<PerformanceTestResult> _scoringStrategy;

    /// <summary>
    /// Initializes a new instance of the PerformanceValidator class.
    /// </summary>
    public PerformanceValidator(ILogger<PerformanceValidator> logger, IMcpHttpClient httpClient) 
        : base(logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _scoringStrategy = new PerformanceScoringStrategy();
    }

    /// <summary>
    /// Performs load testing with concurrent connections and requests.
    /// </summary>
    public async Task<PerformanceTestResult> PerformLoadTestingAsync(McpServerConfig serverConfig, PerformanceTestingConfig config, CancellationToken cancellationToken = default)
    {
        return await ExecuteValidationAsync(serverConfig, "Load Testing", async (ct) =>
        {
            var result = new PerformanceTestResult();
            var concurrentConnections = Math.Clamp(config.MaxConcurrentConnections, 1, 512);
            var totalRequests = Math.Max(20, concurrentConnections * 5);

            // Check connectivity and auth status - use provided auth if available
            var authCheckResponse = await _httpClient.CallAsync(serverConfig.Endpoint!, ValidationConstants.Methods.ToolsList, null, serverConfig.Authentication, ct);
            
            // If we get 401/403, it means either:
            // 1. No auth provided and server requires it -> Skip (Compliant)
            // 2. Auth provided but invalid -> Skip (with warning)
            if (authCheckResponse.StatusCode == 401 || authCheckResponse.StatusCode == 403)
            {
                var hasWwwAuth = authCheckResponse.Headers?.ContainsKey("WWW-Authenticate") == true || 
                                 authCheckResponse.Headers?.ContainsKey("www-authenticate") == true;
                
                result.Status = TestStatus.Skipped;
                result.Score = 0.0;
                
                // Determine if this is a failure (bad token) or just enforcement (no token)
                bool isAuthFailure = serverConfig.Authentication != null && 
                                     !string.IsNullOrEmpty(serverConfig.Authentication.Token);

                string message = isAuthFailure
                    ? "SKIPPED: Authentication failed (invalid token) during performance test initialization"
                    : (hasWwwAuth 
                        ? "Server properly secured — performance testing deferred (requires authentication)"
                        : "Server requires authentication — performance testing skipped");

                result.Message = message;
                result.PerformanceBottlenecks.Add(message);
                Logger.LogInformation("Performance testing skipped: {Message}", message);
                return result;
            }
            
            // Perform basic load testing
            var successfulRequests = 0;
            var failedRequests = 0;
            var responseTimes = new List<long>();

            Logger.LogInformation("Starting load test with {Requests} requests, {Connections} concurrent connections", totalRequests, concurrentConnections);

            var loadTestStart = DateTime.UtcNow;
            var tasks = new List<Task>();
            var semaphore = new SemaphoreSlim(concurrentConnections);

            for (int i = 0; i < totalRequests; i++)
            {
                tasks.Add(Task.Run(async () =>
                {
                    await semaphore.WaitAsync(ct);
                    try
                    {
                        var response = await _httpClient.CallAsync(serverConfig.Endpoint!, ValidationConstants.Methods.ToolsList, null, serverConfig.Authentication, ct);

                        // Use HTTP-layer latency recorded by the client; fall
                        // back to 0 when unavailable. This excludes validator
                        // backoff delays and concurrency queueing.
                        var elapsed = (long)Math.Round(response.ElapsedMs ?? 0);

                        lock (responseTimes)
                        {
                            responseTimes.Add(elapsed);
                            if (response.IsSuccess) successfulRequests++;
                            else failedRequests++;
                        }
                    }
                    catch
                    {
                        lock (responseTimes) { failedRequests++; }
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }, ct));
            }

            await Task.WhenAll(tasks);
            var loadTestDuration = DateTime.UtcNow - loadTestStart;

            // Calculate metrics using the shared calculator
            var avgResponseTime = responseTimes.Count > 0 ? responseTimes.Average() : 0;
            var requestsPerSecond = PerformanceMetricsCalculator.CalculateThroughput(successfulRequests, loadTestDuration);
            var p95 = PerformanceMetricsCalculator.GetPercentile(responseTimes, 0.95);
            var p99 = PerformanceMetricsCalculator.GetPercentile(responseTimes, 0.99);

            result.LoadTesting = new LoadTestResult
            {
                MaxConcurrentConnections = concurrentConnections,
                TotalRequests = totalRequests,
                SuccessfulRequests = successfulRequests,
                FailedRequests = failedRequests,
                AverageResponseTimeMs = avgResponseTime,
                MedianResponseTimeMs = responseTimes.Count > 0 ? responseTimes.OrderBy(x => x).ElementAt(responseTimes.Count / 2) : 0,
                P95ResponseTimeMs = p95,
                P99ResponseTimeMs = p99,
                MaxResponseTimeMs = responseTimes.Count > 0 ? responseTimes.Max() : 0,
                MinResponseTimeMs = responseTimes.Count > 0 ? responseTimes.Min() : 0,
                RequestsPerSecond = requestsPerSecond,
                ServerStabilityMaintained = failedRequests < totalRequests * 0.05
            };

            result.ResourceUsage = new ResourceUsageMetrics { WithinAcceptableLimits = true };
            result.Throughput = new ThroughputMetrics
            {
                RequestsPerSecond = requestsPerSecond,
                BytesPerSecond = 0, // Not measured at transport layer
                TransactionsPerMinute = requestsPerSecond * 60,
                PeakThroughput = requestsPerSecond,
                MeetsPerformanceTargets = avgResponseTime < 2000
            };

            // Identify bottlenecks
            result.PerformanceBottlenecks.AddRange(
                PerformanceMetricsCalculator.IdentifyBottlenecks(avgResponseTime, result.LoadTesting.ErrorRate, p99));

            // Measure tools/call latency (single sequential call) if a tool exists
            try
            {
                var listResponse = await _httpClient.CallAsync(serverConfig.Endpoint!, ValidationConstants.Methods.ToolsList, null, serverConfig.Authentication, ct);
                if (listResponse.IsSuccess && !string.IsNullOrEmpty(listResponse.RawJson))
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(listResponse.RawJson);
                    if (doc.RootElement.TryGetProperty("result", out var res) &&
                        res.TryGetProperty("tools", out var tools) &&
                        tools.ValueKind == System.Text.Json.JsonValueKind.Array &&
                        tools.GetArrayLength() > 0)
                    {
                        var firstTool = tools[0];
                        if (firstTool.TryGetProperty("name", out var nameEl))
                        {
                            var toolName = nameEl.GetString();
                            var callSw = System.Diagnostics.Stopwatch.StartNew();
                            var callResponse = await _httpClient.CallAsync(
                                serverConfig.Endpoint!,
                                ValidationConstants.Methods.ToolsCall,
                                new { name = toolName, arguments = new { } },
                                serverConfig.Authentication, ct);
                            callSw.Stop();

                            var callLatency = callResponse.ElapsedMs ?? callSw.ElapsedMilliseconds;
                            result.PerformanceBottlenecks.AddRange(
                                PerformanceMetricsCalculator.IdentifyBottlenecks(0, 0, 0, callLatency, toolName));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Failed to measure tools/call latency");
            }

            // Calculate Score using Strategy
            result.Score = _scoringStrategy.CalculateScore(result);

            result.Status = result.PerformanceBottlenecks.Count == 0 ? TestStatus.Passed : TestStatus.Failed;

            Logger.LogInformation("Load testing completed: {Requests} requests, {RPS:F1} RPS, {AvgTime:F1}ms avg response time",
                result.LoadTesting.TotalRequests, result.LoadTesting.RequestsPerSecond, result.LoadTesting.AverageResponseTimeMs);
            
            return result;
        }, cancellationToken);
    }

    /// <summary>
    /// Measures response time performance for various operations.
    /// </summary>
    public async Task<PerformanceTestResult> BenchmarkResponseTimesAsync(McpServerConfig serverConfig, IEnumerable<string> operations, CancellationToken cancellationToken = default)
    {
        return await ExecuteValidationAsync(serverConfig, "Response Time Benchmarking", async (ct) =>
        {
            var result = new PerformanceTestResult();
            var operationBenchmarks = new Dictionary<string, OperationBenchmark>();
            var failedOperations = 0;

            foreach (var operation in operations)
            {
                var responseTimes = new List<long>();
                var failureCount = 0;

                for (int i = 0; i < 10; i++)
                {
                    try
                    {
                        var sw = System.Diagnostics.Stopwatch.StartNew();
                        var response = await _httpClient.CallAsync(serverConfig.Endpoint!, operation, null, serverConfig.Authentication, ct);
                        sw.Stop();

                        responseTimes.Add(sw.ElapsedMilliseconds);
                        if (!response.IsSuccess) failureCount++;
                    }
                    catch
                    {
                        failureCount++;
                    }
                }

                operationBenchmarks[operation] = new OperationBenchmark
                {
                    OperationName = operation,
                    AverageResponseTimeMs = responseTimes.Count > 0 ? responseTimes.Average() : 0,
                    MinResponseTimeMs = responseTimes.Count > 0 ? responseTimes.Min() : 0,
                    MaxResponseTimeMs = responseTimes.Count > 0 ? responseTimes.Max() : 0
                };

                failedOperations += failureCount;
            }

            result.ResponseTimes = new ResponseTimeBenchmark { OperationBenchmarks = operationBenchmarks.Values.ToList() };
            result.Status = failedOperations == 0 ? TestStatus.Passed : TestStatus.Failed;

            return result;
        }, cancellationToken);
    }

    /// <summary>
    /// Tests server behavior under resource exhaustion conditions.
    /// </summary>
    public async Task<PerformanceTestResult> TestResourceExhaustionAsync(McpServerConfig serverConfig, IEnumerable<string> resourceTypes, CancellationToken cancellationToken = default)
    {
        return await ExecuteValidationAsync(serverConfig, "Resource Exhaustion", async (ct) =>
        {
            var result = new PerformanceTestResult();
            var exhaustionEvents = new List<string>();

            foreach (var resourceType in resourceTypes)
            {
                try
                {
                    var tasks = new List<Task>();
                    for (int i = 0; i < 50; i++)
                    {
                        tasks.Add(_httpClient.CallAsync(serverConfig.Endpoint!, ValidationConstants.Methods.ToolsList, null, serverConfig.Authentication, ct));
                    }
                    await Task.WhenAll(tasks);
                }
                catch (Exception ex)
                {
                    exhaustionEvents.Add($"Resource exhaustion detected for {resourceType}: {ex.Message}");
                }
            }

            result.ResourceUsage = new ResourceUsageMetrics
            {
                WithinAcceptableLimits = exhaustionEvents.Count == 0,
                ResourceExhaustionEvents = exhaustionEvents
            };

            result.Status = exhaustionEvents.Count == 0 ? TestStatus.Passed : TestStatus.Failed;
            return result;
        }, cancellationToken);
    }
}
