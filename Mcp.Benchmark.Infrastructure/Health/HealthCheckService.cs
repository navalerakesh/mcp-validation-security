using Microsoft.Extensions.Logging;
using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.Infrastructure.Health;

/// <summary>
/// Implementation of the health check service.
/// Handles the logic for verifying server availability before full validation.
/// </summary>
public class HealthCheckService : IHealthCheckService
{
    private readonly IMcpHttpClient _httpClient;
    private readonly ILogger<HealthCheckService> _logger;
    private readonly ITelemetryService _telemetryService;

    public HealthCheckService(IMcpHttpClient httpClient, ILogger<HealthCheckService> logger, ITelemetryService telemetryService)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _telemetryService = telemetryService ?? throw new ArgumentNullException(nameof(telemetryService));
    }

    public async Task<HealthCheckResult> PerformHealthCheckAsync(McpServerConfig serverConfig, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Performing health check for server: {Server}", serverConfig.Endpoint);
        _telemetryService.TrackEvent("HealthCheckStarted", new Dictionary<string, string> { { "Endpoint", serverConfig.Endpoint ?? "Unknown" } });

        _httpClient.SetProtocolVersion(serverConfig.ProtocolVersion);
        _httpClient.SetAuthentication(serverConfig.Authentication);

        var result = new HealthCheckResult();
        var startTime = DateTime.UtcNow;

        try
        {
            // Handle STDIO transport — now supported via StdioMcpClientAdapter
            // The IMcpHttpClient may already be a StdioMcpClientAdapter that wraps process I/O.
            // We use the same ValidateInitializeAsync call since the adapter handles stdin/stdout.
            if (serverConfig.Transport?.Equals("stdio", StringComparison.OrdinalIgnoreCase) == true)
            {
                _logger.LogInformation("STDIO transport detected — performing health check via process I/O");
                
                try
                {
                    var stdioInit = await _httpClient.ValidateInitializeAsync(serverConfig.Endpoint!, cancellationToken);
                    result.InitializationDetails = stdioInit;
                    result.IsHealthy = stdioInit.IsSuccessful;
                    result.ResponseTimeMs = stdioInit.Transport.Duration.TotalMilliseconds;
                    result.ErrorMessage = stdioInit.IsSuccessful ? null : (stdioInit.Error ?? "STDIO initialize failed");
                    result.ServerVersion = stdioInit.Payload?.ServerInfo?.Version ?? "Unknown";
                    result.ProtocolVersion = stdioInit.Payload?.ProtocolVersion ?? "Unknown";

                    _logger.LogInformation("STDIO health check: {Status} ({ResponseTime:F1}ms)",
                        stdioInit.IsSuccessful ? "Healthy" : "Unhealthy", result.ResponseTimeMs);
                    return result;
                }
                catch (Exception ex)
                {
                    result.IsHealthy = false;
                    result.ErrorMessage = $"STDIO health check failed: {ex.Message}";
                    result.ResponseTimeMs = (DateTime.UtcNow - startTime).TotalMilliseconds;
                    _logger.LogWarning(ex, "STDIO health check failed");
                    return result;
                }
            }

            if (string.IsNullOrEmpty(serverConfig.Endpoint))
            {
                result.IsHealthy = false;
                result.ErrorMessage = "No endpoint specified for HTTP transport health check";
                result.ResponseTimeMs = (DateTime.UtcNow - startTime).TotalMilliseconds;
                return result;
            }

            // REAL HTTP transport health check using MCP initialize
            _logger.LogDebug("Performing REAL MCP health check via initialize handshake");
            var initResult = await _httpClient.ValidateInitializeAsync(serverConfig.Endpoint, cancellationToken);
            var initPayload = initResult.Payload;
            var responseTime = initResult.Transport.Duration.TotalMilliseconds;

            result.InitializationDetails = initResult;
            result.IsHealthy = initResult.IsSuccessful;
            result.ResponseTimeMs = responseTime;
            result.ErrorMessage = initResult.IsSuccessful ? null : initResult.Error;
            result.ServerVersion = initPayload?.ServerInfo?.Version ?? "Unknown";
            result.ProtocolVersion = initPayload?.ProtocolVersion ?? "Unknown";

            _logger.LogInformation("REAL health check completed: {Status} ({ResponseTime:F1}ms)",
                initResult.IsSuccessful ? "Healthy" : "Unhealthy", responseTime);

            _telemetryService.TrackMetric("HealthCheckDuration", result.ResponseTimeMs);
            _telemetryService.TrackEvent("HealthCheckCompleted", new Dictionary<string, string> 
            { 
                { "IsHealthy", result.IsHealthy.ToString() },
                { "ErrorMessage", result.ErrorMessage ?? "" }
            });

            return result;
        }
        catch (OperationCanceledException)
        {
            result.IsHealthy = false;
            result.ErrorMessage = "Health check timed out";
            _logger.LogWarning("Health check timed out");
            _telemetryService.TrackEvent("HealthCheckCancelled");
            return result;
        }
        catch (Exception ex)
        {
            result.IsHealthy = false;
            result.ErrorMessage = ex.Message;
            result.ResponseTimeMs = (DateTime.UtcNow - startTime).TotalMilliseconds;
            _logger.LogError(ex, "Health check failed");
            _telemetryService.TrackException(ex);
            return result;
        }
    }
}
