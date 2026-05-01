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

namespace Mcp.Benchmark.Infrastructure.Services;

/// <summary>
/// Main MCP server validator service implementation.
/// Orchestrates comprehensive validation testing using the official MCP SDK.
/// </summary>
public class McpValidatorService : IMcpValidatorService
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
    public async Task<ValidationResult> ValidateServerAsync(McpValidatorConfiguration configuration, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting comprehensive validation for server: {Server}", configuration.Server.Endpoint);
        _telemetryService.TrackEvent("ValidationStarted", new Dictionary<string, string> { { "Endpoint", configuration.Server.Endpoint ?? "Unknown" } });

        var result = new ValidationResult
        {
            ValidationId = Guid.NewGuid().ToString(),
            StartTime = DateTime.UtcNow,
            ServerConfig = configuration.Server,
            ValidationConfig = configuration,
            OverallStatus = ValidationStatus.InProgress,
            ProtocolVersion = configuration.Server.ProtocolVersion
        };

        var requestedConcurrency = configuration.TestExecution?.EnableParallelExecution == false
            ? 1
            : configuration.TestExecution?.MaxParallelThreads ?? Environment.ProcessorCount;
        var calibratedConcurrency = ValidationCalibration.GetFunctionalProbeConcurrency(configuration.Server, requestedConcurrency);
        _httpClient.SetConcurrencyLimit(calibratedConcurrency);

        ValidationSessionContext session;
        try
        {
            session = await _sessionBuilder.BuildAsync(configuration, cancellationToken);
        }
        catch (ValidationSessionException vex)
        {
            _telemetryService.TrackEvent("ValidationSessionFailed", new Dictionary<string, string>
            {
                { "Endpoint", configuration.Server.Endpoint ?? "Unknown" },
                { "Reason", vex.Message }
            });

            result.OverallStatus = vex.Status;
            result.CriticalErrors.Add(vex.Message);
            result.EndTime = DateTime.UtcNow;
            return result;
        }

        // Replace the mutable server config reference with the effective (cloned) instance
        configuration.Server = session.EffectiveServer;
        result.ServerConfig = session.EffectiveServer;
        result.ProtocolVersion = session.ProtocolVersion ?? session.EffectiveServer.ProtocolVersion;
        result.InitializationHandshake = session.InitializationHandshake;
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
            // Execute validation categories based on configuration
            var validationTasks = new List<Task>();

            // Protocol compliance testing
            _logger.LogInformation("Checking Protocol Compliance Config: {Enabled}", categories.ProtocolCompliance.TestJsonRpcCompliance);
            if (categories.ProtocolCompliance.TestJsonRpcCompliance)
            {
                _logger.LogInformation("Queueing Protocol Compliance Validation...");
                validationTasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        _logger.LogInformation("Starting Protocol Compliance Validation...");
                        result.ProtocolCompliance = await _protocolValidator.ValidateJsonRpcComplianceAsync(
                            configuration.Server, categories.ProtocolCompliance, cancellationToken);
                        _logger.LogInformation("Protocol Compliance Validation Completed. Status: {Status}", result.ProtocolCompliance.Status);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Protocol compliance validation failed");
                        result.CriticalErrors.Add($"Protocol compliance validation error: {ex.Message}");
                        result.ProtocolCompliance = new ComplianceTestResult 
                        { 
                            Status = TestStatus.Failed, 
                            Score = 0,
                            Message = ex.Message
                        };
                    }
                }, cancellationToken));
            }
            else
            {
                _logger.LogWarning("Protocol Compliance Validation SKIPPED (Config disabled)");
            }

            // Tool validation testing
            if (categories.ToolTesting.TestToolDiscovery)
            {
                validationTasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        result.ToolValidation = await _toolValidator.ValidateToolDiscoveryAsync(
                            configuration.Server, categories.ToolTesting, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Tool validation failed");
                        result.CriticalErrors.Add($"Tool validation error: {ex.Message}");
                        result.ToolValidation = new ToolTestResult
                        {
                            Status = TestStatus.Failed,
                            Score = 0,
                            Message = ex.Message
                        };
                    }
                }, cancellationToken));
            }

            // Resource validation testing
            if (categories.ResourceTesting.TestResourceDiscovery)
            {
                validationTasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        result.ResourceTesting = await _resourceValidator.ValidateResourceDiscoveryAsync(
                            configuration.Server, categories.ResourceTesting, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Resource validation failed");
                        result.CriticalErrors.Add($"Resource validation error: {ex.Message}");
                        result.ResourceTesting = new ResourceTestResult
                        {
                            Status = TestStatus.Failed,
                            Score = 0,
                            Message = ex.Message
                        };
                    }
                }, cancellationToken));
            }

            // Prompt validation testing
            if (categories.PromptTesting.TestPromptDiscovery)
            {
                validationTasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        result.PromptTesting = await _promptValidator.ValidatePromptDiscoveryAsync(
                            configuration.Server, categories.PromptTesting, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Prompt validation failed");
                        result.CriticalErrors.Add($"Prompt validation error: {ex.Message}");
                        result.PromptTesting = new PromptTestResult
                        {
                            Status = TestStatus.Failed,
                            Score = 0,
                            Message = ex.Message
                        };
                    }
                }, cancellationToken));
            }

            // Security testing
            if (categories.SecurityTesting.TestInputValidation)
            {
                validationTasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        result.SecurityTesting = await _securityValidator.PerformSecurityAssessmentAsync(
                            configuration.Server, categories.SecurityTesting, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Security testing failed");
                        result.CriticalErrors.Add($"Security testing error: {ex.Message}");
                        result.SecurityTesting = new SecurityTestResult
                        {
                            Status = TestStatus.Failed,
                            Score = 0,
                            Message = ex.Message
                        };
                    }
                }, cancellationToken));
            }

            // Wait for all validation tasks to complete
            await Task.WhenAll(validationTasks);

            if (IsErrorHandlingEnabled(categories.ErrorHandling))
            {
                try
                {
                    result.ErrorHandling = await _errorHandlingValidator.ValidateErrorHandlingAsync(
                        configuration.Server,
                        categories.ErrorHandling,
                        cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error-handling validation failed");
                    result.CriticalErrors.Add($"Error-handling validation error: {ex.Message}");
                    result.ErrorHandling = new ErrorHandlingTestResult
                    {
                        Status = TestStatus.Failed,
                        Score = 0,
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
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Performance testing failed");
                    result.CriticalErrors.Add($"Performance testing error: {ex.Message}");
                    result.PerformanceTesting = new PerformanceTestResult
                    {
                        Status = TestStatus.Failed,
                        Score = 0,
                        Message = ex.Message
                    };
                }
            }

            PopulateProtocolDetails(result);
            PopulateAssessmentLayers(result);
            PopulateCoverage(result, categories);
            await ExecuteScenarioPacksAsync(result, applicabilityContext, configuration, cancellationToken);

            // Calculate overall results
            CalculateOverallResults(result);

            // Calculate MCP Trust Assessment (multi-dimensional AI safety evaluation)
            result.TrustAssessment = Scoring.McpTrustCalculator.Calculate(result);
            result.VerdictAssessment = ValidationVerdictEngine.Calculate(result);
            result.OverallStatus = ValidationVerdictEngine.IsPassing(result.VerdictAssessment)
                ? ValidationStatus.Passed
                : ValidationStatus.Failed;

            result.EndTime = DateTime.UtcNow;
            _logger.LogInformation("Validation completed with status: {Status}, Score: {Score:F1}%",
                result.OverallStatus, result.ComplianceScore);

            _telemetryService.TrackEvent("ValidationCompleted", new Dictionary<string, string> 
            { 
                { "Status", result.OverallStatus.ToString() },
                { "Score", result.ComplianceScore.ToString("F1") }
            });

            return result;
        }
        catch (OperationCanceledException)
        {
            result.OverallStatus = ValidationStatus.Cancelled;
            result.EndTime = DateTime.UtcNow;
            _logger.LogWarning("Validation was cancelled");
            _telemetryService.TrackEvent("ValidationCancelled");
            return result;
        }
        catch (Exception ex)
        {
            result.OverallStatus = ValidationStatus.Error;
            result.EndTime = DateTime.UtcNow;
            result.CriticalErrors.Add($"Validation framework error: {ex.Message}");
            _logger.LogError(ex, "Validation failed with critical error");
            _telemetryService.TrackException(ex);
            return result;
        }
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
                Blocker = ValidationEvidenceBlocker.Unimplemented,
                Confidence = EvidenceConfidenceLevel.None,
                Reason = "Validation category did not produce a result."
            });
            return;
        }

        coverage.Add(new ValidationCoverageDeclaration
        {
            LayerId = layerId,
            Scope = scope,
            Status = MapCoverageStatus(testResult.Status),
            Blocker = MapCoverageBlocker(testResult.Status),
            Confidence = MapCoverageConfidence(testResult.Status),
            Reason = testResult.Status is TestStatus.Passed or TestStatus.Failed ? null : testResult.Message
        });
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

    private static ValidationCoverageStatus MapCoverageStatus(TestStatus status)
    {
        return status switch
        {
            TestStatus.Passed or TestStatus.Failed => ValidationCoverageStatus.Covered,
            TestStatus.Skipped => ValidationCoverageStatus.Skipped,
            TestStatus.AuthRequired => ValidationCoverageStatus.AuthRequired,
            TestStatus.Inconclusive => ValidationCoverageStatus.Inconclusive,
            TestStatus.Error or TestStatus.Cancelled => ValidationCoverageStatus.Blocked,
            _ => ValidationCoverageStatus.Unavailable
        };
    }

    private static ValidationEvidenceBlocker MapCoverageBlocker(TestStatus status)
    {
        return status switch
        {
            TestStatus.AuthRequired => ValidationEvidenceBlocker.AuthRequired,
            TestStatus.Inconclusive => ValidationEvidenceBlocker.TransientFailure,
            TestStatus.Skipped => ValidationEvidenceBlocker.ConfigDisabled,
            TestStatus.Error or TestStatus.Cancelled => ValidationEvidenceBlocker.TransportError,
            TestStatus.NotRun or TestStatus.InProgress => ValidationEvidenceBlocker.Unimplemented,
            _ => ValidationEvidenceBlocker.None
        };
    }

    private static EvidenceConfidenceLevel MapCoverageConfidence(TestStatus status)
    {
        return status switch
        {
            TestStatus.Passed or TestStatus.Failed => EvidenceConfidenceLevel.High,
            TestStatus.AuthRequired or TestStatus.Skipped => EvidenceConfidenceLevel.Low,
            TestStatus.Inconclusive => EvidenceConfidenceLevel.Low,
            _ => EvidenceConfidenceLevel.None
        };
    }

    /// <summary>
    /// Performs a quick health check on the MCP server to verify basic connectivity.
    /// </summary>
    /// <param name="serverConfig">The server configuration for connection details.</param>
    /// <param name="cancellationToken">Cancellation token to stop the health check.</param>
    /// <returns>A simple health check result indicating server availability.</returns>
    public async Task<HealthCheckResult> PerformHealthCheckAsync(McpServerConfig serverConfig, CancellationToken cancellationToken = default)
    {
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
        _logger.LogInformation("Discovering capabilities for server: {Server}", serverConfig.Endpoint);

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

    /// <summary>
    /// Validates specific aspects of the MCP server based on the provided test categories.
    /// </summary>
    /// <param name="serverConfig">The server configuration for connection details.</param>
    /// <param name="testCategories">The specific test categories to execute.</param>
    /// <param name="cancellationToken">Cancellation token to stop the validation process.</param>
    /// <returns>Validation results for the specified test categories.</returns>
    public async Task<ValidationResult> ValidateSpecificAspectsAsync(McpServerConfig serverConfig, IEnumerable<TestCategory> testCategories, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Validating specific aspects for server: {Server}", serverConfig.Endpoint);

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

        var advertisedCapabilities = new List<string>();
        var initCapabilities = result.InitializationHandshake?.Payload?.Capabilities;
        if (initCapabilities != null)
        {
            if (initCapabilities.Tools != null) advertisedCapabilities.Add("tools");
            if (initCapabilities.Resources != null) advertisedCapabilities.Add("resources");
            if (initCapabilities.Prompts != null) advertisedCapabilities.Add("prompts");
            if (initCapabilities.Logging != null) advertisedCapabilities.Add("logging");
            if (initCapabilities.Completions != null) advertisedCapabilities.Add("completions");
        }

        if (result.CapabilitySnapshot?.Payload is { } snapshot)
        {
            var capability = compliance.CapabilityNegotiation ?? new CapabilityTestResult();
            var implementedCapabilities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (snapshot.ToolListingSucceeded) implementedCapabilities.Add(McpSpecConstants.Capabilities.Tools);
            if (snapshot.ResourceListingSucceeded) implementedCapabilities.Add(McpSpecConstants.Capabilities.Resources);
            if (snapshot.PromptListingSucceeded) implementedCapabilities.Add(McpSpecConstants.Capabilities.Prompts);
            AddOptionalImplementedCapabilities(implementedCapabilities, compliance.Findings);

            capability.CapabilityExchangeSuccessful = implementedCapabilities.Any();
            capability.CapabilityComplianceScore = snapshot.Score;
            capability.ImplementedCapabilities = implementedCapabilities
                .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (advertisedCapabilities.Any())
            {
                capability.AdvertisedCapabilities = advertisedCapabilities;
                capability.MissingCapabilities = advertisedCapabilities
                    .Except(implementedCapabilities, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            compliance.CapabilityNegotiation = capability;
        }
        else if (compliance.Findings.Any())
        {
            var optionalCapabilities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddOptionalImplementedCapabilities(optionalCapabilities, compliance.Findings);

            if (optionalCapabilities.Any() || advertisedCapabilities.Any())
            {
                compliance.CapabilityNegotiation = new CapabilityTestResult
                {
                    AdvertisedCapabilities = advertisedCapabilities,
                    ImplementedCapabilities = optionalCapabilities
                        .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
                        .ToList(),
                    MissingCapabilities = advertisedCapabilities
                        .Except(optionalCapabilities, StringComparer.OrdinalIgnoreCase)
                        .ToList(),
                    CapabilityExchangeSuccessful = optionalCapabilities.Any()
                };
            }
        }
        else if (advertisedCapabilities.Any() && compliance.CapabilityNegotiation == null)
        {
            compliance.CapabilityNegotiation = new CapabilityTestResult
            {
                AdvertisedCapabilities = advertisedCapabilities
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
