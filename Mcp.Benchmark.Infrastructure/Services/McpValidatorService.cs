using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Constants;
using Mcp.Benchmark.Core.Services;
using Mcp.Benchmark.Infrastructure.Registries;
using Mcp.Benchmark.Infrastructure.Scenarios;
using Mcp.Benchmark.Infrastructure.Utilities;
using Mcp.Compliance.Spec;

namespace Mcp.Benchmark.Infrastructure.Services;

/// <summary>
/// Main MCP server validator service implementation.
/// Orchestrates comprehensive validation testing using the official MCP SDK.
/// </summary>
public class McpValidatorService : IMcpValidatorService, ICorrelatedMcpValidatorService
{
    private readonly IProtocolComplianceValidator _protocolValidator;
    private readonly IToolValidator _toolValidator;
    private readonly IResourceValidator _resourceValidator;
    private readonly IPromptValidator _promptValidator;
    private readonly ISecurityValidator _securityValidator;
    private readonly IPerformanceValidator _performanceValidator;
    private readonly IErrorHandlingValidator _errorHandlingValidator;
    private readonly IMcpHttpClient _httpClient;
    private readonly IAggregateScoringStrategy _scoringStrategy;
    private readonly IValidationSessionBuilder _sessionBuilder;
    private readonly IValidationApplicabilityResolver _applicabilityResolver;
    private readonly IValidationPackRegistry<IProtocolFeaturePack> _protocolFeaturePackRegistry;
    private readonly IValidationPackRegistry<IValidationScenarioPack> _scenarioPackRegistry;
    private readonly IProtocolRuleRegistry _protocolRuleRegistry;
    private readonly IHealthCheckService _healthCheckService;
    private readonly ITelemetryService _telemetryService;
    private readonly ILogger<McpValidatorService> _logger;

