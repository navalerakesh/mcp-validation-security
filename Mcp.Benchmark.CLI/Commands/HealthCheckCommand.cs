using System.Net.Http;
using System.Text.Json;
using Mcp.Benchmark.CLI.Abstractions;
using Mcp.Benchmark.CLI.Exceptions;
using Mcp.Benchmark.CLI.Models;
using Mcp.Benchmark.CLI.Utilities;
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
    IExecutionGovernanceService executionGovernanceService,
    ISessionArtifactStore artifactStore,
    IMcpHttpClient httpClient,
    CliSessionContext sessionContext)
{
    private readonly IMcpValidatorService _validatorService = validatorService ?? throw new ArgumentNullException(nameof(validatorService));
    private readonly IConsoleOutputService _consoleOutput = consoleOutput ?? throw new ArgumentNullException(nameof(consoleOutput));
    private readonly ILogger<HealthCheckCommand> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly INextStepAdvisor _nextStepAdvisor = nextStepAdvisor ?? throw new ArgumentNullException(nameof(nextStepAdvisor));
    private readonly IExecutionGovernanceService _executionGovernanceService = executionGovernanceService ?? throw new ArgumentNullException(nameof(executionGovernanceService));
    private readonly ISessionArtifactStore _artifactStore = artifactStore ?? throw new ArgumentNullException(nameof(artifactStore));
    private readonly IMcpHttpClient _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    private readonly CliSessionContext _sessionContext = sessionContext ?? throw new ArgumentNullException(nameof(sessionContext));

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
        string? serverProfile,
        string? executionMode = null,
        bool? dryRun = null,
        string[]? allowedHosts = null,
        bool? allowPrivateAddresses = null,
        int? maxRequests = null,
        string? persistenceMode = null,
        string? redactLevel = null,
        string? traceMode = null,
        bool? confirmElevatedRisk = null)
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

            var configuration = await CommandConnectionHelper.CreateCommandConfigurationAsync(server, configFile, timeoutMs, profileOverride);
            ExecutionPolicyOverrides.Apply(
                configuration,
                executionMode: executionMode,
                dryRun: dryRun,
                allowedHosts: allowedHosts,
                allowPrivateAddresses: allowPrivateAddresses,
                maxRequests: maxRequests,
                timeoutSeconds: (int)Math.Ceiling(Math.Max(1, timeoutMs) / 1000d),
                persistenceMode: persistenceMode,
                redactLevel: redactLevel,
                traceMode: traceMode,
                confirmElevatedRisk: confirmElevatedRisk);

            var executionPolicy = configuration.Execution ?? new ExecutionPolicy();
            var serverConfig = configuration.Server;
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

            _sessionContext.ApplyExecutionPolicy(executionPolicy);
            var executionPlan = _executionGovernanceService.BuildCommandPlan(
                _sessionContext,
                "health-check",
                serverConfig,
                executionPolicy,
                outputDirectory: null,
                plannedChecks: ["health-check"],
                plannedArtifacts: BuildPlannedArtifacts("health-check", executionPolicy));

            if (!executionPlan.IsValid)
            {
                throw new CliUsageException(CommandConnectionHelper.BuildExecutionErrorMessage(executionPlan.ValidationErrors));
            }

            CommandConnectionHelper.DisplayExecutionPlan(_consoleOutput, executionPlan);

            if (executionPlan.DryRun)
            {
                var dryRunArtifactPaths = PersistAuditManifest("health-check", executionPlan, Array.Empty<string>());
                if (dryRunArtifactPaths.Count > 0)
                {
                    _consoleOutput.WriteSuccess($"Operational artifacts generated: {string.Join(", ", dryRunArtifactPaths)}");
                }

                _consoleOutput.WriteSuccess("Dry run complete. No requests were sent.");
                Environment.ExitCode = 0;
                return;
            }

            var transportExecutionPolicy = executionPolicy.Clone();
            transportExecutionPolicy.AllowedHosts = executionPlan.AllowedHosts.ToList();
            _httpClient.ConfigureExecutionPolicy(transportExecutionPolicy);

            // Display professional validation plan
            _consoleOutput.DisplayValidationPlan(ValidationMessages.Titles.HealthCheck, serverConfig);

            // Show configuration loading status
            _consoleOutput.DisplayConfigurationStatus(configFile?.FullName, configFile?.Exists ?? false);

            // Execute health check with progress indication
            _consoleOutput.ShowProgress(ValidationMessages.Progress.PerformingHealthCheck);

            using var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(executionPlan.TimeoutSeconds));

            var startTime = DateTime.UtcNow;
            var result = await _validatorService.PerformHealthCheckAsync(serverConfig, cancellationTokenSource.Token);
            var totalTime = DateTime.UtcNow - startTime;

            // Display professional results
            _consoleOutput.DisplayHealthCheckResults(result, totalTime, verbose);
            var artifactPaths = PersistSessionArtifact("health-check", serverConfig.CloneWithoutSecrets(), result, verbose);
            PersistAuditManifest("health-check", executionPlan, artifactPaths);

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
    private static bool IsAuthenticationFailure(HealthCheckResult result)
    {
        return ValidationReliability.IsAuthenticationFailure(result);
    }

    private static bool IsAuthenticationFailure(Exception exception)
    {
        return ValidationReliability.IsAuthenticationFailure(exception);
    }

    private IReadOnlyList<string> PersistSessionArtifact(string artifactName, McpServerConfig serverConfig, HealthCheckResult result, bool verbose)
    {
        var payload = new
        {
            timestamp = DateTimeOffset.UtcNow,
            server = serverConfig,
            result,
            verbose
        };

        var artifactPath = _artifactStore.TrySaveJson($"{artifactName}-results", payload);
        return string.IsNullOrWhiteSpace(artifactPath)
            ? Array.Empty<string>()
            : [artifactPath];
    }

    private IReadOnlyList<string> PersistAuditManifest(string artifactName, ExecutionPlan executionPlan, IReadOnlyList<string> artifactPaths)
    {
        var auditManifest = _executionGovernanceService.BuildAuditManifest(executionPlan, result: null, artifactPaths, modelEvaluationArtifact: null);
        var auditPath = _artifactStore.TrySaveJson($"{artifactName}-audit", auditManifest);

        if (string.IsNullOrWhiteSpace(auditPath))
        {
            return artifactPaths;
        }

        return artifactPaths.Concat([auditPath]).ToArray();
    }

    private static IReadOnlyList<string> BuildPlannedArtifacts(string artifactName, ExecutionPolicy executionPolicy)
    {
        if (executionPolicy.PersistenceMode != PersistenceMode.Session)
        {
            return Array.Empty<string>();
        }

        return ["session-log", $"{artifactName}-result", $"{artifactName}-audit"];
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
        IExecutionGovernanceService executionGovernanceService,
        ISessionArtifactStore artifactStore,
        IMcpHttpClient httpClient,
        CliSessionContext sessionContext)
    {
        _validatorService = validatorService ?? throw new ArgumentNullException(nameof(validatorService));
        _consoleOutput = consoleOutput ?? throw new ArgumentNullException(nameof(consoleOutput));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _nextStepAdvisor = nextStepAdvisor ?? throw new ArgumentNullException(nameof(nextStepAdvisor));
        _executionGovernanceService = executionGovernanceService ?? throw new ArgumentNullException(nameof(executionGovernanceService));
        _artifactStore = artifactStore ?? throw new ArgumentNullException(nameof(artifactStore));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _sessionContext = sessionContext ?? throw new ArgumentNullException(nameof(sessionContext));
    }

    private readonly IExecutionGovernanceService _executionGovernanceService;
    private readonly IMcpHttpClient _httpClient;
    private readonly CliSessionContext _sessionContext;

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
        int timeoutMs,
        FileInfo? configFile,
        bool verbose,
        string? token,
        bool interactive,
        string? serverProfile,
        string? executionMode = null,
        bool? dryRun = null,
        string[]? allowedHosts = null,
        bool? allowPrivateAddresses = null,
        int? maxRequests = null,
        string? persistenceMode = null,
        string? redactLevel = null,
        string? traceMode = null,
        bool? confirmElevatedRisk = null)
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

            var configuration = await CommandConnectionHelper.CreateCommandConfigurationAsync(server, configFile, timeoutMs, profileOverride);
            ExecutionPolicyOverrides.Apply(
                configuration,
                executionMode: executionMode,
                dryRun: dryRun,
                allowedHosts: allowedHosts,
                allowPrivateAddresses: allowPrivateAddresses,
                maxRequests: maxRequests,
                timeoutSeconds: (int)Math.Ceiling(Math.Max(1, timeoutMs) / 1000d),
                persistenceMode: persistenceMode,
                redactLevel: redactLevel,
                traceMode: traceMode,
                confirmElevatedRisk: confirmElevatedRisk);

            var executionPolicy = configuration.Execution ?? new ExecutionPolicy();
            var serverConfig = configuration.Server;
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

            _sessionContext.ApplyExecutionPolicy(executionPolicy);
            var executionPlan = _executionGovernanceService.BuildCommandPlan(
                _sessionContext,
                "discover",
                serverConfig,
                executionPolicy,
                outputDirectory: null,
                plannedChecks: ["discover"],
                plannedArtifacts: BuildPlannedArtifacts("discover", executionPolicy));

            if (!executionPlan.IsValid)
            {
                throw new CliUsageException(CommandConnectionHelper.BuildExecutionErrorMessage(executionPlan.ValidationErrors));
            }

            CommandConnectionHelper.DisplayExecutionPlan(_consoleOutput, executionPlan);

            if (executionPlan.DryRun)
            {
                var dryRunArtifactPaths = PersistAuditManifest("discover", executionPlan, Array.Empty<string>());
                if (dryRunArtifactPaths.Count > 0)
                {
                    _consoleOutput.WriteSuccess($"Operational artifacts generated: {string.Join(", ", dryRunArtifactPaths)}");
                }

                _consoleOutput.WriteSuccess("Dry run complete. No requests were sent.");
                Environment.ExitCode = 0;
                return;
            }

            var transportExecutionPolicy = executionPolicy.Clone();
            transportExecutionPolicy.AllowedHosts = executionPlan.AllowedHosts.ToList();
            _httpClient.ConfigureExecutionPolicy(transportExecutionPolicy);

            // Display discovery plan
            _consoleOutput.DisplayDiscoveryPlan(serverConfig, format);

            // Execute capability discovery
            using var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(executionPlan.TimeoutSeconds));

            var capabilities = await _validatorService.DiscoverServerCapabilitiesAsync(serverConfig, cancellationTokenSource.Token);

            // Display results in the requested format
            _consoleOutput.DisplayServerCapabilities(capabilities, format, verbose);

            var artifactPaths = PersistSessionArtifact(serverConfig.CloneWithoutSecrets(), capabilities, format);
            PersistAuditManifest("discover", executionPlan, artifactPaths);

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

    private IReadOnlyList<string> PersistSessionArtifact(McpServerConfig serverConfig, ServerCapabilities capabilities, string format)
    {
        var artifactPath = _artifactStore.TrySaveJson(
            "discover-results",
            new
            {
                timestamp = DateTimeOffset.UtcNow,
                server = serverConfig,
                format,
                capabilities
            });

        return string.IsNullOrWhiteSpace(artifactPath)
            ? Array.Empty<string>()
            : [artifactPath];
    }

    /// <summary>
    /// Creates a server configuration from the provided parameters and optional config file.
    /// </summary>
    /// <param name="server">The server endpoint or identifier.</param>
    /// <param name="configFile">Optional configuration file.</param>
    /// <returns>Configured server settings.</returns>
    private IReadOnlyList<string> PersistAuditManifest(string artifactName, ExecutionPlan executionPlan, IReadOnlyList<string> artifactPaths)
    {
        var auditManifest = _executionGovernanceService.BuildAuditManifest(executionPlan, result: null, artifactPaths, modelEvaluationArtifact: null);
        var auditPath = _artifactStore.TrySaveJson($"{artifactName}-audit", auditManifest);

        if (string.IsNullOrWhiteSpace(auditPath))
        {
            return artifactPaths;
        }

        return artifactPaths.Concat([auditPath]).ToArray();
    }

    private static IReadOnlyList<string> BuildPlannedArtifacts(string artifactName, ExecutionPolicy executionPolicy)
    {
        if (executionPolicy.PersistenceMode != PersistenceMode.Session)
        {
            return Array.Empty<string>();
        }

        return ["session-log", $"{artifactName}-result", $"{artifactName}-audit"];
    }

    private static bool IsAuthenticationFailure(Exception exception)
    {
        return ValidationReliability.IsAuthenticationFailure(exception);
    }
}

