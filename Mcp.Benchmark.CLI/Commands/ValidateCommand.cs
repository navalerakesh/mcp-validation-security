using System;
using System.Net.Http;
using System.Text.Json;
using Mcp.Benchmark.CLI.Abstractions;
using Mcp.Benchmark.CLI.Exceptions;
using Mcp.Benchmark.CLI.Services;
using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Resources;
using Microsoft.Extensions.Logging;
using Mcp.Benchmark.CLI.Utilities;

namespace Mcp.Benchmark.CLI;

/// <summary>
/// Command handler for comprehensive MCP server validation operations.
/// Orchestrates execution of protocol, tools, security, performance and other tests
/// and produces human-friendly console output plus optional Markdown reports.
/// </summary>
public class ValidateCommand
{
    private readonly IMcpValidatorService _validatorService;
    private readonly IConsoleOutputService _consoleOutput;
    private readonly IReportGenerator _reportGenerator;
    private readonly ILogger<ValidateCommand> _logger;
    private readonly INextStepAdvisor _nextStepAdvisor;
        private readonly ISessionArtifactStore _artifactStore;
    private readonly CliSessionContext _sessionContext;

    public ValidateCommand(
        IMcpValidatorService validatorService,
        IConsoleOutputService consoleOutput,
        IReportGenerator reportGenerator,
        ILogger<ValidateCommand> logger,
            INextStepAdvisor nextStepAdvisor,
            ISessionArtifactStore artifactStore,
            CliSessionContext sessionContext)
    {
        _validatorService = validatorService ?? throw new ArgumentNullException(nameof(validatorService));
        _consoleOutput = consoleOutput ?? throw new ArgumentNullException(nameof(consoleOutput));
        _reportGenerator = reportGenerator ?? throw new ArgumentNullException(nameof(reportGenerator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _nextStepAdvisor = nextStepAdvisor ?? throw new ArgumentNullException(nameof(nextStepAdvisor));
            _artifactStore = artifactStore ?? throw new ArgumentNullException(nameof(artifactStore));
        _sessionContext = sessionContext ?? throw new ArgumentNullException(nameof(sessionContext));
    }

    /// <summary>
    /// Executes the validation command with the specified parameters.
    /// This is invoked by Program.cs when the `validate` subcommand is used.
    /// </summary>
    public async Task ExecuteAsync(
        string server,
        DirectoryInfo? outputDirectory,
        string? specProfile,
        FileInfo? configFile,
        bool verbose,
        string? token = null,
        bool interactive = false,
        string? serverProfile = null,
        int? maxConcurrency = null)
    {
        _consoleOutput.SetVerbose(verbose);
        _nextStepAdvisor.Reset();
        McpValidatorConfiguration? configuration = null;
        var targetEndpoint = server;

        try
        {
            if (string.IsNullOrWhiteSpace(server) && (configFile == null || !configFile.Exists))
            {
                throw new CliUsageException("Server endpoint or configuration file must be provided.");
            }

            if (verbose)
            {
                _consoleOutput.WriteInfo($"Starting MCP server validation for: {server}");
            }

            // Load and normalize configuration
            var profileOverride = ParseServerProfile(serverProfile);
            configuration = await LoadConfigurationAsync(server, specProfile, configFile, profileOverride);
            ApplyTestExecutionOverrides(configuration, maxConcurrency);
            targetEndpoint = configuration.Server.Endpoint ?? targetEndpoint;
            var effectiveProfile = configuration.Server.Profile;

            if (profileOverride.HasValue)
            {
                effectiveProfile = profileOverride.Value;
            }

            // Surface effective access profile and spec profile to the user
            var activeSpecProfile = string.IsNullOrWhiteSpace(configuration.Reporting.SpecProfile)
                ? "default (latest)"
                : configuration.Reporting.SpecProfile;
            _consoleOutput.WriteInfo($"Access profile: {effectiveProfile}; MCP spec profile: {activeSpecProfile}");

            if (!EnsureAuthenticationPrerequisites(effectiveProfile, configuration, token, interactive, targetEndpoint))
            {
                Environment.ExitCode = 2;
                return;
            }

            // Apply authentication overrides from CLI if provided
            ApplyAuthenticationOverrides(configuration, token, interactive);

            // Apply output directory override if specified
            if (outputDirectory != null)
            {
                configuration.Reporting.OutputDirectory = outputDirectory.FullName;
            }

            // Show high level plan
            _consoleOutput.DisplayValidationPlan(ValidationMessages.Titles.SecurityValidation, configuration.Server);
            _consoleOutput.DisplayConfigurationStatus(configFile?.FullName, configFile?.Exists ?? false);

            _consoleOutput.ShowProgress(ValidationMessages.Progress.Initializing);

            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10));

            var result = await _validatorService.ValidateServerAsync(configuration, cts.Token);
            result.ValidationId = _sessionContext.SessionId;
            var safeConfig = configuration.CloneWithoutSecrets();
            var safeResult = result.CloneWithoutSecrets();

            // Display main results; verbose turns on additional console output via ConsoleOutputService
            _consoleOutput.DisplayValidationResults(result, showDetails: verbose);

            PersistSessionArtifact(safeConfig, safeResult, verbose);

            if (result.OverallStatus != ValidationStatus.Passed)
            {
                _consoleOutput.WriteSessionLogHint("Validation log");
                _nextStepAdvisor.SuggestSessionLogReview("Validation log");
            }

            _nextStepAdvisor.SuggestSpecProfiles(
                configuration.Reporting.SpecProfile,
                result.ProtocolVersion ?? configuration.Server.ProtocolVersion);

            // Persist Markdown report if requested
            if (outputDirectory != null)
            {
                await SaveValidationResultsAsync(result, configuration.Reporting.OutputDirectory);
            }

            Environment.ExitCode = result.OverallStatus == ValidationStatus.Passed ? 0 : 1;
        }
        catch (CliExceptionBase cliEx)
        {
            _consoleOutput.WriteError(cliEx.Message);
            _consoleOutput.WriteSessionLogHint("Validation log");
            _nextStepAdvisor.SuggestSessionLogReview("Validation log");
            _logger.LogWarning(cliEx, "Validation aborted for server {Server}: {Message}", server, cliEx.Message);
            Environment.ExitCode = cliEx.ExitCode;
        }
        catch (OperationCanceledException)
        {
            _consoleOutput.WriteError(ValidationMessages.Errors.TimeoutOccurred);
            _consoleOutput.WriteSessionLogHint("Validation log");
            _nextStepAdvisor.SuggestSessionLogReview("Validation log");
            _logger.LogWarning("Validation timed out for server {Server}", server);
            Environment.ExitCode = 124;
        }
        catch (Exception ex)
        {
            _consoleOutput.WriteError($"{ValidationMessages.Errors.UnexpectedError}: {ex.Message}");
            _consoleOutput.WriteSessionLogHint("Validation log");
            _nextStepAdvisor.SuggestSessionLogReview("Validation log");
            _logger.LogError(ex, "Validation failed for server {Server}", server);
            if (IsAuthenticationFailure(ex))
            {
                _nextStepAdvisor.SuggestAuthenticationFlow("validate", targetEndpoint);
            }
            Environment.ExitCode = 1;
        }
        finally
        {
            _nextStepAdvisor.Render();
        }
    }