    /// <summary>
    /// Initializes a new instance of the McpValidatorService class.
    /// </summary>
    public McpValidatorService(
        IProtocolComplianceValidator protocolValidator,
        IToolValidator toolValidator,
        IResourceValidator resourceValidator,
        IPromptValidator promptValidator,
        ISecurityValidator securityValidator,
        IPerformanceValidator performanceValidator,
        IErrorHandlingValidator errorHandlingValidator,
        IValidationSessionBuilder sessionBuilder,
        IValidationApplicabilityResolver applicabilityResolver,
        IValidationPackRegistry<IProtocolFeaturePack> protocolFeaturePackRegistry,
        IValidationPackRegistry<IValidationScenarioPack> scenarioPackRegistry,
        IProtocolRuleRegistry protocolRuleRegistry,
        IMcpHttpClient httpClient,
        IAggregateScoringStrategy scoringStrategy,
        IHealthCheckService healthCheckService,
        ITelemetryService telemetryService,
        ILogger<McpValidatorService> logger)
    {
        _protocolValidator = protocolValidator ?? throw new ArgumentNullException(nameof(protocolValidator));
        _toolValidator = toolValidator ?? throw new ArgumentNullException(nameof(toolValidator));
        _resourceValidator = resourceValidator ?? throw new ArgumentNullException(nameof(resourceValidator));
        _promptValidator = promptValidator ?? throw new ArgumentNullException(nameof(promptValidator));
        _securityValidator = securityValidator ?? throw new ArgumentNullException(nameof(securityValidator));
        _performanceValidator = performanceValidator ?? throw new ArgumentNullException(nameof(performanceValidator));
        _errorHandlingValidator = errorHandlingValidator ?? throw new ArgumentNullException(nameof(errorHandlingValidator));
        _sessionBuilder = sessionBuilder ?? throw new ArgumentNullException(nameof(sessionBuilder));
        _applicabilityResolver = applicabilityResolver ?? throw new ArgumentNullException(nameof(applicabilityResolver));
        _protocolFeaturePackRegistry = protocolFeaturePackRegistry ?? throw new ArgumentNullException(nameof(protocolFeaturePackRegistry));
        _scenarioPackRegistry = scenarioPackRegistry ?? throw new ArgumentNullException(nameof(scenarioPackRegistry));
        _protocolRuleRegistry = protocolRuleRegistry ?? throw new ArgumentNullException(nameof(protocolRuleRegistry));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _scoringStrategy = scoringStrategy ?? throw new ArgumentNullException(nameof(scoringStrategy));
        _healthCheckService = healthCheckService ?? throw new ArgumentNullException(nameof(healthCheckService));
        _telemetryService = telemetryService ?? throw new ArgumentNullException(nameof(telemetryService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Validates an MCP server against the specified configuration and scenarios.
    /// </summary>
    /// <param name="configuration">The validation configuration containing server details and test scenarios.</param>
    /// <param name="cancellationToken">Cancellation token to stop the validation process.</param>
    /// <returns>A comprehensive validation result containing all test outcomes.</returns>
    public Task<ValidationResult> ValidateServerAsync(McpValidatorConfiguration configuration, CancellationToken cancellationToken = default)
    {
        return ValidateServerAsync(ValidationRunRequest.Capture(configuration), cancellationToken);
    }

    public async Task<ValidationResult> ValidateServerAsync(ValidationRunRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var configuration = request.CreateConfiguration();
        ConfigureTransportPolicy(configuration.Server, configuration.Execution, force: true);
        var safeTarget = configuration.Server.CloneWithoutSecrets().Endpoint ?? "Unknown";
        _logger.LogInformation("Starting comprehensive validation for target: {Target}", safeTarget);
        _telemetryService.TrackEvent("ValidationStarted", new Dictionary<string, string> { { "Target", safeTarget } });

        var result = new ValidationResult
        {
            ValidationId = Guid.NewGuid().ToString(),
            StartTime = DateTime.UtcNow,
            ServerConfig = configuration.Server.CloneWithoutSecrets(),
            ValidationConfig = configuration.CloneForDeterministicResult(),
            OverallStatus = ValidationStatus.InProgress,
            ProtocolVersion = configuration.Server.ProtocolVersion
        };
        result.ValidationId = request.RequestId;
        var runStopwatch = System.Diagnostics.Stopwatch.StartNew();
        using var runTelemetry = ValidationObservability.BeginRun(
            result.ValidationId,
            configuration.Execution?.MaxRequests ?? ExecutionPolicyDefaults.DefaultMaxRequests);

        ValidationResult CompleteRun()
        {
            runStopwatch.Stop();
            result.Run.OperationalMetrics = runTelemetry.Complete(runStopwatch.Elapsed.TotalMilliseconds);
            result.Run.OperationalMetrics.EvidenceCoverageRatio = result.VerdictAssessment?.EvidenceSummary.EvidenceCoverageRatio
                ?? ValidationEvidenceSummarizer.Summarize(result.Evidence.Coverage).EvidenceCoverageRatio;
            return result;
        }

        var requestedConcurrency = configuration.TestExecution?.EnableParallelExecution == false
            ? 1
            : configuration.TestExecution?.MaxParallelThreads ?? Environment.ProcessorCount;
        var calibratedConcurrency = ValidationCalibration.GetFunctionalProbeConcurrency(configuration.Server, requestedConcurrency);
        _httpClient.SetConcurrencyLimit(calibratedConcurrency);

        ValidationSessionContext session;
        try
        {
            using (ValidationObservability.MeasureStage("session.bootstrap"))
            {
                session = await _sessionBuilder.BuildAsync(configuration, cancellationToken);
            }
        }
        catch (ValidationSessionException vex)
        {
            _telemetryService.TrackEvent("ValidationSessionFailed", new Dictionary<string, string>
            {
                { "Target", safeTarget },
                { "Reason", vex.Message }
            });

            result.OverallStatus = vex.Status;
            result.CriticalErrors.Add(vex.Message);
            result.EndTime = DateTime.UtcNow;
            return CompleteRun();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            result.OverallStatus = ValidationStatus.Cancelled;
            result.EndTime = DateTime.UtcNow;
            _telemetryService.TrackEvent("ValidationCancelled");
            return CompleteRun();
        }
        catch (OperationCanceledException ex)
        {
            result.OverallStatus = ValidationStatus.Error;
            result.EndTime = DateTime.UtcNow;
            result.CriticalErrors.Add("Validation framework error: an internal operation timed out or was cancelled without caller cancellation.");
            _logger.LogError(ex, "Validation bootstrap failed due to internal cancellation");
            _telemetryService.TrackException(ex);
            return CompleteRun();
        }

        // Replace the mutable server config reference with the effective (cloned) instance
        configuration.Server = session.EffectiveServer;
        result.ServerConfig = session.EffectiveServer;
        result.ProtocolVersion = session.ProtocolVersion ?? session.EffectiveServer.ProtocolVersion;
        result.InitializationHandshake = session.InitializationHandshake;
        result.ModernDiscovery = session.ModernDiscovery;
        result.BootstrapHealth = session.BootstrapHealth;
        result.ServerProfile = session.ServerProfile;
        result.ServerProfileSource = session.ServerProfileSource;

        var effectiveConcurrency = ValidationCalibration.GetFunctionalProbeConcurrency(session.EffectiveServer, requestedConcurrency);
        if (effectiveConcurrency != calibratedConcurrency)
        {
            _httpClient.SetConcurrencyLimit(effectiveConcurrency);
        }

        if (session.CapabilitySnapshot != null)
        {
            result.CapabilitySnapshot = session.CapabilitySnapshot;
            PropagateCapabilitySnapshot(configuration, session.CapabilitySnapshot);
        }

        var selectedClientProfiles = configuration.ClientProfiles?.Profiles?.Where(value => !string.IsNullOrWhiteSpace(value)).ToArray()
            ?? Array.Empty<string>();
        var applicabilityContext = _applicabilityResolver.Build(session, configuration, selectedClientProfiles);
        result.Run.SchemaVersion = applicabilityContext.SchemaVersion;
        result.Run.ApplicabilityContext = applicabilityContext;
        PopulateAppliedPacks(result, applicabilityContext);

        configuration.Validation ??= new ValidationConfig();
        configuration.Validation.Categories ??= new ValidationScenarios();
        var categories = configuration.Validation.Categories;
        categories.ProtocolCompliance.ModernDiscovery = session.ModernDiscovery?.Payload;

        if (session.AuthDiscovery != null)
        {
            categories.ToolTesting.PreDiscoveredAuth = session.AuthDiscovery;
        }

        foreach (var log in session.SessionLogs)
        {
            result.ExecutionLogs.Add(log);
        }

        try
        {
            _logger.LogInformation("Checking Protocol Compliance Config: {Enabled}", categories.ProtocolCompliance.TestJsonRpcCompliance);
            Func<Task<CategoryOutcome<ComplianceTestResult>?>> collectProtocol = () => categories.ProtocolCompliance.TestJsonRpcCompliance
                ? ExecuteCategoryAsync(
                    "Protocol compliance validation",
                    ct => _protocolValidator.ValidateJsonRpcComplianceAsync(configuration.Server, categories.ProtocolCompliance, ct),
                    ex => new ComplianceTestResult { Status = TestStatus.Error, Score = ScoringConstants.ScoreMinimum, Message = ex.Message },
                    cancellationToken)
                : Task.FromResult<CategoryOutcome<ComplianceTestResult>?>(null);
            Func<Task<CategoryOutcome<ToolTestResult>?>> collectTools = () => categories.ToolTesting.TestToolDiscovery
                ? ExecuteCategoryAsync(
                    "Tool validation",
                    ct => _toolValidator.ValidateToolDiscoveryAsync(configuration.Server, categories.ToolTesting, ct),
                    ex => new ToolTestResult { Status = TestStatus.Error, Score = ScoringConstants.ScoreMinimum, Message = ex.Message },
                    cancellationToken)
                : Task.FromResult<CategoryOutcome<ToolTestResult>?>(null);
            Func<Task<CategoryOutcome<ResourceTestResult>?>> collectResources = () => categories.ResourceTesting.TestResourceDiscovery
                ? ExecuteCategoryAsync(
                    "Resource validation",
                    ct => _resourceValidator.ValidateResourceDiscoveryAsync(configuration.Server, categories.ResourceTesting, ct),
                    ex => new ResourceTestResult { Status = TestStatus.Error, Score = ScoringConstants.ScoreMinimum, Message = ex.Message },
                    cancellationToken)
                : Task.FromResult<CategoryOutcome<ResourceTestResult>?>(null);
            Func<Task<CategoryOutcome<PromptTestResult>?>> collectPrompts = () => categories.PromptTesting.TestPromptDiscovery
                ? ExecuteCategoryAsync(
                    "Prompt validation",
                    ct => _promptValidator.ValidatePromptDiscoveryAsync(configuration.Server, categories.PromptTesting, ct),
                    ex => new PromptTestResult { Status = TestStatus.Error, Score = ScoringConstants.ScoreMinimum, Message = ex.Message },
                    cancellationToken)
                : Task.FromResult<CategoryOutcome<PromptTestResult>?>(null);
            Func<Task<CategoryOutcome<SecurityTestResult>?>> collectSecurity = () => categories.SecurityTesting.TestInputValidation
                ? ExecuteCategoryAsync(
                    "Security testing",
                    ct => _securityValidator.PerformSecurityAssessmentAsync(configuration.Server, categories.SecurityTesting, ct),
                    ex => new SecurityTestResult { Status = TestStatus.Error, Score = ScoringConstants.ScoreMinimum, Message = ex.Message },
                    cancellationToken)
                : Task.FromResult<CategoryOutcome<SecurityTestResult>?>(null);

            CategoryOutcome<ComplianceTestResult>? protocolOutcome;
            CategoryOutcome<ToolTestResult>? toolOutcome;
            CategoryOutcome<ResourceTestResult>? resourceOutcome;
            CategoryOutcome<PromptTestResult>? promptOutcome;
            CategoryOutcome<SecurityTestResult>? securityOutcome;

            if (ShouldCollectCategoriesInParallel(configuration))
            {
                using (ValidationObservability.MeasureStage("validation.categories"))
                {
                    var protocolTask = collectProtocol();
                    var toolTask = collectTools();
                    var resourceTask = collectResources();
                    var promptTask = collectPrompts();
                    var securityTask = collectSecurity();
                    await Task.WhenAll(protocolTask, toolTask, resourceTask, promptTask, securityTask);
                    protocolOutcome = protocolTask.Result;
                    toolOutcome = toolTask.Result;
                    resourceOutcome = resourceTask.Result;
                    promptOutcome = promptTask.Result;
                    securityOutcome = securityTask.Result;
                }
            }
            else
            {
                using (ValidationObservability.MeasureStage("validation.categories"))
                {
                    protocolOutcome = await collectProtocol();
                    toolOutcome = await collectTools();
                    resourceOutcome = await collectResources();
                    promptOutcome = await collectPrompts();
                    securityOutcome = await collectSecurity();
                }
            }

            ApplyCategoryOutcome(protocolOutcome, value => result.ProtocolCompliance = value, result);
            ApplyCategoryOutcome(toolOutcome, value => result.ToolValidation = value, result);
            ApplyCategoryOutcome(resourceOutcome, value => result.ResourceTesting = value, result);
            ApplyCategoryOutcome(promptOutcome, value => result.PromptTesting = value, result);
            ApplyCategoryOutcome(securityOutcome, value => result.SecurityTesting = value, result);

            if (IsErrorHandlingEnabled(categories.ErrorHandling))
            {
                try
                {
                    result.ErrorHandling = await _errorHandlingValidator.ValidateErrorHandlingAsync(
                        configuration.Server,
                        categories.ErrorHandling,
                        cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error-handling validation failed");
                    result.CriticalErrors.Add($"Error-handling validation error: {ex.Message}");
                    result.ErrorHandling = new ErrorHandlingTestResult
                    {
                        Status = TestStatus.Error,
                        Score = ScoringConstants.ScoreMinimum,
                        Message = ex.Message
                    };
                }
            }

            // Performance testing runs after functional validation so load generation
            // cannot cause false negatives in protocol, auth, or capability checks.
            if (categories.PerformanceTesting.TestConcurrentRequests)
            {
                try
                {
                    result.PerformanceTesting = await _performanceValidator.PerformLoadTestingAsync(
                        configuration.Server, categories.PerformanceTesting, cancellationToken);
                    ValidationCalibration.ApplyPerformanceOutcomeCalibration(configuration.Server, result.PerformanceTesting);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Performance testing failed");
                    result.CriticalErrors.Add($"Performance testing error: {ex.Message}");
                    result.PerformanceTesting = new PerformanceTestResult
                    {
                        Status = TestStatus.Error,
                        MeasurementDisposition = ex is TimeoutException or TaskCanceledException
                            ? PerformanceMeasurementDisposition.TimedOut
                            : PerformanceMeasurementDisposition.Unavailable,
                        Score = ScoringConstants.ScoreMinimum,
                        Message = ex.Message
                    };
                }
            }

            PopulateProtocolDetails(result);
            PopulateAssessmentLayers(result);
            PopulateCoverage(result, categories);
            using (ValidationObservability.MeasureStage("validation.scenarios"))
            {
                await ExecuteScenarioPacksAsync(result, applicabilityContext, configuration, cancellationToken);
            }

            // Calculate overall results
            using (ValidationObservability.MeasureStage("validation.scoring"))
            {
                CalculateOverallResults(result);
            }

            // Calculate MCP Trust Assessment (multi-dimensional AI safety evaluation)
            using (ValidationObservability.MeasureStage("validation.trust"))
            {
                result.TrustAssessment = Scoring.McpTrustCalculator.Calculate(result);
            }
            using (ValidationObservability.MeasureStage("validation.verdict"))
            {
                result.VerdictAssessment = ValidationVerdictEngine.Calculate(result);
            }
            Scoring.McpTrustCalculator.ApplyAuthoritativeVerdictCap(result.TrustAssessment, result.VerdictAssessment);
            result.OverallStatus = ValidationVerdictEngine.DetermineValidationStatus(result.VerdictAssessment);

            // Rewrite hardcoded MCP spec URLs (e.g. /specification/2025-11-25/...) to the
            // protocol version actually negotiated, when that version has an embedded
            // schema bundle. Runs LAST so it covers verdict decisions and evidence
            // observations populated by trust + verdict engines as well as the validator
            // findings/violations themselves.
            try
            {
                var embedded = Mcp.Compliance.Spec.SchemaRegistryProtocolVersions
                    .GetAvailableVersions()
                    .Select(v => v.Value)
                    .ToArray();
                Mcp.Benchmark.Core.Services.SpecReferenceVersionRewriter.Apply(
                    result,
                    result.ProtocolVersion,
                    embedded);
            }
            catch (Exception rewriteEx)
            {
                _logger.LogDebug(rewriteEx, "Spec-reference URL rewrite skipped due to non-fatal error.");
            }

            result.EndTime = DateTime.UtcNow;
            _logger.LogInformation("Validation completed with status: {Status}, Score: {Score:F1}%",
                result.OverallStatus, result.ComplianceScore);

            _telemetryService.TrackEvent("ValidationCompleted", new Dictionary<string, string>
            {
                { "Status", result.OverallStatus.ToString() },
                { "Score", result.ComplianceScore.ToString("F1") }
            });

            return CompleteRun();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            result.OverallStatus = ValidationStatus.Cancelled;
            result.EndTime = DateTime.UtcNow;
            _logger.LogWarning("Validation was cancelled");
            _telemetryService.TrackEvent("ValidationCancelled");
            return CompleteRun();
        }
        catch (OperationCanceledException ex)
        {
            result.OverallStatus = ValidationStatus.Error;
            result.EndTime = DateTime.UtcNow;
            result.CriticalErrors.Add("Validation framework error: an internal operation timed out or was cancelled without caller cancellation.");
            _logger.LogError(ex, "Validation failed due to internal cancellation");
            _telemetryService.TrackException(ex);
            return CompleteRun();
        }
        catch (Exception ex)
        {
            result.OverallStatus = ValidationStatus.Error;
            result.EndTime = DateTime.UtcNow;
            result.CriticalErrors.Add($"Validation framework error: {ex.Message}");
            _logger.LogError(ex, "Validation failed with critical error");
            _telemetryService.TrackException(ex);
            return CompleteRun();
        }
    }

    private async Task<CategoryOutcome<T>?> ExecuteCategoryAsync<T>(
        string categoryName,
        Func<CancellationToken, Task<T>> execute,
        Func<Exception, T> createFailure,
        CancellationToken cancellationToken)
    {
        try
        {
            return new CategoryOutcome<T>(await execute(cancellationToken), null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{CategoryName} failed", categoryName);
            return new CategoryOutcome<T>(createFailure(ex), $"{categoryName} error: {ex.Message}");
        }
    }

    private static void ApplyCategoryOutcome<T>(
        CategoryOutcome<T>? outcome,
        Action<T> apply,
        ValidationResult result)
    {
        if (outcome == null)
        {
            return;
        }

        apply(outcome.Value);
        if (!string.IsNullOrWhiteSpace(outcome.CriticalError))
        {
            result.CriticalErrors.Add(outcome.CriticalError);
        }
    }

    private sealed record CategoryOutcome<T>(T Value, string? CriticalError);

    private static bool ShouldCollectCategoriesInParallel(McpValidatorConfiguration configuration)
    {
        return configuration.TestExecution?.EnableParallelExecution == true &&
               !string.Equals(configuration.Server.Transport, "stdio", StringComparison.OrdinalIgnoreCase);
    }

    private void ConfigureTransportPolicy(McpServerConfig serverConfig, ExecutionPolicy? executionPolicy, bool force = false)
    {
        if (!force && _httpClient.IsExecutionPolicyConfigured)
        {
            return;
        }

        var policy = executionPolicy?.Clone() ?? new ExecutionPolicy();
        if (Uri.TryCreate(serverConfig.Endpoint, UriKind.Absolute, out var endpointUri))
        {
            if (policy.AllowedHosts.Count == 0)
            {
                policy.AllowedHosts.Add(NetworkTargetPolicy.NormalizeHost(endpointUri.IdnHost));
            }

            if (policy.AllowedOrigins.Count == 0)
            {
                policy.AllowedOrigins.Add(NetworkTargetPolicy.NormalizeOrigin(endpointUri));
                foreach (var host in policy.AllowedHosts.Where(host => !string.Equals(host, endpointUri.IdnHost, StringComparison.OrdinalIgnoreCase)))
                {
                    policy.AllowedOrigins.Add(NetworkTargetPolicy.CreateHttpsOrigin(host));
                }
            }
        }

        _httpClient.ConfigureExecutionPolicy(policy);
    }

    private static void PropagateCapabilitySnapshot(McpValidatorConfiguration configuration, TransportResult<CapabilitySummary> snapshot)
    {
        var categories = configuration.Validation?.Categories;
        if (categories == null)
        {
            return;
        }

        if (categories.ToolTesting != null)
        {
            categories.ToolTesting.CapabilitySnapshot = snapshot;
        }

        if (categories.ResourceTesting != null)
        {
            categories.ResourceTesting.CapabilitySnapshot = snapshot;
        }

        if (categories.PromptTesting != null)
        {
            categories.PromptTesting.CapabilitySnapshot = snapshot;
        }
    }

    private void PopulateAppliedPacks(ValidationResult result, ValidationApplicabilityContext applicabilityContext)
    {
        AddAppliedPacks(
            result.Evidence.AppliedPacks,
            _protocolFeaturePackRegistry.Resolve(applicabilityContext)
                .Select(pack => pack.Descriptor));

        AddAppliedPacks(
            result.Evidence.AppliedPacks,
            _scenarioPackRegistry.Resolve(applicabilityContext)
                .Select(pack => pack.Descriptor));

        AddAppliedPacks(
            result.Evidence.AppliedPacks,
            _protocolRuleRegistry.GetPacks()
                .Where(pack => ValidationPackApplicabilityMatcher.Matches(pack.Applicability, applicabilityContext))
                .Select(pack => pack.Descriptor));
    }

    private static void AddAppliedPacks(ICollection<ValidationPackDescriptor> target, IEnumerable<ValidationPackDescriptor> descriptors)
    {
        foreach (var descriptor in descriptors)
        {
            if (target.Any(existing => existing.Key.Equals(descriptor.Key) && existing.Revision.Equals(descriptor.Revision)))
            {
                continue;
            }

            target.Add(descriptor);
        }
    }

    private static void PopulateAssessmentLayers(ValidationResult result)
    {
        result.Assessments.Layers.Clear();

        AddLayer(result.Assessments.Layers, "protocol-core", "Protocol Compliance", result.ProtocolCompliance);
        AddLayer(result.Assessments.Layers, "tool-surface", "Tool Validation", result.ToolValidation);
        AddLayer(result.Assessments.Layers, "resource-surface", "Resource Validation", result.ResourceTesting);
        AddLayer(result.Assessments.Layers, "prompt-surface", "Prompt Validation", result.PromptTesting);
        AddLayer(result.Assessments.Layers, "security-boundaries", "Security Assessment", result.SecurityTesting);
        AddLayer(result.Assessments.Layers, "performance", "Performance Testing", result.PerformanceTesting);
        AddLayer(result.Assessments.Layers, "error-handling", "Error Handling", result.ErrorHandling);
    }

    private static void AddLayer(ICollection<ValidationLayerResult> layers, string layerId, string displayName, TestResultBase? testResult)
    {
        if (testResult == null)
        {
            return;
        }

        layers.Add(new ValidationLayerResult
        {
            LayerId = layerId,
            DisplayName = displayName,
            Status = testResult.Status,
            Summary = testResult.Message,
            Findings = testResult.Findings.ToList()
        });
    }

    private static void PopulateCoverage(ValidationResult result, ValidationScenarios categories)
    {
        result.Evidence.Coverage.Clear();

        AddCoverage(result.Evidence.Coverage, "protocol-core", "json-rpc", categories.ProtocolCompliance.TestJsonRpcCompliance, result.ProtocolCompliance);
        AddCoverage(result.Evidence.Coverage, "tool-surface", "tools/list", categories.ToolTesting.TestToolDiscovery, result.ToolValidation);
        AddCoverage(result.Evidence.Coverage, "resource-surface", "resources/list", categories.ResourceTesting.TestResourceDiscovery, result.ResourceTesting);
        AddCoverage(result.Evidence.Coverage, "prompt-surface", "prompts/list", categories.PromptTesting.TestPromptDiscovery, result.PromptTesting);
        AddCoverage(result.Evidence.Coverage, "security-boundaries", "security-assessment", categories.SecurityTesting.TestInputValidation, result.SecurityTesting);
        AddCoverage(result.Evidence.Coverage, "performance", "load-testing", categories.PerformanceTesting.TestConcurrentRequests, result.PerformanceTesting);
        AddCoverage(result.Evidence.Coverage, "error-handling", "error-handling", IsErrorHandlingEnabled(categories.ErrorHandling), result.ErrorHandling);
    }

    private static void AddCoverage(
        ICollection<ValidationCoverageDeclaration> coverage,
        string layerId,
        string scope,
        bool enabled,
        TestResultBase? testResult)
    {
        if (!enabled)
        {
            coverage.Add(new ValidationCoverageDeclaration
            {
                LayerId = layerId,
                Scope = scope,
                Status = ValidationCoverageStatus.Skipped,
                ObservedOutcome = ValidationOutcome.Skipped,
                Blocker = ValidationEvidenceBlocker.ConfigDisabled,
                Confidence = EvidenceConfidenceLevel.Low,
                Reason = "Validation category disabled by configuration."
            });
            return;
        }

        if (testResult == null)
        {
            coverage.Add(new ValidationCoverageDeclaration
            {
                LayerId = layerId,
                Scope = scope,
                Status = ValidationCoverageStatus.Unavailable,
                ObservedOutcome = ValidationOutcome.Unavailable,
                Blocker = ValidationEvidenceBlocker.Unimplemented,
                Confidence = EvidenceConfidenceLevel.None,
                Reason = "Validation category did not produce a result."
            });
            return;
        }

        coverage.Add(ValidationCoverageFactory.FromTestStatus(
            layerId,
            scope,
            testResult.Status,
            testResult.Message));
    }

    private async Task ExecuteScenarioPacksAsync(
        ValidationResult result,
        ValidationApplicabilityContext applicabilityContext,
        McpValidatorConfiguration configuration,
        CancellationToken cancellationToken)
    {
        result.Assessments.Scenarios.Clear();
        result.Evidence.Observations.Clear();

        var scenarioContext = new ValidationScenarioContext
        {
            ServerConfig = result.ServerConfig,
            ApplicabilityContext = applicabilityContext,
            ValidationConfiguration = configuration,
            ValidationResult = result
        };

        foreach (var pack in _scenarioPackRegistry.Resolve(applicabilityContext))
        {
            foreach (var scenario in pack.GetScenarios())
            {
                var execution = await scenario.ExecuteAsync(scenarioContext, cancellationToken);
                result.Assessments.Scenarios.Add(execution.Scenario);
                AddCoverageDeclarations(result.Evidence.Coverage, execution.Coverage);
                AddObservations(result.Evidence.Observations, execution.Observations);
            }
        }
    }

    private static void AddCoverageDeclarations(
        ICollection<ValidationCoverageDeclaration> target,
        IEnumerable<ValidationCoverageDeclaration> declarations)
    {
        foreach (var declaration in declarations)
        {
            if (target.Any(existing =>
                string.Equals(existing.LayerId, declaration.LayerId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(existing.Scope, declaration.Scope, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            target.Add(declaration);
        }
    }

    private static void AddObservations(
        ICollection<ValidationObservation> target,
        IEnumerable<ValidationObservation> observations)
    {
        foreach (var observation in observations)
        {
            var effectiveId = observation.Id;
            var suffix = 2;
            while (target.Any(existing => string.Equals(existing.Id, effectiveId, StringComparison.Ordinal)))
            {
                effectiveId = $"{observation.Id}-{suffix++}";
            }

            target.Add(new ValidationObservation
            {
                Id = effectiveId,
                LayerId = observation.LayerId,
                Component = observation.Component,
                ObservationKind = observation.ObservationKind,
                ScenarioId = observation.ScenarioId,
                RedactedPayloadPreview = observation.RedactedPayloadPreview,
                Metadata = observation.Metadata
            });
        }
    }

    private static bool IsErrorHandlingEnabled(ErrorHandlingConfig config)
    {
        return config.TestInvalidMethods ||
            config.TestMalformedJson ||
            config.TestConnectionInterruption ||
            config.TestTimeoutHandling ||
            config.TestGracefulDegradation ||
            config.CustomErrorScenarios.Count > 0;
    }

    /// <summary>
    /// Performs a quick health check on the MCP server to verify basic connectivity.
    /// </summary>
    /// <param name="serverConfig">The server configuration for connection details.</param>
    /// <param name="cancellationToken">Cancellation token to stop the health check.</param>
    /// <returns>A simple health check result indicating server availability.</returns>
    public async Task<HealthCheckResult> PerformHealthCheckAsync(McpServerConfig serverConfig, CancellationToken cancellationToken = default)
    {
        ConfigureTransportPolicy(serverConfig, executionPolicy: null);
        return await _healthCheckService.PerformHealthCheckAsync(serverConfig, cancellationToken);
    }

    /// <summary>
    /// Discovers the capabilities and features supported by the MCP server.
    /// </summary>
    /// <param name="serverConfig">The server configuration for connection details.</param>
    /// <param name="cancellationToken">Cancellation token to stop the discovery process.</param>
    /// <returns>A detailed report of server capabilities and supported features.</returns>
    public async Task<ServerCapabilities> DiscoverServerCapabilitiesAsync(McpServerConfig serverConfig, CancellationToken cancellationToken = default)
    {
        ConfigureTransportPolicy(serverConfig, executionPolicy: null);
        _logger.LogInformation("Discovering capabilities for target: {Target}", serverConfig.CloneWithoutSecrets().Endpoint);

        try
        {
            // REAL MCP SDK capability discovery implementation

            // Handle STDIO transport
            if (serverConfig.Transport?.ToLower() == "stdio")
            {
                _logger.LogWarning("STDIO transport capability discovery requires process spawning - not implemented in HTTP validator");
                throw new NotSupportedException("STDIO transport capability discovery requires process-based communication, not HTTP");
            }

            if (string.IsNullOrEmpty(serverConfig.Endpoint))
            {
                throw new ArgumentException("No endpoint specified for HTTP transport capability discovery");
            }

            _httpClient.SetProtocolVersion(serverConfig.ProtocolVersion);
            _httpClient.SetAuthentication(serverConfig.Authentication);

            if (ProtocolEraVersions.IsModern(serverConfig.ProtocolVersion))
            {
                var modernConfiguration = new McpValidatorConfiguration
                {
                    Server = serverConfig.CloneForExecution()
                };
                var session = await _sessionBuilder.BuildAsync(modernConfiguration, cancellationToken);
                var discovery = session.ModernDiscovery?.Payload;
                if (discovery?.IsValid != true)
                {
                    if (!ProtocolEraVersions.IsModern(session.ProtocolVersion))
                    {
                        return BuildLegacyDiscoveredCapabilities(serverConfig, session);
                    }

                    throw new InvalidOperationException(
                        session.ModernDiscovery?.Error ?? "Modern server/discover evidence is unavailable or invalid.");
                }

                var modernCapabilityPayload = session.CapabilitySnapshot?.Payload;
                return new ServerCapabilities
                {
                    ProtocolVersion = session.ProtocolVersion ?? serverConfig.ProtocolVersion ?? "Unknown",
                    Implementation = new ServerImplementation
                    {
                        Name = "Unknown Server",
                        Version = "Unknown",
                        Description = discovery.Instructions ?? "Discovered through modern server/discover."
                    },
                    SupportedTransports = [serverConfig.Transport ?? "http"],
                    SupportedTools = discovery.CapabilityNames.Contains(McpSpecConstants.Capabilities.Tools, StringComparer.OrdinalIgnoreCase)
                        ? [new ToolCapability { Name = "tools-validated", Description = $"Tool validation completed with score: {modernCapabilityPayload?.Score ?? 0:F1}%" }]
                        : [],
                    SupportedResources = discovery.CapabilityNames.Contains(McpSpecConstants.Capabilities.Resources, StringComparer.OrdinalIgnoreCase)
                        ? [new ResourceCapability { UriPattern = "*", Description = "Resources advertised by modern discovery." }]
                        : [],
                    SupportedPrompts = discovery.CapabilityNames.Contains(McpSpecConstants.Capabilities.Prompts, StringComparer.OrdinalIgnoreCase)
                        ? [new PromptCapability { Name = "prompts-advertised", Description = "Prompts advertised by modern discovery." }]
                        : []
                };
            }

            // REAL HTTP transport capability discovery using MCP initialize
            _logger.LogDebug("Performing REAL MCP capability discovery via initialize and capability validation");
            var initResult = await _httpClient.ValidateInitializeAsync(serverConfig.Endpoint, cancellationToken);

            if (!initResult.IsSuccessful)
            {
                throw new InvalidOperationException($"Failed to initialize MCP server for capability discovery: {initResult.Error}");
            }

            var initPayload = initResult.Payload;

            // REAL capability validation
            var capabilityResult = await _httpClient.ValidateCapabilitiesAsync(serverConfig.Endpoint, cancellationToken);
            var capabilityPayload = capabilityResult.Payload;
            var capabilityScore = capabilityPayload?.Score ?? 0;
            var toolValidationStatus = capabilityPayload?.ToolListingSucceeded == true ? "Successfully" : "Failed";

            var capabilities = new ServerCapabilities
            {
                ProtocolVersion = initPayload?.ProtocolVersion ?? "Unknown",
                Implementation = new ServerImplementation
                {
                    Name = initPayload?.ServerInfo?.Name ?? "Unknown Server",
                    Version = initPayload?.ServerInfo?.Version ?? "Unknown",
                    Description = "Discovered via REAL MCP protocol communication"
                },
                SupportedTransports = new List<string> { serverConfig.Transport ?? "http" },
                SupportedTools = new List<ToolCapability>
                {
                    // Basic tool capability based on validation results
                    new() { Name = "tools-validated", Description = $"Tool validation completed with score: {capabilityScore:F1}%" }
                },
                SupportedResources = new List<ResourceCapability>
                {
                    // Basic resource capability
                    new() { UriPattern = "http://*", Description = "HTTP-based resources discovered via MCP validation" }
                },
                SupportedPrompts = new List<PromptCapability>
                {
                    // Basic prompt capability
                    new() { Name = "validation-prompt", Description = "Prompt capabilities validated via MCP protocol" }
                }
            };

            _logger.LogInformation("REAL capability discovery completed: {ToolValidated} tools validated, score: {Score:F1}%",
                toolValidationStatus, capabilityScore);

            return capabilities;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Capability discovery failed");
            throw;
        }
    }

    private static ServerCapabilities BuildLegacyDiscoveredCapabilities(
        McpServerConfig serverConfig,
        ValidationSessionContext session)
    {
        var initialize = session.InitializationHandshake?.Payload;
        var capabilityPayload = session.CapabilitySnapshot?.Payload;
        return new ServerCapabilities
        {
            ProtocolVersion = session.ProtocolVersion ?? initialize?.ProtocolVersion ?? "Unknown",
            Implementation = new ServerImplementation
            {
                Name = initialize?.ServerInfo?.Name ?? "Unknown Server",
                Version = initialize?.ServerInfo?.Version ?? "Unknown",
                Description = "Discovered through negotiated legacy initialize and list capabilities."
            },
            SupportedTransports = [serverConfig.Transport ?? "http"],
            SupportedTools = capabilityPayload?.ToolListingSucceeded == true
                ? [new ToolCapability { Name = "tools-validated", Description = $"Tool validation completed with score: {capabilityPayload.Score:F1}%" }]
                : [],
            SupportedResources = capabilityPayload?.ResourceListingSucceeded == true
                ? [new ResourceCapability { UriPattern = "*", Description = "Resources validated through MCP list capabilities." }]
                : [],
            SupportedPrompts = capabilityPayload?.PromptListingSucceeded == true
                ? [new PromptCapability { Name = "prompts-validated", Description = "Prompts validated through MCP list capabilities." }]
                : []
        };
    }

    /// <summary>
    /// Validates specific aspects of the MCP server based on the provided test categories.
    /// </summary>
    /// <param name="serverConfig">The server configuration for connection details.</param>
    /// <param name="testCategories">The specific test categories to execute.</param>
    /// <param name="cancellationToken">Cancellation token to stop the validation process.</param>
    /// <returns>Validation results for the specified test categories.</returns>
    public async Task<ValidationResult> ValidateSpecificAspectsAsync(McpServerConfig serverConfig, IEnumerable<TestCategory> testCategories, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Validating specific aspects for target: {Target}", serverConfig.CloneWithoutSecrets().Endpoint);

        // Create a focused configuration based on the specified categories
        var configuration = new McpValidatorConfiguration
        {
            Server = serverConfig
        };

        // Enable only the specified test categories
        var categorySet = new HashSet<TestCategory>(testCategories);

        configuration.Validation.Categories.ProtocolCompliance.TestJsonRpcCompliance = categorySet.Contains(TestCategory.ProtocolCompliance);
        configuration.Validation.Categories.ToolTesting.TestToolDiscovery = categorySet.Contains(TestCategory.ToolValidation);
        configuration.Validation.Categories.SecurityTesting.TestInputValidation = categorySet.Contains(TestCategory.SecurityTesting);
        configuration.Validation.Categories.PerformanceTesting.TestConcurrentRequests = categorySet.Contains(TestCategory.PerformanceTesting);

        return await ValidateServerAsync(configuration, cancellationToken);
    }

    private void PopulateProtocolDetails(ValidationResult result)
    {
        if (result.ProtocolCompliance == null)
        {
            return;
        }

        var compliance = result.ProtocolCompliance;

        if (result.InitializationHandshake is { } handshake)
        {
            var initialization = compliance.Initialization ?? new InitializationTestResult();
            initialization.HandshakeSuccessful = handshake.IsSuccessful;
            initialization.InitializationTimeMs = handshake.Transport.Duration.TotalMilliseconds;
            initialization.ServerInfoProvided = !string.IsNullOrWhiteSpace(handshake.Payload?.ServerInfo?.Name);
            initialization.ClientInfoAccepted = handshake.Payload != null;
            if (!handshake.IsSuccessful && !string.IsNullOrWhiteSpace(handshake.Error))
            {
                initialization.InitializationErrors.Add(handshake.Error);
            }
            compliance.Initialization = initialization;
        }

        var advertisedCapabilities = CapabilitySnapshotUtils
            .ExtractAdvertisedCapabilities(result.InitializationHandshake?.Payload)
            .ToList();
        var capabilityDeclarationsAvailable = CapabilitySnapshotUtils.HasCapabilityDeclarations(result.InitializationHandshake?.Payload);

        if (result.CapabilitySnapshot?.Payload is { } snapshot)
        {
            if (snapshot.CapabilityDeclarationsAvailable)
            {
                advertisedCapabilities = snapshot.AdvertisedCapabilities.ToList();
                capabilityDeclarationsAvailable = true;
            }

            var capability = compliance.CapabilityNegotiation ?? new CapabilityTestResult();
            var implementedCapabilities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (snapshot.ToolListingSucceeded) implementedCapabilities.Add(McpSpecConstants.Capabilities.Tools);
            if (snapshot.ResourceListingSucceeded) implementedCapabilities.Add(McpSpecConstants.Capabilities.Resources);
            if (snapshot.PromptListingSucceeded) implementedCapabilities.Add(McpSpecConstants.Capabilities.Prompts);
            AddOptionalImplementedCapabilities(implementedCapabilities, compliance.Findings);

            capability.CapabilityExchangeSuccessful = capabilityDeclarationsAvailable || implementedCapabilities.Any();
            capability.CapabilityComplianceScore = snapshot.Score;
            capability.ImplementedCapabilities = implementedCapabilities
                .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (capabilityDeclarationsAvailable || advertisedCapabilities.Any())
            {
                capability.AdvertisedCapabilities = advertisedCapabilities;
                capability.MissingCapabilities = advertisedCapabilities
                    .Where(static capability => !capability.Contains('.', StringComparison.Ordinal))
                    .Except(implementedCapabilities, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            compliance.CapabilityNegotiation = capability;
        }
        else if (compliance.Findings.Any())
        {
            var optionalCapabilities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddOptionalImplementedCapabilities(optionalCapabilities, compliance.Findings);

            if (optionalCapabilities.Any() || advertisedCapabilities.Any() || capabilityDeclarationsAvailable)
            {
                compliance.CapabilityNegotiation = new CapabilityTestResult
                {
                    AdvertisedCapabilities = advertisedCapabilities,
                    ImplementedCapabilities = optionalCapabilities
                        .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
                        .ToList(),
                    MissingCapabilities = advertisedCapabilities
                        .Where(static capability => !capability.Contains('.', StringComparison.Ordinal))
                        .Except(optionalCapabilities, StringComparer.OrdinalIgnoreCase)
                        .ToList(),
                    CapabilityExchangeSuccessful = capabilityDeclarationsAvailable || optionalCapabilities.Any()
                };
            }
        }
        else if ((advertisedCapabilities.Any() || capabilityDeclarationsAvailable) && compliance.CapabilityNegotiation == null)
        {
            compliance.CapabilityNegotiation = new CapabilityTestResult
            {
                AdvertisedCapabilities = advertisedCapabilities,
                CapabilityExchangeSuccessful = capabilityDeclarationsAvailable
            };
        }
    }

    private static void AddOptionalImplementedCapabilities(ISet<string> implementedCapabilities, IEnumerable<ValidationFinding> findings)
    {
        foreach (var finding in findings)
        {
            if (!string.Equals(finding.Metadata.GetValueOrDefault("supported"), "true", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var capability = finding.Metadata.GetValueOrDefault("capability");
            if (!string.IsNullOrWhiteSpace(capability))
            {
                implementedCapabilities.Add(capability);
            }
        }
    }

    /// <summary>
    /// Calculates overall validation results and compliance scores.
    /// </summary>
    /// <param name="result">The validation result to calculate scores for.</param>
    private void CalculateOverallResults(ValidationResult result)
    {
        var totalTests = 0;
        var passedTests = 0;
        var failedTests = 0;
        var skippedTests = 0;
        var authRequiredTests = 0;
        var inconclusiveTests = 0;

        // Aggregate protocol compliance results
        if (result.ProtocolCompliance != null)
        {
            totalTests += 1;
            if (result.ProtocolCompliance.Status == TestStatus.Passed) passedTests++;
            else if (result.ProtocolCompliance.Status == TestStatus.Failed) failedTests++;
            else if (result.ProtocolCompliance.Status == TestStatus.Skipped) skippedTests++;
            else if (result.ProtocolCompliance.Status == TestStatus.AuthRequired) authRequiredTests++;
            else if (result.ProtocolCompliance.Status == TestStatus.Inconclusive) inconclusiveTests++;
        }

        // Aggregate tool validation results
        if (result.ToolValidation != null)
        {
            totalTests += result.ToolValidation.ToolsDiscovered;
            passedTests += result.ToolValidation.ToolsTestPassed;
            failedTests += result.ToolValidation.ToolsTestFailed;
        }

        // Aggregate security testing results
        if (result.SecurityTesting != null)
        {
            totalTests += 1;
            if (result.SecurityTesting.Status == TestStatus.Passed) passedTests++;
            else if (result.SecurityTesting.Status == TestStatus.Failed) failedTests++;
            else if (result.SecurityTesting.Status == TestStatus.Skipped) skippedTests++;
            else if (result.SecurityTesting.Status == TestStatus.AuthRequired) authRequiredTests++;
            else if (result.SecurityTesting.Status == TestStatus.Inconclusive) inconclusiveTests++;
        }

        // Aggregate performance testing results
        if (result.PerformanceTesting != null)
        {
            totalTests += 1;
            if (result.PerformanceTesting.Status == TestStatus.Passed) passedTests++;
            else if (result.PerformanceTesting.Status == TestStatus.Failed) failedTests++;
            else if (result.PerformanceTesting.Status == TestStatus.Skipped) skippedTests++;
            else if (result.PerformanceTesting.Status == TestStatus.AuthRequired) authRequiredTests++;
            else if (result.PerformanceTesting.Status == TestStatus.Inconclusive) inconclusiveTests++;
        }

        if (result.ErrorHandling != null)
        {
            totalTests += 1;
            if (result.ErrorHandling.Status == TestStatus.Passed) passedTests++;
            else if (result.ErrorHandling.Status == TestStatus.Failed) failedTests++;
            else if (result.ErrorHandling.Status == TestStatus.Skipped) skippedTests++;
            else if (result.ErrorHandling.Status == TestStatus.AuthRequired) authRequiredTests++;
            else if (result.ErrorHandling.Status == TestStatus.Inconclusive) inconclusiveTests++;
        }

        // Update summary statistics
        result.Summary.TotalTests = totalTests;
        result.Summary.PassedTests = passedTests;
        result.Summary.FailedTests = failedTests;
        result.Summary.SkippedTests = skippedTests;
        result.Summary.AuthRequiredTests = authRequiredTests;
        result.Summary.InconclusiveTests = inconclusiveTests;
        result.Summary.CriticalIssues = result.CriticalErrors.Count;

        // Use the scoring strategy to calculate the score and status
        var scoringResult = _scoringStrategy.CalculateScore(result);

        result.ComplianceScore = scoringResult.OverallScore;
        result.OverallStatus = scoringResult.Status;
        result.ScoringNotes = scoringResult.ScoringNotes;
        result.ScoringDetails = scoringResult;
        result.Summary.CoverageRatio = scoringResult.CoverageRatio;
        result.Summary.EvidenceConfidenceRatio = scoringResult.EvidenceSummary.EvidenceConfidenceRatio;

        // Generate recommendations based on results
        GenerateRecommendations(result);
        result.Recommendations = result.Recommendations
            .Where(recommendation => !string.IsNullOrWhiteSpace(recommendation))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Generates recommendations based on validation results.
    /// </summary>
    /// <param name="result">The validation result to generate recommendations for.</param>
    private void GenerateRecommendations(ValidationResult result)
    {
        var recommendations = new List<string>();

        recommendations.AddRange(BuildProtocolRecommendations(result));
        recommendations.AddRange(BuildSecurityRecommendations(result));
        recommendations.AddRange(BuildPerformanceRecommendations(result));
        recommendations.AddRange(BuildCatalogFindingRecommendations(result));

        result.Recommendations.AddRange(recommendations
            .Where(recommendation => !string.IsNullOrWhiteSpace(recommendation))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(6));
    }

    private static IEnumerable<string> BuildProtocolRecommendations(ValidationResult result)
    {
        if (result.ProtocolCompliance?.Violations?.Any() != true)
        {
            return Array.Empty<string>();
        }

        return result.ProtocolCompliance.Violations
            .Where(violation => !string.IsNullOrWhiteSpace(violation.Recommendation))
            .OrderByDescending(violation => violation.Severity)
            .Select(violation => PrefixRecommendation(
                ValidationRuleSourceClassifier.GetLabel(violation),
                violation.Recommendation!));
    }

    private static IEnumerable<string> BuildSecurityRecommendations(ValidationResult result)
    {
        if (result.SecurityTesting?.Vulnerabilities?.Any() != true)
        {
            return Array.Empty<string>();
        }

        return result.SecurityTesting.Vulnerabilities
            .Where(vulnerability => !string.IsNullOrWhiteSpace(vulnerability.Remediation))
            .OrderByDescending(vulnerability => vulnerability.Severity)
            .Select(vulnerability => PrefixRecommendation("security", vulnerability.Remediation!));
    }

    private static IEnumerable<string> BuildPerformanceRecommendations(ValidationResult result)
    {
        if (result.PerformanceTesting == null)
        {
            return Array.Empty<string>();
        }

        var performance = result.PerformanceTesting;
        if (!PerformanceMeasurementEvaluator.HasObservedMetrics(performance))
        {
            var reason = PerformanceMeasurementEvaluator.GetUnavailableReason(
                performance,
                "Performance measurements were not captured before the run ended.");

            return new[]
            {
                PrefixRecommendation("operational", $"Investigate why the performance probe ended without captured measurements ({reason}) before treating runtime behavior as representative.")
            };
        }

        return performance.Findings
            .Where(finding => finding.Severity > ValidationFindingSeverity.Info && !string.IsNullOrWhiteSpace(finding.Recommendation))
            .OrderByDescending(finding => finding.Severity)
            .Select(finding => PrefixRecommendation(finding.EffectiveSourceLabel, finding.Recommendation!));
    }

    private static IEnumerable<string> BuildCatalogFindingRecommendations(ValidationResult result)
    {
        var recommendations = new List<string>();

        recommendations.AddRange(BuildFindingCoverageRecommendations(
            result.ToolValidation?.ToolResults.SelectMany(tool => tool.Findings)
                .Concat(result.ToolValidation?.Findings ?? Enumerable.Empty<ValidationFinding>()),
            ValidationFindingAggregator.GetToolCatalogSize(result.ToolValidation),
            "tool"));

        recommendations.AddRange(BuildFindingCoverageRecommendations(
            result.PromptTesting?.PromptResults.SelectMany(prompt => prompt.Findings)
                .Concat(result.PromptTesting?.Findings ?? Enumerable.Empty<ValidationFinding>()),
            GetPromptCatalogSize(result.PromptTesting),
            "prompt"));

        recommendations.AddRange(BuildFindingCoverageRecommendations(
            result.ResourceTesting?.ResourceResults.SelectMany(resource => resource.Findings)
                .Concat(result.ResourceTesting?.Findings ?? Enumerable.Empty<ValidationFinding>()),
            GetResourceCatalogSize(result.ResourceTesting),
            "resource"));

        return recommendations;
    }

    private static IEnumerable<string> BuildFindingCoverageRecommendations(IEnumerable<ValidationFinding>? findings, int totalComponents, string componentLabel)
    {
        if (findings == null)
        {
            return Array.Empty<string>();
        }

        return ValidationFindingAggregator.SummarizeFindingsByRule(findings, totalComponents)
            .Where(rollup =>
                rollup.Severity > ValidationFindingSeverity.Info &&
                !string.IsNullOrWhiteSpace(rollup.Recommendation))
            .Select(rollup => PrefixRecommendation(
                rollup.SourceLabel,
                AppendCoverageScope(rollup.Recommendation!, rollup.AffectedComponents, rollup.TotalComponents, componentLabel)));
    }

    private static string AppendCoverageScope(string recommendation, int affectedComponents, int totalComponents, string componentLabel)
    {
        if (affectedComponents <= 0)
        {
            return recommendation;
        }

        var normalized = TrimSentence(recommendation);
        var noun = affectedComponents == 1 ? componentLabel : componentLabel + "s";
        if (totalComponents <= 0)
        {
            return $"{normalized} Gap affects {affectedComponents} {noun}.";
        }

        return $"{normalized} Gap affects {affectedComponents}/{totalComponents} {noun}.";
    }

    private static int GetPromptCatalogSize(PromptTestResult? promptTesting)
    {
        if (promptTesting == null)
        {
            return 0;
        }

        return promptTesting.PromptsDiscovered > 0
            ? promptTesting.PromptsDiscovered
            : promptTesting.PromptResults.Select(prompt => prompt.PromptName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();
    }

    private static int GetResourceCatalogSize(ResourceTestResult? resourceTesting)
    {
        if (resourceTesting == null)
        {
            return 0;
        }

        return resourceTesting.ResourcesDiscovered > 0
            ? resourceTesting.ResourcesDiscovered
            : resourceTesting.ResourceResults.Select(resource => resource.ResourceUri)
                .Where(uri => !string.IsNullOrWhiteSpace(uri))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();
    }

    private static string PrefixRecommendation(string authority, string recommendation)
    {
        var prefix = string.IsNullOrWhiteSpace(authority)
            ? "Action"
            : char.ToUpperInvariant(authority[0]) + authority[1..].ToLowerInvariant();

        return $"{prefix}: {TrimSentence(recommendation)}";
    }

    private static string TrimSentence(string text)
    {
        return text.Trim().TrimEnd('.');
    }
}
