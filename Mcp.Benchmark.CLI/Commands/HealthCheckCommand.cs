using System.Net.Http;
using System.Text.Json;
using Mcp.Benchmark.CLI.Abstractions;
using Mcp.Benchmark.CLI.Exceptions;
using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Resources;
using Mcp.Benchmark.Core.Services;
using Microsoft.Extensions.Logging;

namespace Mcp.Benchmark.CLI;

/// <summary>
/// Command handler for performing quick health checks on MCP servers.
/// Provides fast connectivity and basic functionality verification.
/// </summary>
/// <remarks>
/// Initializes a new instance of the HealthCheckCommand class.
/// </remarks>
/// <param name="validatorService">The validator service for performing health checks.</param>
/// <param name="consoleOutput">Professional console output service.</param>
/// <param name="logger">Logger instance for command execution logging.</param>
public class HealthCheckCommand(
    IMcpValidatorService validatorService,
    IConsoleOutputService consoleOutput,
    ILogger<HealthCheckCommand> logger,
    INextStepAdvisor nextStepAdvisor,
    ISessionArtifactStore artifactStore)
{
    private readonly IMcpValidatorService _validatorService = validatorService ?? throw new ArgumentNullException(nameof(validatorService));
    private readonly IConsoleOutputService _consoleOutput = consoleOutput ?? throw new ArgumentNullException(nameof(consoleOutput));
    private readonly ILogger<HealthCheckCommand> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly INextStepAdvisor _nextStepAdvisor = nextStepAdvisor ?? throw new ArgumentNullException(nameof(nextStepAdvisor));
    private readonly ISessionArtifactStore _artifactStore = artifactStore ?? throw new ArgumentNullException(nameof(artifactStore));

    /// <summary>
    /// Executes the health check command with the specified parameters.
    /// </summary>
    /// <param name="server">The MCP server endpoint or configuration identifier.</param>
    /// <param name="timeoutMs">Timeout for the health check operation in milliseconds.</param>
    /// <param name="configFile">Optional configuration file for server connection details.</param>
    /// <param name="verbose">Enable verbose logging output.</param>
    /// <returns>Task representing the asynchronous health check operation.</returns>
    public async Task ExecuteAsync(
        string? server,
        int timeoutMs,
        FileInfo? configFile,
        bool verbose,
        string? token,
        bool interactive,
        string? serverProfile)
    {
        _nextStepAdvisor.Reset();
        var targetEndpoint = server;

        try
        {
            if (string.IsNullOrWhiteSpace(server) && (configFile == null || !configFile.Exists))
            {
                throw new CliUsageException($"{ValidationMessages.Errors.InvalidConfiguration}: server or configuration file is required.");
            }

            _logger.LogInformation("Performing health check for server: {Server}", server ?? configFile?.FullName);

            var profileOverride = CommandConnectionHelper.ParseServerProfile(serverProfile);

            // Create server configuration
            var serverConfig = await CreateServerConfigAsync(server, configFile, timeoutMs, profileOverride);
            targetEndpoint = serverConfig.Endpoint ?? targetEndpoint;

            if (!CommandConnectionHelper.EnsureAuthenticationPrerequisites(
                    _consoleOutput,
                    _nextStepAdvisor,
                    "health-check",
                    serverConfig,
                    token,
                    interactive,
                    targetEndpoint))
            {
                Environment.ExitCode = 2;
                return;
            }

            CommandConnectionHelper.ApplyAuthenticationOverrides(serverConfig, token, interactive);

            // Display professional validation plan
            _consoleOutput.DisplayValidationPlan(ValidationMessages.Titles.HealthCheck, serverConfig);

            // Show configuration loading status
            _consoleOutput.DisplayConfigurationStatus(configFile?.FullName, configFile?.Exists ?? false);

            // Execute health check with progress indication
            _consoleOutput.ShowProgress(ValidationMessages.Progress.PerformingHealthCheck);

            using var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromMilliseconds(timeoutMs));

            var startTime = DateTime.UtcNow;
            var result = await _validatorService.PerformHealthCheckAsync(serverConfig, cancellationTokenSource.Token);
            var totalTime = DateTime.UtcNow - startTime;

            // Display professional results
            _consoleOutput.DisplayHealthCheckResults(result, totalTime, verbose);
            PersistSessionArtifact("health-check", serverConfig.CloneWithoutSecrets(), result, verbose);

            if (!result.IsHealthy && IsAuthenticationFailure(result))
            {
                _nextStepAdvisor.SuggestAuthenticationFlow("health-check", targetEndpoint);
            }

            if (!result.IsHealthy)
            {
                _consoleOutput.WriteSessionLogHint("Health-check log");
                _nextStepAdvisor.SuggestSessionLogReview("Health-check log");
            }

            // Set exit code based on health check result
            Environment.ExitCode = result.IsHealthy ? 0 : 1;
        }
        catch (CliExceptionBase cliEx)
        {
            _consoleOutput.WriteError(cliEx.Message);
            _consoleOutput.WriteSessionLogHint("Health-check log");
            _nextStepAdvisor.SuggestSessionLogReview("Health-check log");
            _logger.LogWarning(cliEx, "Health check aborted: {Message}", cliEx.Message);
            Environment.ExitCode = cliEx.ExitCode;
        }
        catch (OperationCanceledException)
        {
            _consoleOutput.WriteError(ValidationMessages.Errors.TimeoutOccurred);
            _consoleOutput.WriteSessionLogHint("Health-check log");
            _nextStepAdvisor.SuggestSessionLogReview("Health-check log");
            _logger.LogWarning("Health check timed out after {Timeout}ms", timeoutMs);
            Environment.ExitCode = 124; // Timeout exit code
        }
        catch (Exception ex)
        {
            _consoleOutput.WriteError($"{ValidationMessages.Errors.UnexpectedError}: {ex.Message}");
            _consoleOutput.WriteSessionLogHint("Health-check log");
            _nextStepAdvisor.SuggestSessionLogReview("Health-check log");
            _logger.LogError(ex, "Health check failed with error: {Message}", ex.Message);
            if (IsAuthenticationFailure(ex))
            {
                _nextStepAdvisor.SuggestAuthenticationFlow("health-check", targetEndpoint);
            }
            Environment.ExitCode = 1;
        }
        finally
        {
            _nextStepAdvisor.Render();
        }
    }

    /// <summary>
    /// Creates a server configuration from the provided parameters and optional config file.
    /// </summary>
    /// <param name="server">The server endpoint or identifier.</param>
    /// <param name="configFile">Optional configuration file.</param>
    /// <param name="timeoutMs">Timeout value in milliseconds.</param>
    /// <returns>Configured server settings.</returns>
    private async Task<McpServerConfig> CreateServerConfigAsync(
        string? server,
        FileInfo? configFile,
        int timeoutMs,
        McpServerProfile? profileOverride)
    {
        McpServerConfig serverConfig;

        if (configFile?.Exists == true)
        {
            _logger.LogDebug("Loading server configuration from: {ConfigFile}", configFile.FullName);
            var configJson = await File.ReadAllTextAsync(configFile.FullName);
            var fullConfig = JsonSerializer.Deserialize<McpValidatorConfiguration>(configJson);
            serverConfig = fullConfig?.Server ?? new McpServerConfig();
        }
        else
        {
            serverConfig = new McpServerConfig();
        }

        // Override with command-line parameters when provided
        if (!string.IsNullOrWhiteSpace(server))
        {
            serverConfig.Endpoint = server;
        }

        serverConfig.TimeoutMs = timeoutMs;

        // Auto-detect transport from URL
        if (!string.IsNullOrWhiteSpace(server) && Uri.TryCreate(server, UriKind.Absolute, out var uri))
        {
            if (uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase) ||
                uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase))
            {
                serverConfig.Transport = "http";
            }
            else if (uri.Scheme.Equals("ws", StringComparison.OrdinalIgnoreCase) ||
                     uri.Scheme.Equals("wss", StringComparison.OrdinalIgnoreCase))
            {
                serverConfig.Transport = "websocket";
            }
        }
        else if (!string.IsNullOrWhiteSpace(server))
        {
            // Fallback simple check
            if (server.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                server.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                serverConfig.Transport = "http";
            }
        }

        CommandConnectionHelper.ApplyServerProfileOverride(serverConfig, profileOverride);

        return serverConfig;
    }

    private static bool IsAuthenticationFailure(HealthCheckResult result)
    {
        return ValidationReliability.IsAuthenticationFailure(result);
    }

    private static bool IsAuthenticationFailure(Exception exception)
    {
        return ValidationReliability.IsAuthenticationFailure(exception);
    }

    private void PersistSessionArtifact(string artifactName, McpServerConfig serverConfig, HealthCheckResult result, bool verbose)
    {
        var payload = new
        {
            timestamp = DateTimeOffset.UtcNow,
            server = serverConfig,
            result,
            verbose
        };

        _artifactStore.TrySaveJson($"{artifactName}-results", payload);
    }
}

