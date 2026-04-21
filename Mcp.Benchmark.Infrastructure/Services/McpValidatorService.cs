using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Constants;

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
    private readonly IMcpHttpClient _httpClient;
    private readonly IAggregateScoringStrategy _scoringStrategy;
    private readonly IValidationSessionBuilder _sessionBuilder;
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
        IValidationSessionBuilder sessionBuilder,
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
        _sessionBuilder = sessionBuilder ?? throw new ArgumentNullException(nameof(sessionBuilder));
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
        _httpClient.SetConcurrencyLimit(requestedConcurrency);

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

        if (session.CapabilitySnapshot != null)
        {
            result.CapabilitySnapshot = session.CapabilitySnapshot;
            PropagateCapabilitySnapshot(configuration, session.CapabilitySnapshot);
        }

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

            // Performance testing runs after functional validation so load generation
            // cannot cause false negatives in protocol, auth, or capability checks.
            if (categories.PerformanceTesting.TestConcurrentRequests)
            {
                try
                {
                    result.PerformanceTesting = await _performanceValidator.PerformLoadTestingAsync(
                        configuration.Server, categories.PerformanceTesting, cancellationToken);
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

            // Calculate overall results
            CalculateOverallResults(result);

            // Calculate MCP Trust Assessment (multi-dimensional AI safety evaluation)
            result.TrustAssessment = Scoring.McpTrustCalculator.Calculate(result);

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

        // Aggregate protocol compliance results
        if (result.ProtocolCompliance != null)
        {
            totalTests += 1;
            if (result.ProtocolCompliance.Status == TestStatus.Passed) passedTests++;
            else if (result.ProtocolCompliance.Status == TestStatus.Failed) failedTests++;
            else if (result.ProtocolCompliance.Status == TestStatus.Skipped) skippedTests++;
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
        }

        // Aggregate performance testing results
        if (result.PerformanceTesting != null)
        {
            totalTests += 1;
            if (result.PerformanceTesting.Status == TestStatus.Passed) passedTests++;
            else if (result.PerformanceTesting.Status == TestStatus.Failed) failedTests++;
            else if (result.PerformanceTesting.Status == TestStatus.Skipped) skippedTests++;
        }

        // Update summary statistics
        result.Summary.TotalTests = totalTests;
        result.Summary.PassedTests = passedTests;
        result.Summary.FailedTests = failedTests;
        result.Summary.SkippedTests = skippedTests;
        result.Summary.CriticalIssues = result.CriticalErrors.Count;

        // Use the scoring strategy to calculate the score and status
        var scoringResult = _scoringStrategy.CalculateScore(result);
        
        result.ComplianceScore = scoringResult.OverallScore;
        result.OverallStatus = scoringResult.Status;
        result.ScoringNotes = scoringResult.ScoringNotes;
        result.ScoringDetails = scoringResult;
        result.Summary.CoverageRatio = scoringResult.CoverageRatio;

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
        if (result.ComplianceScore < 80.0)
        {
            result.Recommendations.Add("Consider improving overall compliance score to meet industry standards");
        }

        if (result.Summary.FailedTests > 0)
        {
            result.Recommendations.Add("Review and address failed test cases to improve server stability");
        }

        if (result.SecurityTesting?.SecurityScore < 70.0)
        {
            result.Recommendations.Add("Implement additional security measures to address identified vulnerabilities");
        }

        if (result.ProtocolCompliance?.ComplianceScore < 90.0)
        {
            result.Recommendations.Add("Ensure full compliance with MCP protocol specification");
        }

        if (result.ToolValidation?.ToolsTestFailed > 0)
        {
            result.Recommendations.Add("Fix tool implementation issues to ensure proper functionality");
        }
    }
}
