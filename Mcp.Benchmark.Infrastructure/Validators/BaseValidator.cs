using Microsoft.Extensions.Logging;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Constants;

namespace Mcp.Benchmark.Infrastructure.Validators;

/// <summary>
/// Abstract base class for all validators providing common functionality.
/// Handles logging, timing, cancellation, and standard validation checks.
/// </summary>
public abstract class BaseValidator<T> where T : class
{
    protected readonly ILogger<T> Logger;

    protected BaseValidator(ILogger<T> logger)
    {
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Executes a validation operation with standard logging, timing, and error handling.
    /// </summary>
    protected async Task<TResult> ExecuteValidationAsync<TResult>(
        McpServerConfig serverConfig,
        string operationName,
        Func<CancellationToken, Task<TResult>> validationLogic,
        CancellationToken cancellationToken) 
        where TResult : TestResultBase, new()
    {
        Logger.LogInformation("Starting {Operation} for server: {Server}", operationName, serverConfig.Endpoint ?? serverConfig.Transport);
        var startTime = DateTime.UtcNow;
        var result = new TResult
        {
            Status = TestStatus.InProgress
        };

        try
        {
            // 1. Validate Basic Configuration
            if (!ValidateServerConfig(serverConfig, result))
            {
                result.Duration = DateTime.UtcNow - startTime;
                return result;
            }

            // 2. Setup Timeout
            using var timeoutTokenSource = serverConfig.TimeoutMs > 0 
                ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
                : null;
            
            if (timeoutTokenSource != null)
            {
                timeoutTokenSource.CancelAfter(TimeSpan.FromMilliseconds(serverConfig.TimeoutMs));
            }
            
            var effectiveToken = timeoutTokenSource?.Token ?? cancellationToken;

            // 3. Execute Logic
            result = await validationLogic(effectiveToken);
            
            // Ensure duration is set if not already
            if (result.Duration == TimeSpan.Zero)
            {
                result.Duration = DateTime.UtcNow - startTime;
            }

            Logger.LogInformation("{Operation} completed with status: {Status}", operationName, result.Status);
            return result;
        }
        catch (OperationCanceledException)
        {
            Logger.LogWarning("{Operation} timed out or was cancelled", operationName);
            result.Status = TestStatus.Failed; // Or Error?
            result.CriticalErrors.Add("Operation timed out or was cancelled");
            result.Duration = DateTime.UtcNow - startTime;
            return result;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "{Operation} failed unexpectedly", operationName);
            result.Status = TestStatus.Error;
            result.CriticalErrors.Add($"Unexpected error: {ex.Message}");
            result.Duration = DateTime.UtcNow - startTime;
            return result;
        }
    }

    /// <summary>
    /// Validates common server configuration requirements.
    /// </summary>
    protected virtual bool ValidateServerConfig(McpServerConfig config, TestResultBase result)
    {
        if (string.Equals(config.Transport, ValidationConstants.Transports.Stdio, StringComparison.OrdinalIgnoreCase))
        {
            // STDIO transport is now supported via StdioMcpClientAdapter.
            // The endpoint field carries the command to spawn.
            if (string.IsNullOrEmpty(config.Endpoint))
            {
                Logger.LogError("STDIO transport requires a command in the endpoint field.");
                result.Status = TestStatus.Failed;
                result.CriticalErrors.Add("STDIO transport requires a command in the endpoint field (e.g. 'npx -y @modelcontextprotocol/server-filesystem /tmp').");
                return false;
            }
            return true;
        }

        if (string.IsNullOrEmpty(config.Endpoint))
        {
            Logger.LogError(ValidationConstants.Messages.NoHttpEndpoint);
            result.Status = TestStatus.Failed;
            result.CriticalErrors.Add(ValidationConstants.Messages.NoHttpEndpoint);
            return false;
        }

        return true;
    }
}
