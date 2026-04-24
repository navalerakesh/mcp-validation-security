using System;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using Mcp.Benchmark.CLI.Abstractions;
using Mcp.Benchmark.ClientProfiles;
using Mcp.Benchmark.CLI.Exceptions;
using Mcp.Benchmark.CLI.Models;
using Mcp.Benchmark.CLI.Services;
using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Resources;
using Mcp.Benchmark.Core.Services;
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
    private readonly IClientProfileEvaluator _clientProfileEvaluator;
    private readonly IReportGenerator _reportGenerator;
    private readonly IValidationReportRenderer _reportRenderer;
    private readonly IGitHubActionsReporter _gitHubActionsReporter;
    private readonly ILogger<ValidateCommand> _logger;
    private readonly INextStepAdvisor _nextStepAdvisor;
    private readonly IExecutionGovernanceService _executionGovernanceService;
    private readonly IModelEvaluationExecutor _modelEvaluationExecutor;
    private readonly ISessionArtifactStore _artifactStore;
    private readonly IMcpHttpClient _httpClient;
    private readonly CliSessionContext _sessionContext;

    public ValidateCommand(
        IMcpValidatorService validatorService,
        IConsoleOutputService consoleOutput,
        IClientProfileEvaluator clientProfileEvaluator,
        IReportGenerator reportGenerator,
        IValidationReportRenderer reportRenderer,
        IGitHubActionsReporter gitHubActionsReporter,
        ILogger<ValidateCommand> logger,
        INextStepAdvisor nextStepAdvisor,
        IExecutionGovernanceService executionGovernanceService,
        IModelEvaluationExecutor modelEvaluationExecutor,
        ISessionArtifactStore artifactStore,
        IMcpHttpClient httpClient,
        CliSessionContext sessionContext)
    {
        _validatorService = validatorService ?? throw new ArgumentNullException(nameof(validatorService));
        _consoleOutput = consoleOutput ?? throw new ArgumentNullException(nameof(consoleOutput));
        _clientProfileEvaluator = clientProfileEvaluator ?? throw new ArgumentNullException(nameof(clientProfileEvaluator));
        _reportGenerator = reportGenerator ?? throw new ArgumentNullException(nameof(reportGenerator));
        _reportRenderer = reportRenderer ?? throw new ArgumentNullException(nameof(reportRenderer));
        _gitHubActionsReporter = gitHubActionsReporter ?? throw new ArgumentNullException(nameof(gitHubActionsReporter));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _nextStepAdvisor = nextStepAdvisor ?? throw new ArgumentNullException(nameof(nextStepAdvisor));
        _executionGovernanceService = executionGovernanceService ?? throw new ArgumentNullException(nameof(executionGovernanceService));
        _modelEvaluationExecutor = modelEvaluationExecutor ?? throw new ArgumentNullException(nameof(modelEvaluationExecutor));
        _artifactStore = artifactStore ?? throw new ArgumentNullException(nameof(artifactStore));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
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
        int? maxConcurrency = null,
        string? policyMode = null,
        string[]? clientProfiles = null,
        string? reportDetail = null,
        string? executionMode = null,
        bool? dryRun = null,
        string[]? allowedHosts = null,
        bool? allowPrivateAddresses = null,
        int? maxRequests = null,
        int? timeoutSeconds = null,
        string? persistenceMode = null,
        string? redactLevel = null,
        string? traceMode = null,
        bool? confirmElevatedRisk = null,
        bool? enableModelEval = null)
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
            ApplyPolicyOverride(configuration, policyMode);
            ApplyClientProfileOverride(configuration, clientProfiles);
            ApplyReportingOverrides(configuration, reportDetail);
            ExecutionPolicyOverrides.Apply(
                configuration,
                maxConcurrency,
                executionMode,
                dryRun,
                allowedHosts,
                allowPrivateAddresses,
                maxRequests,
                timeoutSeconds,
                persistenceMode,
                redactLevel,
                traceMode,
                confirmElevatedRisk,
                enableModelEval);
            configuration.Execution ??= new ExecutionPolicy();
            ApplyTestExecutionOverrides(configuration, configuration.Execution.MaxConcurrency);
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

            var explicitOutputDirectory = ResolveExplicitOutputDirectory(outputDirectory, configuration);

            _sessionContext.ApplyExecutionPolicy(configuration.Execution);
            var executionPlan = _executionGovernanceService.BuildValidationPlan(_sessionContext, configuration, explicitOutputDirectory);
            if (!executionPlan.IsValid)
            {
                throw new CliUsageException(BuildExecutionErrorMessage(executionPlan.ValidationErrors));
            }

            DisplayExecutionPlan(executionPlan);
            _consoleOutput.DisplayConfigurationStatus(configFile?.FullName, configFile?.Exists ?? false);

            if (executionPlan.DryRun)
            {
                var dryRunAuditManifest = _executionGovernanceService.BuildAuditManifest(
                    executionPlan,
                    result: null,
                    artifactPaths: Array.Empty<string>(),
                    modelEvaluationArtifact: null);
                var dryRunArtifactPaths = await PersistOperationalArtifactsAsync(explicitOutputDirectory, executionPlan, dryRunAuditManifest, modelEvaluationArtifact: null);

                if (dryRunArtifactPaths.Count > 0)
                {
                    _consoleOutput.WriteSuccess($"Operational artifacts generated: {string.Join(", ", dryRunArtifactPaths)}");
                }

                _consoleOutput.WriteSuccess("Dry run complete. No requests were sent.");
                Environment.ExitCode = 0;
                return;
            }

            var transportExecutionPolicy = configuration.Execution.Clone();
            transportExecutionPolicy.AllowedHosts = executionPlan.AllowedHosts.ToList();
            _httpClient.ConfigureExecutionPolicy(transportExecutionPolicy);

            _consoleOutput.ShowProgress(ValidationMessages.Progress.Initializing);

            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10));

            var result = await _validatorService.ValidateServerAsync(configuration, cts.Token);
            result.ValidationId = _sessionContext.SessionId;
            result.PolicyOutcome = ValidationPolicyEvaluator.Evaluate(result, configuration.Policy);
            try
            {
                result.ClientCompatibility = _clientProfileEvaluator.Evaluate(result, configuration.ClientProfiles);
            }
            catch (ArgumentException ex)
            {
                throw new CliUsageException(ex.Message);
            }

            result.ValidationConfig = configuration.CloneWithoutSecrets();

            var safeConfig = configuration.CloneWithoutSecrets();
            var safeResult = result.CloneWithoutSecrets();
            ModelEvaluationArtifact? modelEvaluationArtifact = null;
                var modelEvaluationPolicy = configuration.Evaluation?.ModelEvaluation;

                if (modelEvaluationPolicy?.Enabled == true)
            {
                modelEvaluationArtifact = await _modelEvaluationExecutor.ExecuteAsync(
                    safeResult,
                    executionPlan,
                    modelEvaluationPolicy,
                    cts.Token);
            }

            // Display main results; verbose turns on additional console output via ConsoleOutputService
            _consoleOutput.DisplayValidationResults(result, showDetails: verbose);
            DisplayPolicyOutcome(result.PolicyOutcome);
            DisplayClientCompatibility(result.ClientCompatibility);

            PersistSessionArtifact(safeConfig, safeResult, verbose);

            IReadOnlyList<string> artifactPaths = Array.Empty<string>();

            if (result.PolicyOutcome is { Passed: false } || result.OverallStatus != ValidationStatus.Passed)
            {
                _consoleOutput.WriteSessionLogHint("Validation log");
                _nextStepAdvisor.SuggestSessionLogReview("Validation log");
            }

            _nextStepAdvisor.SuggestSpecProfiles(
                configuration.Reporting.SpecProfile,
                result.ProtocolVersion ?? configuration.Server.ProtocolVersion);

            // Persist Markdown report if requested
            if (!string.IsNullOrWhiteSpace(explicitOutputDirectory))
            {
                artifactPaths = await SaveValidationResultsAsync(
                    result,
                    explicitOutputDirectory,
                    verbose,
                    executionPlan,
                    modelEvaluationArtifact);
            }
            else
            {
                var auditManifest = _executionGovernanceService.BuildAuditManifest(
                    executionPlan,
                    safeResult,
                    artifactPaths: Array.Empty<string>(),
                    modelEvaluationArtifact);
                artifactPaths = await PersistOperationalArtifactsAsync(
                    explicitOutputDirectory: null,
                    executionPlan,
                    auditManifest,
                    modelEvaluationArtifact);
            }

            _gitHubActionsReporter.PublishValidationResult(safeResult, artifactPaths);

            Environment.ExitCode = result.PolicyOutcome?.RecommendedExitCode ?? (result.OverallStatus == ValidationStatus.Passed ? 0 : 1);
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
            configuration.TestExecution.DefaultTestTimeoutMs = Math.Max(1000, configuration.Server.TimeoutMs);
            configuration.Validation.Categories.PerformanceTesting.MaxConcurrentConnections = normalized;
        }
    }

    private static void ApplyPolicyOverride(McpValidatorConfiguration configuration, string? policyMode)
    {
        configuration.Policy ??= new ValidationPolicyConfig();
        configuration.Policy.Mode = ValidationPolicyEvaluator.NormalizeMode(string.IsNullOrWhiteSpace(policyMode)
            ? configuration.Policy.Mode
            : policyMode);
    }

    private static void ApplyClientProfileOverride(McpValidatorConfiguration configuration, string[]? clientProfiles)
    {
        if (clientProfiles == null || clientProfiles.Length == 0)
        {
            return;
        }

        var normalizedProfiles = clientProfiles
            .SelectMany(value => value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (normalizedProfiles.Count == 0)
        {
            return;
        }

        configuration.ClientProfiles = new ClientProfileOptions
        {
            Profiles = normalizedProfiles
        };
    }

    private static void ApplyReportingOverrides(McpValidatorConfiguration configuration, string? reportDetail)
    {
        configuration.Reporting ??= new ReportingConfig();
        if (TryParseReportDetailLevel(reportDetail, out var detailLevel))
        {
            configuration.Reporting.ApplyDetailLevel(detailLevel);
            return;
        }

        configuration.Reporting.NormalizeDetailLevel();
    }

    private static bool TryParseReportDetailLevel(string? rawDetailLevel, out ReportDetailLevel detailLevel)
    {
        if (!string.IsNullOrWhiteSpace(rawDetailLevel) &&
            Enum.TryParse(rawDetailLevel, ignoreCase: true, out detailLevel))
        {
            return true;
        }

        detailLevel = default;
        return false;
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

    private void DisplayPolicyOutcome(ValidationPolicyOutcome? policyOutcome)
    {
        if (policyOutcome == null)
        {
            return;
        }

        var modeLabel = policyOutcome.Mode.ToUpperInvariant();
        if (policyOutcome.Passed)
        {
            _consoleOutput.WriteSuccess($"Policy {modeLabel}: {policyOutcome.Summary}");
        }
        else
        {
            _consoleOutput.WriteError($"Policy {modeLabel}: {policyOutcome.Summary}");
        }

        foreach (var reason in policyOutcome.Reasons.Take(3))
        {
            _consoleOutput.WriteWarning($"Policy reason: {reason}");
        }

        if (policyOutcome.AppliedSuppressions.Count > 0)
        {
            _consoleOutput.WriteSuccess($"Applied suppressions: {policyOutcome.AppliedSuppressions.Count} entry(ies), muting {policyOutcome.SuppressedSignalCount} signal(s).");
        }

        foreach (var suppression in policyOutcome.AppliedSuppressions.Take(3))
        {
            _consoleOutput.WriteWarning($"Suppressed by {suppression.Owner}: {suppression.Id} ({suppression.MatchedSignalCount} signal(s))");
        }

        foreach (var ignored in policyOutcome.IgnoredSuppressions.Take(3))
        {
            _consoleOutput.WriteWarning($"Ignored suppression {ignored.Id}: {ignored.Reason}");
        }
    }

    private void DisplayClientCompatibility(ClientCompatibilityReport? compatibilityReport)
    {
        if (compatibilityReport?.Assessments.Count > 0 != true)
        {
            return;
        }

        _consoleOutput.WriteInfo("Client profile compatibility:");
        foreach (var assessment in compatibilityReport.Assessments)
        {
            var message = $"{assessment.DisplayName} ({assessment.ProfileId}): {assessment.StatusLabel}. {assessment.Summary}";
            switch (assessment.Status)
            {
                case ClientProfileCompatibilityStatus.Compatible:
                    _consoleOutput.WriteSuccess(message);
                    break;
                case ClientProfileCompatibilityStatus.CompatibleWithWarnings:
                    _consoleOutput.WriteWarning(message);
                    break;
                default:
                    _consoleOutput.WriteError(message);
                    break;
            }

            foreach (var requirement in assessment.Requirements
                         .Where(item => item.Outcome is ClientProfileRequirementOutcome.Warning or ClientProfileRequirementOutcome.Failed)
                         .Take(2))
            {
                _consoleOutput.WriteWarning($"{assessment.ProfileId}/{requirement.RequirementId}: {requirement.Summary}");
            }
        }
    }

    private static bool IsAuthenticationFailure(Exception exception)
    {
        return ValidationReliability.IsAuthenticationFailure(exception);
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
    /// Saves validation results to disk: a primary Markdown report, a JSON
    /// snapshot that can be used later with the offline <c>report</c> command,
    /// and a SARIF artifact for CI/code scanning integrations.
    /// </summary>
    private async Task<IReadOnlyList<string>> SaveValidationResultsAsync(
        ValidationResult result,
        string outputDirectory,
        bool verbose,
        ExecutionPlan executionPlan,
        ModelEvaluationArtifact? modelEvaluationArtifact)
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

        var htmlPath = Path.Combine(outputDirectory, $"{baseName}-report.html");
        var includeDetailedSections = safeResult.ValidationConfig.Reporting.IncludesDetailedSections();
        var html = _reportRenderer.GenerateHtmlReport(safeResult, safeResult.ValidationConfig.Reporting, includeDetailedSections);
        await File.WriteAllTextAsync(htmlPath, html);

        // Machine‑readable JSON snapshot for offline reporting (mcpval report).
        var jsonPath = Path.Combine(outputDirectory, $"{baseName}-result.json");
        var jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        var jsonRoot = JsonSerializer.SerializeToNode(safeResult, jsonOptions) as JsonObject
            ?? throw new InvalidOperationException("Failed to serialize validation result to JSON.");
        AnnotatePerformanceMeasurementState(jsonRoot, safeResult.PerformanceTesting);
        var json = jsonRoot.ToJsonString(jsonOptions);
        await File.WriteAllTextAsync(jsonPath, json);

        var sarifPath = Path.Combine(outputDirectory, $"{baseName}-results.sarif.json");
        var sarif = _reportRenderer.GenerateSarifReport(safeResult);
        await File.WriteAllTextAsync(sarifPath, sarif);

        _logger.LogInformation("Markdown report saved: {MarkdownPath}", markdownPath);
        _logger.LogInformation("HTML report saved: {HtmlPath}", htmlPath);
        _logger.LogInformation("Validation result JSON saved: {JsonPath}", jsonPath);
        _logger.LogInformation("SARIF report saved: {SarifPath}", sarifPath);
        _consoleOutput.WriteSuccess($"Reports generated: {markdownPath} and {htmlPath}");

        var savedPaths = new List<string> { markdownPath, htmlPath, jsonPath, sarifPath };

        // Emit an aggregate profile summary artifact when client profiles are evaluated.
        if (safeResult.ClientCompatibility?.Assessments.Count > 0)
        {
            var profileSummaryPath = Path.Combine(outputDirectory, $"{baseName}-profile-summary.json");
            var profileSummary = BuildClientProfileSummary(safeResult);
            var profileSummaryJson = JsonSerializer.Serialize(profileSummary, jsonOptions);
            await File.WriteAllTextAsync(profileSummaryPath, profileSummaryJson);
            _logger.LogInformation("Client profile summary saved: {Path}", profileSummaryPath);
            savedPaths.Add(profileSummaryPath);
        }

        if (modelEvaluationArtifact != null)
        {
            var modelEvaluationPath = Path.Combine(outputDirectory, $"{baseName}-model-evaluation.json");
            var modelEvaluationJson = JsonSerializer.Serialize(modelEvaluationArtifact, jsonOptions);
            await File.WriteAllTextAsync(modelEvaluationPath, modelEvaluationJson);
            _logger.LogInformation("Model evaluation artifact saved: {Path}", modelEvaluationPath);
            savedPaths.Add(modelEvaluationPath);
        }

        var auditManifest = _executionGovernanceService.BuildAuditManifest(
            executionPlan,
            safeResult,
            savedPaths,
            modelEvaluationArtifact);
        var auditManifestPath = Path.Combine(outputDirectory, $"{baseName}-audit.json");
        var auditManifestJson = JsonSerializer.Serialize(auditManifest, jsonOptions);
        await File.WriteAllTextAsync(auditManifestPath, auditManifestJson);
        _logger.LogInformation("Audit manifest saved: {Path}", auditManifestPath);
        savedPaths.Add(auditManifestPath);

        return savedPaths;
    }

    private async Task<IReadOnlyList<string>> PersistOperationalArtifactsAsync(
        string? explicitOutputDirectory,
        ExecutionPlan executionPlan,
        AuditManifest auditManifest,
        ModelEvaluationArtifact? modelEvaluationArtifact)
    {
        var artifactPaths = new List<string>();

        if (!string.IsNullOrWhiteSpace(explicitOutputDirectory))
        {
            if (!Directory.Exists(explicitOutputDirectory))
            {
                Directory.CreateDirectory(explicitOutputDirectory);
            }

            var jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            var baseName = $"mcp-validation-{timestamp}";

            if (modelEvaluationArtifact != null)
            {
                var modelEvaluationPath = Path.Combine(explicitOutputDirectory, $"{baseName}-model-evaluation.json");
                var modelEvaluationJson = JsonSerializer.Serialize(modelEvaluationArtifact, jsonOptions);
                await File.WriteAllTextAsync(modelEvaluationPath, modelEvaluationJson);
                artifactPaths.Add(modelEvaluationPath);
            }

            var updatedAuditManifest = _executionGovernanceService.BuildAuditManifest(
                executionPlan,
                result: null,
                artifactPaths,
                modelEvaluationArtifact);
            var auditManifestPath = Path.Combine(explicitOutputDirectory, $"{baseName}-audit.json");
            var auditManifestJson = JsonSerializer.Serialize(updatedAuditManifest, jsonOptions);
            await File.WriteAllTextAsync(auditManifestPath, auditManifestJson);
            artifactPaths.Add(auditManifestPath);
            return artifactPaths;
        }

        var persistedAuditPath = _artifactStore.TrySaveJson("audit-manifest", auditManifest);
        if (!string.IsNullOrWhiteSpace(persistedAuditPath))
        {
            artifactPaths.Add(persistedAuditPath);
        }

        if (modelEvaluationArtifact != null)
        {
            var persistedModelEvaluationPath = _artifactStore.TrySaveJson("model-evaluation", modelEvaluationArtifact);
            if (!string.IsNullOrWhiteSpace(persistedModelEvaluationPath))
            {
                artifactPaths.Add(persistedModelEvaluationPath);
            }
        }

        return artifactPaths;
    }

    private void DisplayExecutionPlan(ExecutionPlan executionPlan)
    {
        _consoleOutput.DisplaySessionBanner();
        Console.WriteLine();
        _consoleOutput.WriteHeader("EXECUTION PLAN");
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
        Console.WriteLine($"Model Evaluation: {(executionPlan.ModelEvaluationEnabled ? "enabled" : "disabled")}");

        if (executionPlan.SelectedClientProfiles.Count > 0)
        {
            Console.WriteLine($"Client Profiles: {string.Join(", ", executionPlan.SelectedClientProfiles)}");
        }

        if (executionPlan.OutputDirectory != null)
        {
            Console.WriteLine($"Output Directory: {executionPlan.OutputDirectory}");
        }

        Console.WriteLine($"Planned Checks: {string.Join(", ", executionPlan.PlannedChecks)}");
        Console.WriteLine($"Planned Artifacts: {string.Join(", ", executionPlan.PlannedArtifacts)}");
        Console.WriteLine();
    }

    private static string BuildExecutionErrorMessage(IReadOnlyList<string> errors)
    {
        return "Execution plan rejected:" + Environment.NewLine + string.Join(Environment.NewLine, errors.Select(error => $"- {error}"));
    }

    private static string? ResolveExplicitOutputDirectory(DirectoryInfo? outputDirectory, McpValidatorConfiguration configuration)
    {
        if (outputDirectory != null)
        {
            return outputDirectory.FullName;
        }

        return configuration.Execution?.PersistenceMode == PersistenceMode.ExplicitOutput
            ? configuration.Reporting.OutputDirectory
            : null;
    }

    private static object BuildClientProfileSummary(ValidationResult result)
    {
        var assessments = result.ClientCompatibility!.Assessments;

        return new
        {
            generatedUtc = DateTime.UtcNow.ToString("o"),
            validationId = result.ValidationId,
            serverEndpoint = result.ServerConfig.Endpoint,
            complianceScore = result.ComplianceScore,
            trustLevel = result.TrustAssessment?.TrustLabel,
            profileCount = assessments.Count,
            compatibleCount = assessments.Count(a => a.Status == ClientProfileCompatibilityStatus.Compatible),
            warningCount = assessments.Count(a => a.Status == ClientProfileCompatibilityStatus.CompatibleWithWarnings),
            incompatibleCount = assessments.Count(a => a.Status == ClientProfileCompatibilityStatus.Incompatible),
            profiles = assessments.Select(a => new
            {
                profileId = a.ProfileId,
                displayName = a.DisplayName,
                status = a.StatusLabel,
                passedRequirements = a.PassedRequirements,
                warningRequirements = a.WarningRequirements,
                failedRequirements = a.FailedRequirements,
                topBlockers = a.Requirements
                    .Where(r => r.Outcome == ClientProfileRequirementOutcome.Failed)
                    .Select(r => new
                    {
                        requirementId = r.RequirementId,
                        title = r.Title,
                        ruleIds = r.RuleIds,
                        recommendation = r.Recommendation
                    })
                    .ToList()
            }).ToList()
        };
    }

    private static void AnnotatePerformanceMeasurementState(JsonObject root, PerformanceTestResult? performance)
    {
        if (performance == null ||
            root["assessments"] is not JsonObject assessmentsObject ||
            assessmentsObject["performanceTesting"] is not JsonObject performanceObject)
        {
            return;
        }

        var measurementsCaptured = PerformanceMeasurementEvaluator.HasObservedMetrics(performance);
        performanceObject["measurementsCaptured"] = measurementsCaptured;
        performanceObject["measurementStatus"] = measurementsCaptured ? "Captured" : "Unavailable";

        if (!measurementsCaptured)
        {
            performanceObject["measurementReason"] = PerformanceMeasurementEvaluator.GetUnavailableReason(
                performance,
                "Performance measurements were not captured before the run ended.");
        }
    }
}
