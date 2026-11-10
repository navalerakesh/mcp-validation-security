using System;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Mcp.Benchmark.CLI.Abstractions;
using Mcp.Benchmark.ClientProfiles;
using Mcp.Benchmark.CLI.Exceptions;
using Mcp.Benchmark.CLI.Models;
using Mcp.Benchmark.CLI.Services;
using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Constants;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Resources;
using Mcp.Benchmark.Core.Services;
using Mcp.Benchmark.Core.Utilities;
using Microsoft.Extensions.Logging;
using Mcp.Benchmark.CLI.Utilities;
using Mcp.Benchmark.CLI.Utilities.Logging;

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
    private readonly IOfficialConformanceEvidenceImporter _officialConformanceEvidenceImporter;

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
        CliSessionContext sessionContext,
        IOfficialConformanceEvidenceImporter? officialConformanceEvidenceImporter = null)
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
        _officialConformanceEvidenceImporter = officialConformanceEvidenceImporter ?? UnavailableOfficialConformanceEvidenceImporter.Instance;
    }

    private sealed class UnavailableOfficialConformanceEvidenceImporter : IOfficialConformanceEvidenceImporter
    {
        public static UnavailableOfficialConformanceEvidenceImporter Instance { get; } = new();

        public OfficialConformanceImportResult Import(string json, OfficialConformanceImportContext? context = null) =>
            throw new InvalidOperationException("Official conformance evidence importer is not configured for this host.");
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
        string? assuranceProfile = null,
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
        bool? enableModelEval = null,
        string? contentSafetyContext = null,
        string[]? allowedOrigins = null,
        string? protocolEra = null,
        FileInfo? baselineFile = null,
        bool? regressionOnly = null,
        string? outputFormat = null,
        bool? signAttestation = null,
        FileInfo? conformanceResultsFile = null,
        string[]? extensionIds = null,
        FileInfo? providerHarnessFile = null,
        FileInfo? providerAuthorizationKeyFile = null,
        CancellationToken cancellationToken = default)
    {
        var structuredOutput = string.Equals(outputFormat, "json", StringComparison.OrdinalIgnoreCase);
        _consoleOutput.SetVerbose(verbose);
        _consoleOutput.SetStructuredOutput(structuredOutput);
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
            var contentSafetyContextOverride = ParseContentSafetyContextProfile(contentSafetyContext);
            configuration = await LoadConfigurationAsync(server, specProfile, configFile, profileOverride, contentSafetyContextOverride);
            configuration.Server.ProtocolEra = CommandConnectionHelper.ParseProtocolEra(protocolEra) ?? configuration.Server.ProtocolEra;
            ApplyPolicyOverride(configuration, policyMode);
            ApplyAssuranceOverride(configuration, assuranceProfile);
            ApplyClientProfileOverride(configuration, clientProfiles);
            ApplyExtensionOverride(configuration, extensionIds);
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
                enableModelEval,
                allowedOrigins);
            configuration.Execution ??= new ExecutionPolicy();
            ApplyTestExecutionOverrides(configuration, configuration.Execution.MaxConcurrency);
            targetEndpoint = configuration.Server.Endpoint ?? targetEndpoint;
            var effectiveProfile = configuration.Server.Profile;

            if (profileOverride.HasValue)
            {
                effectiveProfile = profileOverride.Value;
            }

            var effectiveContentSafetyContext = ContentSafetyAnalysisContext.FromServerConfig(configuration.Server).Profile;

            // Surface effective access profile and spec profile to the user
            var activeSpecProfile = string.IsNullOrWhiteSpace(configuration.Reporting.SpecProfile)
                ? "default (latest)"
                : configuration.Reporting.SpecProfile;
            _consoleOutput.WriteInfo($"Access profile: {effectiveProfile}; risk context: {effectiveContentSafetyContext}; MCP spec profile: {activeSpecProfile}");

            EnsureAuthenticationPrerequisites(effectiveProfile, configuration, token, interactive, targetEndpoint);

            // Apply authentication overrides from CLI if provided
            ApplyAuthenticationOverrides(configuration, token, interactive);

            // Apply output directory override if specified
            if (outputDirectory != null)
            {
                configuration.Reporting.OutputDirectory = outputDirectory.FullName;
            }
            if (signAttestation.HasValue)
            {
                configuration.Reporting.SignAttestation = signAttestation.Value;
            }
            if (regressionOnly.HasValue)
            {
                configuration.Policy.RegressionOnly = regressionOnly.Value;
            }

            var explicitOutputDirectory = ResolveExplicitOutputDirectory(outputDirectory, configuration);
            var providerAssuranceRequested = string.Equals(
                configuration.Assurance.Profile,
                AssuranceProfileCatalog.Provider.ProfileId,
                StringComparison.Ordinal);
            var providerActiveExecution = providerAssuranceRequested && configuration.Execution.DryRun != true;
            var attestationReady = false;
            if (configuration.Reporting.SignAttestation)
            {
                if (string.IsNullOrWhiteSpace(explicitOutputDirectory))
                {
                    throw new CliUsageException("Signed attestations require an explicit output directory.");
                }
                var privateKeyPem = Environment.GetEnvironmentVariable("MCPVAL_ATTESTATION_PRIVATE_KEY_PEM");
                if (string.IsNullOrWhiteSpace(privateKeyPem))
                {
                    throw new CliUsageException("Signed attestations require MCPVAL_ATTESTATION_PRIVATE_KEY_PEM.");
                }
                try
                {
                    ValidationAttestationSigner.ValidatePrivateKey(privateKeyPem);
                    attestationReady = true;
                }
                catch (Exception exception) when (exception is ArgumentException or CryptographicException)
                {
                    throw new CliUsageException($"Attestation private key is invalid: {exception.Message}");
                }
            }

            if (providerActiveExecution && !configuration.Reporting.SignAttestation)
                throw new CliUsageException("Provider assurance requires --sign-attestation and an explicit output directory.");
            if (providerActiveExecution && (providerHarnessFile == null || providerAuthorizationKeyFile == null))
                throw new CliUsageException("Provider assurance requires --provider-harness and --provider-authorization-key.");
            if (!providerAssuranceRequested && (providerHarnessFile != null || providerAuthorizationKeyFile != null))
                throw new CliUsageException("Provider harness inputs are accepted only with --assurance provider.");
            if (providerAssuranceRequested && (providerHarnessFile == null) != (providerAuthorizationKeyFile == null))
                throw new CliUsageException("Provider harness and authorization key must be supplied together.");

            _sessionContext.ApplyExecutionPolicy(configuration.Execution);
            var executionPlan = _executionGovernanceService.BuildValidationPlan(_sessionContext, configuration, explicitOutputDirectory);
            if (!executionPlan.IsValid)
            {
                throw new CliUsageException(BuildExecutionErrorMessage(executionPlan.ValidationErrors));
            }
            executionPlan.ProviderExecutionPlanSha256 = ComputeProviderExecutionPlanDigest(executionPlan);

            var conformanceEvidenceJson = await LoadOfficialConformanceEvidenceAsync(
                conformanceResultsFile,
                cancellationToken).ConfigureAwait(false);
            ProviderHarnessManifest? providerHarnessManifest = null;
            ProviderAuthorizationVerification? providerAuthorizationVerification = null;
            ProviderHarnessAssessment? providerHarnessAssessment = null;
            var providerExecutionPlanDigest = executionPlan.ProviderExecutionPlanSha256;
            if (providerHarnessFile != null && providerAuthorizationKeyFile != null)
            {
                providerHarnessManifest = await BoundedArtifactReader.ReadProviderHarnessAsync(
                    providerHarnessFile!,
                    cancellationToken).ConfigureAwait(false);
                var trustedPublicKey = await BoundedArtifactReader.ReadTextAsync(
                    providerAuthorizationKeyFile!,
                    BoundedArtifactReader.MaximumPublicKeyBytes,
                    cancellationToken).ConfigureAwait(false);
                providerAuthorizationVerification = ProviderAuthorizationVerifier.Verify(
                    providerHarnessManifest,
                    trustedPublicKey);
                providerHarnessAssessment = ProviderHarnessEvaluator.Evaluate(
                    providerHarnessManifest,
                    providerAuthorizationVerification,
                    executionPlan.TargetDigest,
                    executionPlan.ResolvedSchemaVersion,
                    providerExecutionPlanDigest,
                    configuration.Execution.Mode,
                    DateTimeOffset.UtcNow,
                    executionPlan.PlannedChecks);
                if (providerHarnessAssessment.Status == ProviderHarnessStatus.Invalid)
                {
                    throw new CliUsageException(
                        "Provider harness verification failed: " + string.Join(" ", providerHarnessAssessment.Reasons));
                }
            }

            if (!structuredOutput)
            {
                DisplayExecutionPlan(executionPlan);
            }
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
                if (structuredOutput)
                {
                    Console.Out.WriteLine(JsonSerializer.Serialize(
                        new CliResultEnvelope { CommandName = "validate", ExitCode = 0, Result = executionPlan },
                        ArtifactJsonOptions.Create(writeIndented: false)));
                }
                return;
            }

            var targetResolutionErrors = await _executionGovernanceService.ValidateTargetResolutionAsync(executionPlan);
            if (targetResolutionErrors.Count > 0)
            {
                throw new CliUsageException(BuildExecutionErrorMessage(targetResolutionErrors));
            }

            var transportExecutionPolicy = configuration.Execution.Clone();
            transportExecutionPolicy.AllowedHosts = executionPlan.AllowedHosts.ToList();
            transportExecutionPolicy.AllowedOrigins = executionPlan.AllowedOrigins.ToList();
            _httpClient.ConfigureExecutionPolicy(transportExecutionPolicy);
            configuration.Server.ProtocolVersion = executionPlan.ResolvedSchemaVersion;

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromMinutes(10));
            await _httpClient.StartSessionAsync(configuration.Server.Endpoint!, configuration.Server.Environment, cts.Token);

            _consoleOutput.ShowProgress(ValidationMessages.Progress.Initializing);

            var result = _validatorService is ICorrelatedMcpValidatorService correlatedValidatorService
                ? await correlatedValidatorService.ValidateServerAsync(
                    ValidationRunRequest.Capture(configuration, _sessionContext.SessionId),
                    cts.Token)
                : await _validatorService.ValidateServerAsync(configuration, cts.Token);
            if (result.ValidationId != _sessionContext.SessionId)
            {
                result.ValidationId = _sessionContext.SessionId;
                if (result.Run.OperationalMetrics != null)
                {
                    result.Run.OperationalMetrics.RunCorrelationId = result.ValidationId;
                }
            }
            try
            {
                result.ClientCompatibility = _clientProfileEvaluator.Evaluate(result, configuration.ClientProfiles);
            }
            catch (ArgumentException ex)
            {
                throw new CliUsageException(ex.Message);
            }
            if (conformanceEvidenceJson != null)
            {
                AttachOfficialConformanceEvidence(
                    result,
                    conformanceEvidenceJson,
                    executionPlan.TargetDigest,
                    result.ProtocolVersion ?? executionPlan.ResolvedSchemaVersion);
            }

            var assuranceProfileManifest = AssuranceProfileCatalog.Resolve(configuration.Assurance.Profile);
            var certificationTime = DateTimeOffset.UtcNow;
            var suppressionAssessment = ValidationPolicyEvaluator.AssessSuppressions(
                configuration.Policy,
                certificationTime);
            var preliminaryContext = new AssuranceEvaluationContext
            {
                ExecutionMode = configuration.Execution.Mode,
                HasSignedAttestation = attestationReady,
                RequiredExtensionIds = configuration.Validation.Extensions,
                ActiveWaiverCount = suppressionAssessment.ActiveCount,
                InvalidWaiverCount = suppressionAssessment.InvalidSuppressions.Count
            };
            result.Assurance = AssuranceAssessmentEngine.Evaluate(
                result,
                assuranceProfileManifest,
                preliminaryContext);

            if (providerHarnessManifest != null && providerAuthorizationVerification != null)
            {
                providerHarnessAssessment = ProviderHarnessEvaluator.Evaluate(
                    providerHarnessManifest,
                    providerAuthorizationVerification,
                    executionPlan.TargetDigest,
                    executionPlan.ResolvedSchemaVersion,
                    providerExecutionPlanDigest,
                    configuration.Execution.Mode,
                    certificationTime,
                    executionPlan.PlannedChecks,
                    BuildCurrentProviderRunReceipt(result, providerExecutionPlanDigest, certificationTime));
            }

            result.Assurance = AssuranceAssessmentEngine.Evaluate(
                result,
                assuranceProfileManifest,
                new AssuranceEvaluationContext
                {
                    ExecutionMode = configuration.Execution.Mode,
                    HasSignedAttestation = attestationReady,
                    ProviderHarness = providerHarnessAssessment,
                    RequiredExtensionIds = configuration.Validation.Extensions,
                    ActiveWaiverCount = suppressionAssessment.ActiveCount,
                    InvalidWaiverCount = suppressionAssessment.InvalidSuppressions.Count
                });

            if (baselineFile != null)
            {
                result.BaselineComparison = ValidationBaselineEvaluator.Compare(
                    await LoadBaselineAsync(baselineFile, cancellationToken),
                    result);
            }
            result.PolicyOutcome = ValidationPolicyEvaluator.Evaluate(result, configuration.Policy);

            result.Run.TargetIdentitySha256 = executionPlan.TargetDigest;
            result.Run.ExecutionPlanSha256 = providerExecutionPlanDigest;
            result.Run.ExecutionMode = configuration.Execution.Mode;
            result.Run.PlannedChecks = executionPlan.PlannedChecks.ToArray();

            result.ValidationConfig = configuration.CloneWithoutSecrets();

            var safeConfig = configuration.CloneWithoutSecrets();
            var safeResult = result.CloneWithoutSecrets();
            ModelEvaluationArtifact? modelEvaluationArtifact = null;
                var modelEvaluationPolicy = configuration.Evaluation?.ModelEvaluation;

                if (modelEvaluationPolicy?.Enabled == true)
            {
                modelEvaluationArtifact = await _modelEvaluationExecutor.ExecuteAsync(
                    BuildModelEvaluationInput(safeResult),
                    executionPlan,
                    modelEvaluationPolicy,
                    cts.Token);
            }

            // Display main results; verbose turns on additional console output via ConsoleOutputService
            _consoleOutput.DisplayValidationResults(result, showDetails: verbose);
            DisplayAssuranceOutcome(result.Assurance);
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

            Environment.ExitCode = AssuranceExitCodeMapper.Map(result);
            if (structuredOutput)
            {
                Console.Out.WriteLine(JsonSerializer.Serialize(
                    new CliResultEnvelope { CommandName = "validate", ExitCode = Environment.ExitCode, Result = safeResult },
                    ArtifactJsonOptions.Create(writeIndented: false)));
            }
        }
        catch (CliExceptionBase cliEx)
        {
            var loggedTarget = SessionLogRedactor.Redact(targetEndpoint, RedactionLevel.Strict);
            if (structuredOutput)
            {
                WriteStructuredError(
                    cliEx.ErrorCode,
                    cliEx is CliUsageException ? "usage" : "operation",
                    cliEx.Message,
                    cliEx.ExitCode,
                    cliEx.IsTransient);
            }
            _consoleOutput.WriteError(cliEx.Message);
            _consoleOutput.WriteSessionLogHint("Validation log");
            _nextStepAdvisor.SuggestSessionLogReview("Validation log");
            _logger.LogWarning(cliEx, "Validation aborted for target {Target}: {Message}", loggedTarget, cliEx.Message);
            Environment.ExitCode = cliEx.ExitCode;
        }
        catch (OperationCanceledException)
        {
            var loggedTarget = SessionLogRedactor.Redact(targetEndpoint, RedactionLevel.Strict);
            if (structuredOutput)
            {
                WriteStructuredError("VALIDATION_TIMEOUT", "timeout", ValidationMessages.Errors.TimeoutOccurred, 124, retryable: true);
            }
            _consoleOutput.WriteError(ValidationMessages.Errors.TimeoutOccurred);
            _consoleOutput.WriteSessionLogHint("Validation log");
            _nextStepAdvisor.SuggestSessionLogReview("Validation log");
            _logger.LogWarning("Validation timed out for target {Target}", loggedTarget);
            Environment.ExitCode = 124;
        }
        catch (Exception ex)
        {
            var loggedTarget = SessionLogRedactor.Redact(targetEndpoint, RedactionLevel.Strict);
            if (structuredOutput)
            {
                WriteStructuredError("VALIDATION_RUNTIME_INTEGRITY", "runtime", ValidationMessages.Errors.UnexpectedError, AssuranceExitCodeMapper.RuntimeIntegrityFailure, retryable: false);
            }
            _consoleOutput.WriteError(ValidationMessages.Errors.UnexpectedError);
            _consoleOutput.WriteSessionLogHint("Validation log");
            _nextStepAdvisor.SuggestSessionLogReview("Validation log");
            _logger.LogError(ex, "Validation failed for target {Target}", loggedTarget);
            if (IsAuthenticationFailure(ex))
            {
                _nextStepAdvisor.SuggestAuthenticationFlow("validate", targetEndpoint);
            }
            Environment.ExitCode = AssuranceExitCodeMapper.RuntimeIntegrityFailure;
        }
        finally
        {
            if (!structuredOutput)
            {
                _nextStepAdvisor.Render();
            }
        }
    }

    private static void WriteStructuredError(string code, string category, string message, int exitCode, bool retryable)
    {
        Console.Error.WriteLine(JsonSerializer.Serialize(
            new CliErrorEnvelope
            {
                Code = code,
                Category = category,
                Message = message,
                ExitCode = exitCode,
                Retryable = retryable
            },
            ArtifactJsonOptions.Create(writeIndented: false)));
    }

    private static async Task<ValidationResult> LoadBaselineAsync(FileInfo baselineFile, CancellationToken cancellationToken)
    {
        if (!baselineFile.Exists)
        {
            throw new CliUsageException($"Baseline result file was not found: {baselineFile.FullName}");
        }

        return await BoundedArtifactReader.ReadValidationResultAsync(baselineFile, cancellationToken).ConfigureAwait(false);
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
            var normalized = Math.Clamp(
                maxConcurrency.Value,
                ExecutionPolicyDefaults.MinimumPositiveValue,
                ExecutionPolicyDefaults.MaximumConcurrency);
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

    private static void ApplyAssuranceOverride(McpValidatorConfiguration configuration, string? assuranceProfile)
    {
        var requestedProfile = string.IsNullOrWhiteSpace(assuranceProfile)
            ? configuration.Assurance.Profile
            : assuranceProfile;
        try
        {
            configuration.Assurance.Profile = AssuranceProfileCatalog.Resolve(requestedProfile).ProfileId;
        }
        catch (ArgumentException ex)
        {
            throw new CliUsageException(ex.Message);
        }
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

    private static void ApplyExtensionOverride(McpValidatorConfiguration configuration, string[]? extensionIds)
    {
        if (extensionIds == null || extensionIds.Length == 0)
        {
            return;
        }

        configuration.Validation ??= new ValidationConfig();
        configuration.Validation.Extensions = extensionIds
            .SelectMany(value => value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToList();
    }

    private async Task<string?> LoadOfficialConformanceEvidenceAsync(
        FileInfo? conformanceResultsFile,
        CancellationToken cancellationToken)
    {
        if (conformanceResultsFile == null)
        {
            return null;
        }

        try
        {
            var json = await BoundedArtifactReader.ReadTextAsync(
                conformanceResultsFile,
                OfficialConformanceContract.MaximumEvidenceBytes,
                cancellationToken).ConfigureAwait(false);
            _officialConformanceEvidenceImporter.Import(json);
            return json;
        }
        catch (Mcp.Benchmark.Core.Exceptions.OfficialConformanceEvidenceException exception)
        {
            throw new CliUsageException(exception.Message);
        }
    }

    private void AttachOfficialConformanceEvidence(
        ValidationResult result,
        string json,
        string targetIdentitySha256,
        string protocolVersion)
    {
        try
        {
            var imported = _officialConformanceEvidenceImporter.Import(json, new OfficialConformanceImportContext
            {
                ExpectedTargetIdentitySha256 = targetIdentitySha256,
                ExpectedProtocolVersion = protocolVersion
            });
            var reconciliation = OfficialConformanceReconciler.Reconcile(imported.Assessment, result.Assurance.Claims);
            imported.Assessment.Conflicts.AddRange(reconciliation.Conflicts);
            imported.Assessment.Decisions.AddRange(reconciliation.Decisions);
            if (reconciliation.Conflicts.Count > 0)
            {
                imported.Assessment.VerificationStatus = OfficialConformanceVerificationStatus.Conflict;
            }
            result.Assessments.OfficialConformance = imported.Assessment;
            result.Evidence.Coverage.AddRange(imported.Coverage);
            result.Evidence.Observations.AddRange(imported.Observations);
        }
        catch (Mcp.Benchmark.Core.Exceptions.OfficialConformanceEvidenceException exception)
        {
            throw new CliUsageException(exception.Message);
        }
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
        McpServerProfile? serverProfileOverride,
        ContentSafetyContextProfile? contentSafetyContextOverride)
    {
        McpValidatorConfiguration configuration;

        if (configFile is not null)
        {
            configuration = await BoundedArtifactReader.ReadConfigurationAsync(configFile).ConfigureAwait(false);
        }
        else
        {
            configuration = new McpValidatorConfiguration();
        }

        NormalizeServerConfig(configuration.Server, server);
        ApplySpecProfile(configuration, specProfile);
        ApplyServerProfileOverride(configuration, serverProfileOverride);
        ApplyContentSafetyContextOverride(configuration, contentSafetyContextOverride);

        return configuration;
    }

    private void EnsureAuthenticationPrerequisites(
        McpServerProfile profile,
        McpValidatorConfiguration configuration,
        string? token,
        bool interactive,
        string? endpointHint)
    {
        if (!RequiresStrictAuthentication(profile))
        {
            return;
        }

        var hasToken = !string.IsNullOrWhiteSpace(token) ||
                       !string.IsNullOrWhiteSpace(configuration.Server.Authentication?.Token) ||
                       configuration.Server.Authentication?.TokenRef is { Name.Length: > 0 };

        if (hasToken || interactive)
        {
            return;
        }

        _nextStepAdvisor.SuggestAuthenticationFlow("validate", endpointHint);
        throw new CliAuthenticationRequiredException(
            "Authenticated or enterprise servers require credentials. Provide -t/--token or enable -i/--interactive.");
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

    private static void ApplyContentSafetyContextOverride(
        McpValidatorConfiguration configuration,
        ContentSafetyContextProfile? contentSafetyContextOverride)
    {
        if (contentSafetyContextOverride.HasValue)
        {
            configuration.Server.ContentSafetyContext = contentSafetyContextOverride.Value;
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

    private static ContentSafetyContextProfile? ParseContentSafetyContextProfile(string? rawProfile)
    {
        if (string.IsNullOrWhiteSpace(rawProfile))
        {
            return null;
        }

        var normalized = rawProfile.Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Trim();

        foreach (var profile in Enum.GetValues<ContentSafetyContextProfile>())
        {
            if (string.Equals(profile.ToString(), normalized, StringComparison.OrdinalIgnoreCase))
            {
                return profile;
            }
        }

        return null;
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

    private void DisplayAssuranceOutcome(AssuranceEvaluation? assurance)
    {
        if (assurance == null)
        {
            return;
        }

        var assessments = assurance.Assessments;
        var summary = $"Assurance {assurance.Profile.ProfileId.ToUpperInvariant()}: " +
            $"core={assessments.CoreConformance.Status}, " +
            $"security={assessments.SecurityAssurance.Status}, " +
            $"operations={assessments.OperationalReadiness.Status}, " +
            $"certificate={assurance.CertificateEligibility.Status}.";
        if (assurance.CertificateEligibility.Status == CertificateEligibilityStatus.Eligible)
        {
            _consoleOutput.WriteSuccess(summary);
        }
        else
        {
            _consoleOutput.WriteWarning(summary);
        }

        foreach (var reason in assurance.CertificateEligibility.Reasons.Take(3))
        {
            _consoleOutput.WriteWarning($"Assurance reason: {reason}");
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

        var baseName = CreateArtifactBaseName(result.ValidationId);

        // Always work from a redacted clone when persisting artifacts so that
        // no tokens, passwords or sensitive headers are written to disk.
        var safeResult = result.CloneWithoutSecrets();

        // Human‑readable Markdown report used by most users.
        var markdownPath = Path.Combine(outputDirectory, $"{baseName}-report.md");
        var reportContent = _reportGenerator.GenerateReport(safeResult);
        await AtomicFileWriter.WriteAllTextAsync(markdownPath, reportContent, outputDirectory);

        var htmlPath = Path.Combine(outputDirectory, $"{baseName}-report.html");
        var includeDetailedSections = safeResult.ValidationConfig.Reporting.IncludesDetailedSections();
        var html = _reportRenderer.GenerateHtmlReport(safeResult, safeResult.ValidationConfig.Reporting, includeDetailedSections);
        await AtomicFileWriter.WriteAllTextAsync(htmlPath, html, outputDirectory);

        // Machine‑readable JSON snapshot for offline reporting (mcpval report).
        var jsonPath = Path.Combine(outputDirectory, $"{baseName}-result.json");
        var jsonOptions = ArtifactJsonOptions.Create();

        var jsonRoot = JsonSerializer.SerializeToNode(safeResult, jsonOptions) as JsonObject
            ?? throw new InvalidOperationException("Failed to serialize validation result to JSON.");
        AnnotatePerformanceMeasurementState(jsonRoot, safeResult.PerformanceTesting);
        var json = jsonRoot.ToJsonString(jsonOptions);
        await AtomicFileWriter.WriteAllTextAsync(jsonPath, json, outputDirectory);

        var sarifPath = Path.Combine(outputDirectory, $"{baseName}-results.sarif.json");
        var sarif = _reportRenderer.GenerateSarifReport(safeResult);
        await AtomicFileWriter.WriteAllTextAsync(sarifPath, sarif, outputDirectory);

        var junitPath = Path.Combine(outputDirectory, $"{baseName}-results.junit.xml");
        var junit = _reportRenderer.GenerateJunitReport(safeResult);
        await AtomicFileWriter.WriteAllTextAsync(junitPath, junit, outputDirectory);

        _logger.LogInformation("Markdown report saved: {MarkdownPath}", markdownPath);
        _logger.LogInformation("HTML report saved: {HtmlPath}", htmlPath);
        _logger.LogInformation("Validation result JSON saved: {JsonPath}", jsonPath);
        _logger.LogInformation("SARIF report saved: {SarifPath}", sarifPath);
        _logger.LogInformation("JUnit report saved: {JunitPath}", junitPath);
        _consoleOutput.WriteSuccess($"Reports generated: {markdownPath} and {htmlPath}");

        var savedPaths = new List<string> { markdownPath, htmlPath, jsonPath, sarifPath, junitPath };

        if (safeResult.ValidationConfig.Reporting.SignAttestation)
        {
            var privateKeyPem = Environment.GetEnvironmentVariable("MCPVAL_ATTESTATION_PRIVATE_KEY_PEM")!;
            var attestation = ValidationAttestationSigner.Sign(safeResult.ValidationId, jsonPath, privateKeyPem);
            var attestationPath = Path.Combine(outputDirectory, $"{baseName}-attestation.json");
            var attestationJson = JsonSerializer.Serialize(attestation, jsonOptions);
            await AtomicFileWriter.WriteAllTextAsync(attestationPath, attestationJson, outputDirectory);
            savedPaths.Add(attestationPath);
        }

        // Emit an aggregate profile summary artifact when client profiles are evaluated.
        if (safeResult.ClientCompatibility?.Assessments.Count > 0)
        {
            var profileSummaryPath = Path.Combine(outputDirectory, $"{baseName}-profile-summary.json");
            var profileSummary = BuildClientProfileSummary(safeResult);
            var profileSummaryJson = JsonSerializer.Serialize(profileSummary, jsonOptions);
            await AtomicFileWriter.WriteAllTextAsync(profileSummaryPath, profileSummaryJson, outputDirectory);
            _logger.LogInformation("Client profile summary saved: {Path}", profileSummaryPath);
            savedPaths.Add(profileSummaryPath);
        }

        if (modelEvaluationArtifact != null)
        {
            var modelEvaluationPath = Path.Combine(outputDirectory, $"{baseName}-model-evaluation.json");
            var modelEvaluationJson = JsonSerializer.Serialize(modelEvaluationArtifact, jsonOptions);
            await AtomicFileWriter.WriteAllTextAsync(modelEvaluationPath, modelEvaluationJson, outputDirectory);
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
        await AtomicFileWriter.WriteAllTextAsync(auditManifestPath, auditManifestJson, outputDirectory);
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

            var jsonOptions = ArtifactJsonOptions.Create();

            var baseName = CreateArtifactBaseName(executionPlan.SessionId);

            if (modelEvaluationArtifact != null)
            {
                var modelEvaluationPath = Path.Combine(explicitOutputDirectory, $"{baseName}-model-evaluation.json");
                var modelEvaluationJson = JsonSerializer.Serialize(modelEvaluationArtifact, jsonOptions);
                await AtomicFileWriter.WriteAllTextAsync(modelEvaluationPath, modelEvaluationJson, explicitOutputDirectory);
                artifactPaths.Add(modelEvaluationPath);
            }

            var updatedAuditManifest = _executionGovernanceService.BuildAuditManifest(
                executionPlan,
                result: null,
                artifactPaths,
                modelEvaluationArtifact);
            var auditManifestPath = Path.Combine(explicitOutputDirectory, $"{baseName}-audit.json");
            var auditManifestJson = JsonSerializer.Serialize(updatedAuditManifest, jsonOptions);
            await AtomicFileWriter.WriteAllTextAsync(auditManifestPath, auditManifestJson, explicitOutputDirectory);
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

    private static string CreateArtifactBaseName(string identity)
    {
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff");
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity ?? string.Empty)))
            .ToLowerInvariant()[..12];
        return $"mcp-validation-{timestamp}-{digest}";
    }

    private void DisplayExecutionPlan(ExecutionPlan executionPlan)
    {
        _consoleOutput.DisplaySessionBanner();
        Console.WriteLine();
        _consoleOutput.WriteHeader("EXECUTION PLAN");
        Console.WriteLine($"Target: {executionPlan.Target}");
        Console.WriteLine($"Transport: {executionPlan.Transport}");
        Console.WriteLine($"Protocol: {executionPlan.RequestedProtocolProfile} -> {executionPlan.ResolvedSchemaVersion} ({executionPlan.ProtocolEra.ToString().ToLowerInvariant()}; selection={executionPlan.ProtocolEraSelection.ToString().ToLowerInvariant()})");
        Console.WriteLine($"Mode: {executionPlan.ExecutionMode}");
        Console.WriteLine($"Dry Run: {(executionPlan.DryRun ? "enabled" : "disabled")}");
        Console.WriteLine($"Persistence: {executionPlan.PersistenceMode}");
        Console.WriteLine($"Redaction: {executionPlan.RedactionLevel}");
        Console.WriteLine($"Trace: {executionPlan.TraceMode}");
        Console.WriteLine($"Timeout: {executionPlan.TimeoutSeconds}s per request");
        Console.WriteLine($"Response Limit: {executionPlan.MaxResponseBytes} bytes");
        Console.WriteLine($"Request Budget: {executionPlan.MaxRequests}");
        Console.WriteLine($"Concurrency: {executionPlan.MaxConcurrency}");
        Console.WriteLine($"Provider Plan SHA-256: {executionPlan.ProviderExecutionPlanSha256}");
        Console.WriteLine($"Allowed Hosts: {(executionPlan.AllowedHosts.Count == 0 ? "(target host only)" : string.Join(", ", executionPlan.AllowedHosts))}");
        Console.WriteLine($"Allowed Origins: {(executionPlan.AllowedOrigins.Count == 0 ? "none" : string.Join(", ", executionPlan.AllowedOrigins))}");
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

    internal static string ComputeProviderExecutionPlanDigest(ExecutionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return CanonicalJsonDigest.ComputeObject(new
        {
            plan.TargetDigest,
            plan.Transport,
            plan.ResolvedSchemaVersion,
            plan.ProtocolEra,
            plan.ExecutionMode,
            plan.MaxRequests,
            plan.MaxConcurrency,
            plan.TimeoutSeconds,
            plan.MaxResponseBytes,
            AllowedHosts = plan.AllowedHosts.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            AllowedOrigins = plan.AllowedOrigins.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            PlannedChecks = plan.PlannedChecks.OrderBy(value => value, StringComparer.Ordinal).ToArray()
        });
    }

    internal static ProviderHarnessRunReceipt BuildCurrentProviderRunReceipt(
        ValidationResult result,
        string executionPlanSha256,
        DateTimeOffset completedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(result);
        var nonSuccessClaimIds = result.Assurance.Claims
            .Where(claim => claim.Outcome is not (AssuranceClaimOutcome.Satisfied or AssuranceClaimOutcome.NotApplicable))
            .Select(claim => claim.ClaimId)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var semanticResultSha256 = CanonicalJsonDigest.ComputeObject(new
        {
            CoreConformance = result.Assurance.Assessments.CoreConformance.Status,
            SecurityAssurance = result.Assurance.Assessments.SecurityAssurance.Status,
            OperationalReadiness = result.Assurance.Assessments.OperationalReadiness.Status,
            NonSuccessClaimIds = nonSuccessClaimIds
        });
        return new ProviderHarnessRunReceipt
        {
            RunId = result.ValidationId,
            ResultSha256 = semanticResultSha256,
            ExecutionPlanSha256 = executionPlanSha256,
            StartedAtUtc = new DateTimeOffset(result.StartTime.ToUniversalTime()),
            EndedAtUtc = completedAtUtc,
            CoreConformance = result.Assurance.Assessments.CoreConformance.Status,
            SecurityAssurance = result.Assurance.Assessments.SecurityAssurance.Status,
            OperationalReadiness = result.Assurance.Assessments.OperationalReadiness.Status,
            NonSuccessClaimIds = nonSuccessClaimIds
        };
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
            documentType = ArtifactContracts.ClientProfileSummaryDocumentType,
            documentSchemaVersion = ArtifactContracts.ClientProfileSummarySchemaVersion,
            generatedUtc = (result.EndTime ?? result.StartTime).ToUniversalTime().ToString("O"),
            validationId = result.ValidationId,
            validatorVersion = result.Producer.Version,
            serverEndpoint = result.ServerConfig.Endpoint,
            assuranceProfile = result.Assurance.Profile.ProfileId,
            certificateEligibility = result.Assurance.CertificateEligibility.Status,
            coreConformance = result.Assurance.Assessments.CoreConformance.Status,
            securityAssurance = result.Assurance.Assessments.SecurityAssurance.Status,
            operationalReadiness = result.Assurance.Assessments.OperationalReadiness.Status,
            policyPassed = result.PolicyOutcome?.Passed,
            policySummary = result.PolicyOutcome?.Summary,
            evidenceSummary = result.Assurance.EvidenceSummary,
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

    private static ModelEvaluationInput BuildModelEvaluationInput(ValidationResult result)
    {
        var aiFindings = result.ToolValidation?.AiReadinessFindings ?? new List<ValidationFinding>();
        return new ModelEvaluationInput
        {
            ValidationId = result.ValidationId,
            AssuranceProfile = result.Assurance.Profile.ProfileId,
            CoreConformance = result.Assurance.Assessments.CoreConformance.Status,
            SecurityAssurance = result.Assurance.Assessments.SecurityAssurance.Status,
            OperationalReadiness = result.Assurance.Assessments.OperationalReadiness.Status,
            CertificateEligibility = result.Assurance.CertificateEligibility.Status,
            NonSuccessClaims = result.Assurance.Claims
                .Where(claim => claim.Outcome is not (AssuranceClaimOutcome.Satisfied or AssuranceClaimOutcome.NotApplicable))
                .Select(claim => new ModelEvaluationClaimInput(claim.ClaimId, claim.Dimension, claim.Authority, claim.Outcome, claim.Subject))
                .ToArray(),
            ClientProfiles = result.ClientCompatibility?.Assessments
                .Select(assessment => new ModelEvaluationClientInput(assessment.ProfileId, assessment.Status))
                .ToArray() ?? Array.Empty<ModelEvaluationClientInput>(),
            AiFindings = aiFindings
                .Where(finding => !string.IsNullOrWhiteSpace(finding.RuleId))
                .Select(finding => new ModelEvaluationFindingInput(
                    finding.RuleId,
                    finding.Category,
                    finding.Component,
                    finding.Severity,
                    finding.Metadata.TryGetValue(AiReadinessEvidenceKinds.MetadataKey, out var evidenceKind)
                        ? evidenceKind
                        : AiReadinessEvidenceKinds.Infer(null, finding.RuleId)))
                .ToArray()
        };
    }

    private static void AnnotatePerformanceMeasurementState(JsonObject root, PerformanceTestResult? performance)
    {
        if (performance == null ||
            root["measurements"] is not JsonObject measurementsObject ||
            measurementsObject["performanceTesting"] is not JsonObject performanceObject)
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