    private static void ApplyTestExecutionOverrides(McpValidatorConfiguration configuration, int? maxConcurrency)
    {
        if (configuration.TestExecution == null)
        {
            configuration.TestExecution = new TestExecutionConfig();
        }

        configuration.Validation ??= new ValidationConfig();
        configuration.Validation.Categories ??= new ValidationScenarios();

        if (maxConcurrency.HasValue)
        {
            var normalized = Math.Clamp(maxConcurrency.Value, 1, 128);
            configuration.TestExecution.MaxParallelThreads = normalized;
            configuration.Validation.Categories.PerformanceTesting.MaxConcurrentConnections = normalized;
        }
    }

    private static void ApplyAuthenticationOverrides(
        McpValidatorConfiguration configuration,
        string? token,
        bool interactive)
    {
        if (string.IsNullOrEmpty(token) &&
            !interactive)
        {
            return;
        }

        var auth = configuration.Server.Authentication ??= new AuthenticationConfig();

        if (!string.IsNullOrEmpty(token))
        {
            auth.Token = token;
            auth.Type = string.IsNullOrEmpty(auth.Type) || auth.Type == "none"
                ? "bearer"
                : auth.Type;
            auth.Required = true;
        }

        auth.AllowInteractive = interactive;
    }

    private static void NormalizeServerConfig(McpServerConfig serverConfig, string server)
    {
        if (!string.IsNullOrWhiteSpace(server))
        {
            serverConfig.Endpoint = server;

            if (Uri.TryCreate(server, UriKind.Absolute, out var uri))
            {
                if (uri.Scheme is "http" or "https")
                {
                    serverConfig.Transport = "http";
                }
                else if (uri.Scheme is "ws" or "wss")
                {
                    serverConfig.Transport = "websocket";
                }
            }
            else if (server.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                     server.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                serverConfig.Transport = "http";
            }
        }
    }