/// <summary>
/// Command handler for discovering MCP server capabilities and features.
/// Provides detailed information about server functionality and supported operations.
/// </summary>
public class DiscoverCommand
{
    private readonly IMcpValidatorService _validatorService;
    private readonly IConsoleOutputService _consoleOutput;
    private readonly ILogger<DiscoverCommand> _logger;
    private readonly INextStepAdvisor _nextStepAdvisor;
    private readonly ISessionArtifactStore _artifactStore;

    /// <summary>
    /// Initializes a new instance of the DiscoverCommand class.
    /// </summary>
    /// <param name="validatorService">The validator service for performing capability discovery.</param>
    /// <param name="consoleOutput">Professional console output service.</param>
    /// <param name="logger">Logger instance for command execution logging.</param>
    public DiscoverCommand(
        IMcpValidatorService validatorService,
        IConsoleOutputService consoleOutput,
        ILogger<DiscoverCommand> logger,
        INextStepAdvisor nextStepAdvisor,
        ISessionArtifactStore artifactStore)
    {
        _validatorService = validatorService ?? throw new ArgumentNullException(nameof(validatorService));
        _consoleOutput = consoleOutput ?? throw new ArgumentNullException(nameof(consoleOutput));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _nextStepAdvisor = nextStepAdvisor ?? throw new ArgumentNullException(nameof(nextStepAdvisor));
        _artifactStore = artifactStore ?? throw new ArgumentNullException(nameof(artifactStore));
    }