internal static class CommandConnectionHelper
{
    public static async Task<McpValidatorConfiguration> CreateCommandConfigurationAsync(
        string? server,
        FileInfo? configFile,
        int timeoutMs,
        McpServerProfile? profileOverride)
    {
        McpValidatorConfiguration configuration;

        if (configFile?.Exists == true)
        {
            var configJson = await File.ReadAllTextAsync(configFile.FullName);
            configuration = JsonSerializer.Deserialize<McpValidatorConfiguration>(configJson) ?? new McpValidatorConfiguration();
        }
        else
        {
            configuration = new McpValidatorConfiguration();
        }

        if (!string.IsNullOrWhiteSpace(server))
        {
            configuration.Server.Endpoint = server;
        }

        configuration.Server.TimeoutMs = timeoutMs;
        ApplyTransportOverride(configuration.Server, server);
        ApplyServerProfileOverride(configuration.Server, profileOverride);
        configuration.Execution ??= new ExecutionPolicy();
        return configuration;
    }

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

    public static void DisplayExecutionPlan(IConsoleOutputService consoleOutput, ExecutionPlan executionPlan)
    {
        consoleOutput.DisplaySessionBanner();
        Console.WriteLine();
        consoleOutput.WriteHeader("EXECUTION PLAN");
        Console.WriteLine($"Command: {executionPlan.CommandName}");
        Console.WriteLine($"Target: {executionPlan.Target}");
        Console.WriteLine($"Transport: {executionPlan.Transport}");
        Console.WriteLine($"Mode: {executionPlan.ExecutionMode}");
        Console.WriteLine($"Dry Run: {(executionPlan.DryRun ? "enabled" : "disabled")}");
        Console.WriteLine($"Persistence: {executionPlan.PersistenceMode}");
        Console.WriteLine($"Redaction: {executionPlan.RedactionLevel}");
        Console.WriteLine($"Trace: {executionPlan.TraceMode}");
        Console.WriteLine($"Timeout: {executionPlan.TimeoutSeconds}s per request");
        Console.WriteLine($"Request Budget: {executionPlan.MaxRequests}");
        Console.WriteLine($"Concurrency: {executionPlan.MaxConcurrency}");
        Console.WriteLine($"Allowed Hosts: {(executionPlan.AllowedHosts.Count == 0 ? "(target host only)" : string.Join(", ", executionPlan.AllowedHosts))}");
        Console.WriteLine($"Private Addresses: {(executionPlan.AllowPrivateAddresses ? "allowed" : "blocked")}");

        if (executionPlan.OutputDirectory != null)
        {
            Console.WriteLine($"Output Directory: {executionPlan.OutputDirectory}");
        }

        if (executionPlan.PlannedChecks.Count > 0)
        {
            Console.WriteLine($"Planned Checks: {string.Join(", ", executionPlan.PlannedChecks)}");
        }

        if (executionPlan.PlannedArtifacts.Count > 0)
        {
            Console.WriteLine($"Planned Artifacts: {string.Join(", ", executionPlan.PlannedArtifacts)}");
        }

        Console.WriteLine();
    }

    public static string BuildExecutionErrorMessage(IReadOnlyList<string> errors)
    {
        return "Execution plan rejected:" + Environment.NewLine + string.Join(Environment.NewLine, errors.Select(error => $"- {error}"));
    }

    private static void ApplyTransportOverride(McpServerConfig serverConfig, string? server)
    {
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

            return;
        }

        if (!string.IsNullOrWhiteSpace(server) &&
            (server.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
             server.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
        {
            serverConfig.Transport = "http";
        }
    }

    private static bool RequiresStrictAuthentication(McpServerProfile profile)
    {
        return profile == McpServerProfile.Authenticated || profile == McpServerProfile.Enterprise;
    }
}
