using System;
using Microsoft.Extensions.Logging;
using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Constants;
using Mcp.Benchmark.Core.Services;

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
            var targetConcurrency = Math.Clamp(config.MaxConcurrentConnections, 1, 512);
            var concurrentConnections = ValidationCalibration.GetInitialPerformanceProbeConcurrency(serverConfig, targetConcurrency);
            var probeRoundCount = 0;

            // Check connectivity and auth status - use provided auth if available
            var authCheckResponse = await _httpClient.CallAsync(serverConfig.Endpoint!, ValidationConstants.Methods.ToolsList, null, serverConfig.Authentication, ct);
            
            // If we get 401/403, it means either:
            // 1. No auth provided and server requires it -> Skip (Compliant)
            // 2. Auth provided but invalid -> Skip (with warning)
            var authChallenge = AuthenticationChallengeInterpreter.Inspect(authCheckResponse);
            if (authChallenge.RequiresAuthentication)
            {
                result.Status = TestStatus.Skipped;
                result.Score = ValidationCalibration.AdvisoryPerformanceScore;
                
                // Determine if this is a failure (bad token) or just enforcement (no token)
                bool isAuthFailure = serverConfig.Authentication != null && 
                                     !string.IsNullOrEmpty(serverConfig.Authentication.Token);

                string message = isAuthFailure
                    ? "SKIPPED: Authentication failed (invalid token) during performance test initialization"
                    : (authChallenge.HasWwwAuthenticateHeader 
                        ? "Server properly secured — performance testing deferred (requires authentication)"
                        : "Server requires authentication — performance testing skipped");

                result.Message = message;
                result.PerformanceBottlenecks.Add(message);
                result.Findings.Add(new ValidationFinding
                {
                    RuleId = ValidationFindingRuleIds.PerformanceAuthRequiredAdvisory,
                    Category = "Performance",
                    Component = "load-testing",
                    Severity = ValidationFindingSeverity.Info,
                    Summary = "Performance score is advisory because the endpoint requires authentication before a synthetic load probe can be run safely.",
                    Recommendation = "Run a workload-specific performance benchmark with appropriate credentials if operational capacity must be measured.",
                    Source = ValidationRuleSource.Guideline
                });
                Logger.LogInformation("Performance testing skipped: {Message}", message);
                return result;
            }
            
            LoadProbeRound? round = null;
            var calibrationAttempt = 1;
            var observedRateLimitedRequests = 0;
            var observedTransientFailures = 0;
            while (true)
            {
                round = await ExecuteLoadProbeRoundAsync(
                    serverConfig,
                    concurrentConnections,
                    ValidationCalibration.GetPerformanceProbeRequestCount(serverConfig, concurrentConnections, targetConcurrency),
                    ct);
                probeRoundCount++;
                observedRateLimitedRequests += round.RateLimitedRequests;
                observedTransientFailures += round.TransientFailures;

                if (!ValidationReliability.ShouldRetryPerformanceCalibration(
                        calibrationAttempt,
                        ValidationReliability.DefaultPerformanceCalibrationAttempts,
                        round.TotalRequests,
                        round.SuccessfulRequests,
                        round.RateLimitedRequests,
                        round.TransientFailures))
                {
                    if (ValidationCalibration.ShouldEscalatePerformanceProbe(
                            serverConfig,
                            concurrentConnections,
                            targetConcurrency,
                            round.FailedRequests,
                            round.RateLimitedRequests,
                            round.TransientFailures))
                    {
                        result.Findings.Add(new ValidationFinding
                        {
                            RuleId = ValidationFindingRuleIds.PerformancePublicRemoteRampUp,
                            Category = "Performance",
                            Component = "load-testing",
                            Severity = ValidationFindingSeverity.Info,
                            Summary = $"Public remote synthetic load probe stabilized at concurrency {concurrentConnections} before ramping to the configured target concurrency {targetConcurrency}.",
                            Recommendation = "Interpret the final round as the scored measurement; the initial round was used to avoid front-loading public endpoints with an immediate peak probe.",
                            Source = ValidationRuleSource.Guideline,
                            Metadata = new Dictionary<string, string>
                            {
                                ["initialConcurrency"] = concurrentConnections.ToString(),
                                ["targetConcurrency"] = targetConcurrency.ToString()
                            }
                        });

                        concurrentConnections = targetConcurrency;
                        continue;
                    }

                    break;
                }

                var nextConcurrency = ValidationReliability.GetReducedConcurrency(concurrentConnections);
                result.Findings.Add(new ValidationFinding
                {
                    RuleId = ValidationFindingRuleIds.PerformanceRecalibratedAfterTransientLimits,
                    Category = "Performance",
                    Component = "load-testing",
                    Severity = ValidationFindingSeverity.Info,
                    Summary = $"Synthetic load probe observed transient limits at concurrency {concurrentConnections}; retrying calibration at reduced concurrency {nextConcurrency}.",
                    Recommendation = "Interpret the final performance result using the stabilized calibration round rather than the initial throttled probe.",
                    Source = ValidationRuleSource.Guideline,
                    Metadata = new Dictionary<string, string>
                    {
                        ["attempt"] = calibrationAttempt.ToString(),
                        ["previousConcurrency"] = concurrentConnections.ToString(),
                        ["nextConcurrency"] = nextConcurrency.ToString(),
                        ["rateLimitedRequests"] = round.RateLimitedRequests.ToString(),
                        ["transientFailures"] = round.TransientFailures.ToString()
                    }
                });

                concurrentConnections = nextConcurrency;
                calibrationAttempt++;
            }

            // Calculate metrics using the shared calculator
            var finalRound = round ?? throw new InvalidOperationException("Load probe did not execute.");
            var avgResponseTime = finalRound.ResponseTimes.Count > 0 ? finalRound.ResponseTimes.Average() : 0;
            var requestsPerSecond = PerformanceMetricsCalculator.CalculateThroughput(finalRound.SuccessfulRequests, finalRound.Duration);
            var p95 = PerformanceMetricsCalculator.GetPercentile(finalRound.ResponseTimes, 0.95);
            var p99 = PerformanceMetricsCalculator.GetPercentile(finalRound.ResponseTimes, 0.99);

            result.LoadTesting = new LoadTestResult
            {
                MaxConcurrentConnections = concurrentConnections,
                TotalRequests = finalRound.TotalRequests,
                SuccessfulRequests = finalRound.SuccessfulRequests,
                FailedRequests = finalRound.FailedRequests,
                RateLimitedRequests = finalRound.RateLimitedRequests,
                ProbeRoundsExecuted = probeRoundCount,
                ObservedRateLimitedRequests = observedRateLimitedRequests,
                ObservedTransientFailures = observedTransientFailures,
                AverageResponseTimeMs = avgResponseTime,
                MedianResponseTimeMs = finalRound.ResponseTimes.Count > 0 ? finalRound.ResponseTimes.OrderBy(x => x).ElementAt(finalRound.ResponseTimes.Count / 2) : 0,
                P95ResponseTimeMs = p95,
                P99ResponseTimeMs = p99,
                MaxResponseTimeMs = finalRound.ResponseTimes.Count > 0 ? finalRound.ResponseTimes.Max() : 0,
                MinResponseTimeMs = finalRound.ResponseTimes.Count > 0 ? finalRound.ResponseTimes.Min() : 0,
                RequestsPerSecond = requestsPerSecond,
                ServerStabilityMaintained = finalRound.FailedRequests < finalRound.TotalRequests * 0.05
            };

            result.Findings.Add(new ValidationFinding
            {
                RuleId = "MCP.GUIDELINE.PERFORMANCE.SYNTHETIC_PROBE",
                Category = "Performance",
                Component = ValidationConstants.Methods.ToolsList,
                Severity = ValidationFindingSeverity.Info,
                Summary = $"Synthetic load probe executed against {ValidationConstants.Methods.ToolsList} using {finalRound.TotalRequests} requests at concurrency {concurrentConnections}.",
                Recommendation = "Interpret this as a generic pressure probe, not a workload-specific SLA benchmark.",
                Source = ValidationRuleSource.Guideline,
                Metadata = new Dictionary<string, string>
                {
                    ["requests"] = finalRound.TotalRequests.ToString(),
                    ["concurrency"] = concurrentConnections.ToString(),
                    ["operation"] = ValidationConstants.Methods.ToolsList
                }
            });

            if (result.LoadTesting.PressureSignalsObserved)
            {
                result.Findings.Add(new ValidationFinding
                {
                    RuleId = ValidationFindingRuleIds.PerformancePressureSignalsObserved,
                    Category = "Performance",
                    Component = "load-testing",
                    Severity = ValidationFindingSeverity.Info,
                    Summary = $"Synthetic load probe executed {probeRoundCount} round(s) and observed {observedRateLimitedRequests} rate-limited request(s) plus {observedTransientFailures} retryable transient failure(s) across calibration.",
                    Recommendation = "Review rate-limit and retry telemetry alongside final latency before treating the probe as representative of steady-state performance.",
                    Source = ValidationRuleSource.Guideline,
                    Metadata = new Dictionary<string, string>
                    {
                        ["probeRoundsExecuted"] = probeRoundCount.ToString(),
                        ["observedRateLimitedRequests"] = observedRateLimitedRequests.ToString(),
                        ["observedTransientFailures"] = observedTransientFailures.ToString()
                    }
                });
            }

            result.ResourceUsage = new ResourceUsageMetrics { WithinAcceptableLimits = true };
            result.Throughput = new ThroughputMetrics
            {
                RequestsPerSecond = requestsPerSecond,
                BytesPerSecond = 0, // Not measured at transport layer
                TransactionsPerMinute = requestsPerSecond * 60,
                PeakThroughput = requestsPerSecond,
                MeetsPerformanceTargets = avgResponseTime < 2000
            };

            if (ShouldTreatAsRateLimitedProfile(serverConfig, finalRound.SuccessfulRequests, finalRound.FailedRequests, finalRound.RateLimitedRequests))
            {
                result.Status = TestStatus.Skipped;
                result.Score = ValidationCalibration.AdvisoryPerformanceScore;
                result.Message = $"Load testing remained constrained by transient rate limits or transport capacity after {probeRoundCount} probe round(s); performance assessment skipped to avoid classifying remote controls as instability.";
                result.PerformanceBottlenecks.Add(result.Message);
                Logger.LogInformation("Performance testing skipped after rate limiting was observed for {Profile} profile", serverConfig.Profile);
                return result;
            }

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

                            var callLatency = callResponse.ElapsedMs ?? callSw.Elapsed.TotalMilliseconds;
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

            ValidationCalibration.ApplyPerformanceOutcomeCalibration(serverConfig, result);
            if (result.Status == TestStatus.Skipped)
            {
                return result;
            }

            result.Status = result.PerformanceBottlenecks.Count == 0 ? TestStatus.Passed : TestStatus.Failed;

            Logger.LogInformation("Load testing completed: {Requests} requests, {RPS:F1} RPS, {AvgTime:F1}ms avg response time",
                result.LoadTesting.TotalRequests, result.LoadTesting.RequestsPerSecond, result.LoadTesting.AverageResponseTimeMs);
            
            return result;
        }, cancellationToken);
    }

    private static bool ShouldTreatAsRateLimitedProfile(
        McpServerConfig serverConfig,
        int successfulRequests,
        int failedRequests,
        int rateLimitedRequests)
    {
        if (rateLimitedRequests == 0)
        {
            return false;
        }

        if (serverConfig.Profile is not (McpServerProfile.Public or McpServerProfile.Authenticated or McpServerProfile.Enterprise))
        {
            return false;
        }

        var totalRequests = successfulRequests + failedRequests;
        if (totalRequests == 0)
        {
            return false;
        }

        if (rateLimitedRequests == totalRequests)
        {
            return true;
        }

        return (double)rateLimitedRequests / totalRequests >= 0.2;
    }

    private async Task<LoadProbeRound> ExecuteLoadProbeRoundAsync(McpServerConfig serverConfig, int concurrentConnections, int totalRequests, CancellationToken ct)
    {
        var successfulRequests = 0;
        var failedRequests = 0;
        var rateLimitedRequests = 0;
        var transientFailures = 0;
        var responseTimes = new List<double>();

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

                    // Preserve sub-millisecond timing for fast local/STDIO
                    // servers while still excluding validator queueing.
                    var elapsed = response.ElapsedMs ?? 0;

                    lock (responseTimes)
                    {
                        responseTimes.Add(elapsed);
                        if (response.IsSuccess)
                        {
                            successfulRequests++;
                        }
                        else
                        {
                            failedRequests++;
                            if (response.StatusCode == 429)
                            {
                                rateLimitedRequests++;
                            }

                            if (response.StatusCode != 429 && ValidationReliability.ShouldRetryRpcResponse(response))
                            {
                                transientFailures++;
                            }
                        }
                    }
                }
                catch (Exception ex) when (ValidationReliability.ShouldRetryException(ex, ct))
                {
                    lock (responseTimes)
                    {
                        failedRequests++;
                        transientFailures++;
                    }
                }
                catch
                {
                    lock (responseTimes)
                    {
                        failedRequests++;
                    }
                }
                finally
                {
                    semaphore.Release();
                }
            }, ct));
        }

        await Task.WhenAll(tasks);
        return new LoadProbeRound(
            totalRequests,
            successfulRequests,
            failedRequests,
            rateLimitedRequests,
            transientFailures,
            responseTimes,
            DateTime.UtcNow - loadTestStart);
    }

    private sealed record LoadProbeRound(
        int TotalRequests,
        int SuccessfulRequests,
        int FailedRequests,
        int RateLimitedRequests,
        int TransientFailures,
        List<double> ResponseTimes,
        TimeSpan Duration);

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
                var responseTimes = new List<double>();
                var failureCount = 0;

                for (int i = 0; i < 10; i++)
                {
                    try
                    {
                        var sw = System.Diagnostics.Stopwatch.StartNew();
                        var response = await _httpClient.CallAsync(serverConfig.Endpoint!, operation, null, serverConfig.Authentication, ct);
                        sw.Stop();

                        responseTimes.Add(sw.Elapsed.TotalMilliseconds);
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