    /// <summary>
    /// Executes the discover command with the specified parameters.
    /// </summary>
    /// <param name="server">The MCP server endpoint or configuration identifier.</param>
    /// <param name="format">Output format for the discovery results (json, yaml, table).</param>
    /// <param name="configFile">Optional configuration file for server connection details.</param>
    /// <param name="verbose">Enable verbose logging output.</param>
    /// <returns>Task representing the asynchronous discovery operation.</returns>
    public async Task ExecuteAsync(
        string? server,
        string format,
        FileInfo? configFile,
        bool verbose,
        string? token,
        bool interactive,
        string? serverProfile)
    {
        _nextStepAdvisor.Reset();
        var targetEndpoint = server;
        try
        {
            if (string.IsNullOrWhiteSpace(server) && (configFile == null || !configFile.Exists))
            {
                throw new CliUsageException($"{ValidationMessages.Errors.InvalidConfiguration}: server or configuration file is required.");
            }

            _logger.LogInformation("Discovering capabilities for server: {Server}", server ?? configFile?.FullName);

            var profileOverride = CommandConnectionHelper.ParseServerProfile(serverProfile);

            // Create server configuration
            var serverConfig = await CreateServerConfigAsync(server, configFile, profileOverride);
            targetEndpoint = serverConfig.Endpoint ?? targetEndpoint;

            if (!CommandConnectionHelper.EnsureAuthenticationPrerequisites(
                    _consoleOutput,
                    _nextStepAdvisor,
                    "discover",
                    serverConfig,
                    token,
                    interactive,
                    targetEndpoint))
            {
                Environment.ExitCode = 2;
                return;
            }

            CommandConnectionHelper.ApplyAuthenticationOverrides(serverConfig, token, interactive);

            // Display discovery plan
            _consoleOutput.DisplayDiscoveryPlan(serverConfig, format);

            // Execute capability discovery
            using var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromMinutes(2));

            var capabilities = await _validatorService.DiscoverServerCapabilitiesAsync(serverConfig, cancellationTokenSource.Token);

            // Display results in the requested format
            _consoleOutput.DisplayServerCapabilities(capabilities, format, verbose);

            PersistSessionArtifact(serverConfig.CloneWithoutSecrets(), capabilities, format);

            Environment.ExitCode = 0;
        }
        catch (CliExceptionBase cliEx)
        {
            _consoleOutput.WriteError(cliEx.Message);
            _consoleOutput.WriteSessionLogHint("Discovery log");
            _nextStepAdvisor.SuggestSessionLogReview("Discovery log");
            _logger.LogWarning(cliEx, "Discovery aborted: {Message}", cliEx.Message);
            Environment.ExitCode = cliEx.ExitCode;
        }
        catch (OperationCanceledException)
        {
            _consoleOutput.WriteError(ValidationMessages.Errors.TimeoutOccurred);
            _consoleOutput.WriteSessionLogHint("Discovery log");
            _nextStepAdvisor.SuggestSessionLogReview("Discovery log");
            _logger.LogWarning("Discovery operation timed out");
            Environment.ExitCode = 124;
        }
        catch (Exception ex)
        {
            _consoleOutput.WriteError($"{ValidationMessages.Errors.UnexpectedError}: {ex.Message}");
            _consoleOutput.WriteSessionLogHint("Discovery log");
            _nextStepAdvisor.SuggestSessionLogReview("Discovery log");
            _logger.LogError(ex, "Discovery failed with error: {Message}", ex.Message);
            if (IsAuthenticationFailure(ex))
            {
                _nextStepAdvisor.SuggestAuthenticationFlow("discover", targetEndpoint);
            }
            Environment.ExitCode = 1;
        }
        finally
        {
            _nextStepAdvisor.Render();
        }
    }

    private void PersistSessionArtifact(McpServerConfig serverConfig, ServerCapabilities capabilities, string format)
    {
        _artifactStore.TrySaveJson(
            "discover-results",
            new
            {
                timestamp = DateTimeOffset.UtcNow,
                server = serverConfig,
                format,
                capabilities
            });
    }

    /// <summary>
    /// Creates a server configuration from the provided parameters and optional config file.
    /// </summary>
    /// <param name="server">The server endpoint or identifier.</param>
    /// <param name="configFile">Optional configuration file.</param>
    /// <returns>Configured server settings.</returns>
    private async Task<McpServerConfig> CreateServerConfigAsync(
        string? server,
        FileInfo? configFile,
        McpServerProfile? profileOverride)
    {
        McpServerConfig serverConfig;

        if (configFile?.Exists == true)
        {
            _logger.LogDebug("Loading server configuration from: {ConfigFile}", configFile.FullName);
            var configJson = await File.ReadAllTextAsync(configFile.FullName);
            var fullConfig = JsonSerializer.Deserialize<McpValidatorConfiguration>(configJson);
            serverConfig = fullConfig?.Server ?? new McpServerConfig();
        }
        else
        {
            serverConfig = new McpServerConfig();
        }

        if (!string.IsNullOrWhiteSpace(server))
        {
            serverConfig.Endpoint = server;
        }

        // Auto-detect transport from URL
        if (!string.IsNullOrWhiteSpace(server) && Uri.TryCreate(server, UriKind.Absolute, out var uri))
        {
            if (uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase) ||
                uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase))
            {
                serverConfig.Transport = "http";
            }
            else if (uri.Scheme.Equals("ws", StringComparison.OrdinalIgnoreCase) ||
                     uri.Scheme.Equals("wss", StringComparison.OrdinalIgnoreCase))
            {
                serverConfig.Transport = "websocket";
            }
        }
        else if (!string.IsNullOrWhiteSpace(server))
        {
            // Fallback simple check
            if (server.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                server.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                serverConfig.Transport = "http";
            }
        }

        CommandConnectionHelper.ApplyServerProfileOverride(serverConfig, profileOverride);

        return serverConfig;
    }

    private static bool IsAuthenticationFailure(Exception exception)
    {
        return ValidationReliability.IsAuthenticationFailure(exception);
    }
}