    private static void ApplySpecProfile(McpValidatorConfiguration configuration, string? specProfile)
    {
        if (string.IsNullOrWhiteSpace(specProfile))
        {
            return;
        }

        configuration.Validation.Categories.ProtocolCompliance.ProtocolVersion = specProfile;
        configuration.Reporting.SpecProfile = specProfile;
    }

    private static async Task<McpValidatorConfiguration> LoadConfigurationAsync(
        string server,
        string? specProfile,
        FileInfo? configFile,
        McpServerProfile? serverProfileOverride)
    {
        McpValidatorConfiguration configuration;

        if (configFile?.Exists == true)
        {
            var json = await File.ReadAllTextAsync(configFile.FullName);
            configuration = JsonSerializer.Deserialize<McpValidatorConfiguration>(json) ?? new McpValidatorConfiguration();
        }
        else
        {
            configuration = new McpValidatorConfiguration();
        }

        NormalizeServerConfig(configuration.Server, server);
        ApplySpecProfile(configuration, specProfile);
        ApplyServerProfileOverride(configuration, serverProfileOverride);

        return configuration;
    }

    private bool EnsureAuthenticationPrerequisites(
        McpServerProfile profile,
        McpValidatorConfiguration configuration,
        string? token,
        bool interactive,
        string? endpointHint)
    {
        if (!RequiresStrictAuthentication(profile))
        {
            return true;
        }

        var hasToken = !string.IsNullOrWhiteSpace(token) ||
                       !string.IsNullOrWhiteSpace(configuration.Server.Authentication?.Token);

        if (hasToken || interactive)
        {
            return true;
        }

        _consoleOutput.WriteError(
            "Authenticated or enterprise servers require credentials. Provide -t/--token or enable -i/--interactive.");
        _nextStepAdvisor.SuggestAuthenticationFlow("validate", endpointHint);

        return false;
    }

    private static void ApplyServerProfileOverride(
        McpValidatorConfiguration configuration,
        McpServerProfile? serverProfileOverride)
    {
        if (serverProfileOverride.HasValue)
        {
            configuration.Server.Profile = serverProfileOverride.Value;
        }
    }

    private static McpServerProfile? ParseServerProfile(string? rawProfile)
    {
        if (string.IsNullOrWhiteSpace(rawProfile))
        {
            return null;
        }

        return Enum.TryParse<McpServerProfile>(rawProfile, ignoreCase: true, out var parsed)
            ? parsed
            : null;
    }

    private static bool IsAuthenticationFailure(Exception exception)
    {
        var current = exception;
        while (current != null)
        {
            if (current is HttpRequestException httpEx &&
                (httpEx.Message.Contains("401", StringComparison.OrdinalIgnoreCase) ||
                 httpEx.Message.Contains("403", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            current = current.InnerException;
        }

        return false;
    }

    private static bool RequiresStrictAuthentication(McpServerProfile profile)
    {
        return profile == McpServerProfile.Authenticated || profile == McpServerProfile.Enterprise;
    }

    private void PersistSessionArtifact(McpValidatorConfiguration configuration, ValidationResult safeResult, bool verbose)
    {
        _artifactStore.TrySaveJson(
            "validate-results",
            new
            {
                timestamp = DateTimeOffset.UtcNow,
                configuration,
                result = safeResult,
                verbose
            });
    }

    /// <summary>
    /// Saves validation results to disk: a primary Markdown report plus a JSON
    /// snapshot that can be used later with the offline <c>report</c> command.
    /// </summary>
    private async Task SaveValidationResultsAsync(ValidationResult result, string outputDirectory)
    {
        if (!Directory.Exists(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        var baseName = $"mcp-validation-{timestamp}";

        // Always work from a redacted clone when persisting artifacts so that
        // no tokens, passwords or sensitive headers are written to disk.
        var safeResult = result.CloneWithoutSecrets();

        // Human‑readable Markdown report used by most users.
        var markdownPath = Path.Combine(outputDirectory, $"{baseName}-report.md");
        var reportContent = _reportGenerator.GenerateReport(safeResult);
        await File.WriteAllTextAsync(markdownPath, reportContent);

        // Machine‑readable JSON snapshot for offline reporting (mcpval report).
        var jsonPath = Path.Combine(outputDirectory, $"{baseName}-result.json");
        var jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        var json = JsonSerializer.Serialize(safeResult, jsonOptions);
        await File.WriteAllTextAsync(jsonPath, json);

        _logger.LogInformation("Markdown report saved: {MarkdownPath}", markdownPath);
        _logger.LogInformation("Validation result JSON saved: {JsonPath}", jsonPath);
        _consoleOutput.WriteSuccess($"Report generated: {markdownPath}");
    }
}