internal static class CommandConnectionHelper
{
    public static McpServerProfile? ParseServerProfile(string? rawProfile)
    {
        if (string.IsNullOrWhiteSpace(rawProfile))
        {
            return null;
        }

        return Enum.TryParse<McpServerProfile>(rawProfile, ignoreCase: true, out var parsed)
            ? parsed
            : null;
    }

    public static void ApplyServerProfileOverride(McpServerConfig serverConfig, McpServerProfile? overrideProfile)
    {
        if (overrideProfile.HasValue)
        {
            serverConfig.Profile = overrideProfile.Value;
        }
    }

    public static bool EnsureAuthenticationPrerequisites(
        IConsoleOutputService consoleOutput,
        INextStepAdvisor advisor,
        string commandName,
        McpServerConfig serverConfig,
        string? token,
        bool interactive,
        string? endpointHint)
    {
        if (!RequiresStrictAuthentication(serverConfig.Profile))
        {
            return true;
        }

        var hasToken = !string.IsNullOrWhiteSpace(token) ||
                       !string.IsNullOrWhiteSpace(serverConfig.Authentication?.Token);

        if (hasToken || interactive)
        {
            return true;
        }

        consoleOutput.WriteError("Authenticated or enterprise servers require credentials. Provide -t/--token or enable -i/--interactive.");
        advisor.SuggestAuthenticationFlow(commandName, endpointHint);
        return false;
    }

    public static void ApplyAuthenticationOverrides(McpServerConfig serverConfig, string? token, bool interactive)
    {
        if (string.IsNullOrWhiteSpace(token) && !interactive)
        {
            return;
        }

        var auth = serverConfig.Authentication ??= new AuthenticationConfig();

        if (!string.IsNullOrWhiteSpace(token))
        {
            auth.Token = token;
            auth.Type = string.IsNullOrWhiteSpace(auth.Type) || auth.Type == "none"
                ? "bearer"
                : auth.Type;
            auth.Required = true;
        }

        auth.AllowInteractive = interactive;
    }

    private static bool RequiresStrictAuthentication(McpServerProfile profile)
    {
        return profile == McpServerProfile.Authenticated || profile == McpServerProfile.Enterprise;
    }
}
