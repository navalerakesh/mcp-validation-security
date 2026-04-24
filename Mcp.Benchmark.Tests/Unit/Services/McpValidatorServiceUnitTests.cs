using System.Collections.Generic;
using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Constants;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Services;
using Mcp.Benchmark.Infrastructure.Registries;
using Mcp.Benchmark.Infrastructure.Scenarios;
using Mcp.Benchmark.Infrastructure.Services;
using Mcp.Benchmark.Infrastructure.Rules.Protocol;
using Microsoft.Extensions.Logging;
using Moq;
using FluentAssertions;
using Xunit;
using Mcp.Compliance.Spec;

namespace Mcp.Benchmark.Tests.Unit;

/// <summary>
/// Unit tests for the McpValidatorService orchestrator.
/// Tests validation workflow coordination, result aggregation, and error handling.
/// </summary>
public class McpValidatorServiceUnitTests
{
    private readonly Mock<ILogger<McpValidatorService>> _loggerMock;
    private readonly Mock<IProtocolComplianceValidator> _protocolValidatorMock;
    private readonly Mock<IToolValidator> _toolValidatorMock;
    private readonly Mock<IResourceValidator> _resourceValidatorMock;
    private readonly Mock<IPromptValidator> _promptValidatorMock;
    private readonly Mock<ISecurityValidator> _securityValidatorMock;
    private readonly Mock<IPerformanceValidator> _performanceValidatorMock;
    private readonly Mock<IErrorHandlingValidator> _errorHandlingValidatorMock;
    private readonly Mock<IMcpHttpClient> _httpClientMock;
    private readonly Mock<IValidationSessionBuilder> _sessionBuilderMock;
    private readonly Mock<IValidationApplicabilityResolver> _applicabilityResolverMock;
    private readonly IValidationPackRegistry<IProtocolFeaturePack> _protocolFeaturePackRegistry;
    private readonly IValidationPackRegistry<IValidationScenarioPack> _scenarioPackRegistry;
    private readonly IProtocolRuleRegistry _protocolRuleRegistry;
    private readonly Mock<IAggregateScoringStrategy> _scoringStrategyMock;
    private readonly Mock<IHealthCheckService> _healthCheckServiceMock;
    private readonly Mock<ITelemetryService> _telemetryServiceMock;
    private readonly TransportResult<CapabilitySummary> _defaultCapabilitySnapshot;
    private readonly McpValidatorService _validatorService;

    public McpValidatorServiceUnitTests()
    {
        _loggerMock = new Mock<ILogger<McpValidatorService>>();
        _protocolValidatorMock = new Mock<IProtocolComplianceValidator>();
        _toolValidatorMock = new Mock<IToolValidator>();
        _resourceValidatorMock = new Mock<IResourceValidator>();
        _promptValidatorMock = new Mock<IPromptValidator>();
        _securityValidatorMock = new Mock<ISecurityValidator>();
        _performanceValidatorMock = new Mock<IPerformanceValidator>();
        _errorHandlingValidatorMock = new Mock<IErrorHandlingValidator>();
        _httpClientMock = new Mock<IMcpHttpClient>();
        _sessionBuilderMock = new Mock<IValidationSessionBuilder>();
        _applicabilityResolverMock = new Mock<IValidationApplicabilityResolver>();
        _protocolFeaturePackRegistry = new ValidationPackRegistry<IProtocolFeaturePack>(
            new IProtocolFeaturePack[] { new BuiltInProtocolFeaturePack(new EmbeddedSchemaRegistry()) });
        _scenarioPackRegistry = new ValidationPackRegistry<IValidationScenarioPack>(
            new IValidationScenarioPack[] { new BuiltInObservedSurfaceScenarioPack() });
        _protocolRuleRegistry = new ProtocolRuleRegistry(
            new ValidationPackRegistry<IValidationRulePack<ProtocolValidationContext>>(
                new IValidationRulePack<ProtocolValidationContext>[] { new BuiltInProtocolRulePack() }));
        _scoringStrategyMock = new Mock<IAggregateScoringStrategy>();
        _healthCheckServiceMock = new Mock<IHealthCheckService>();
        _telemetryServiceMock = new Mock<ITelemetryService>();

        _defaultCapabilitySnapshot = new TransportResult<CapabilitySummary>
        {
            IsSuccessful = true,
            Payload = new CapabilitySummary
            {
                ToolListingSucceeded = true,
                ToolInvocationSucceeded = true,
                DiscoveredToolsCount = 0
            },
            Transport = TransportMetadata.Empty
        };

        _sessionBuilderMock
            .Setup(x => x.BuildAsync(It.IsAny<McpValidatorConfiguration>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((McpValidatorConfiguration cfg, CancellationToken _) =>
            {
                var serverClone = new McpServerConfig
                {
                    Endpoint = cfg.Server.Endpoint,
                    Transport = cfg.Server.Transport,
                    Profile = cfg.Server.Profile,
                    ProtocolVersion = cfg.Server.ProtocolVersion,
                    Authentication = cfg.Server.Authentication,
                    TimeoutMs = cfg.Server.TimeoutMs,
                    Headers = new Dictionary<string, string>(cfg.Server.Headers),
                    Environment = new Dictionary<string, string>(cfg.Server.Environment)
                };

                return new ValidationSessionContext(cfg, serverClone)
                {
                    CapabilitySnapshot = _defaultCapabilitySnapshot,
                    ProtocolVersion = cfg.Server.ProtocolVersion ?? "2025-11-25"
                };
            });

        // Setup default scoring strategy behavior
        _scoringStrategyMock.Setup(x => x.CalculateScore(It.IsAny<ValidationResult>()))
            .Returns(new ScoringResult { OverallScore = 100.0, Status = ValidationStatus.Passed });

        // Setup default health check behavior
        _healthCheckServiceMock.Setup(x => x.PerformHealthCheckAsync(It.IsAny<McpServerConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HealthCheckResult { IsHealthy = true });

        _errorHandlingValidatorMock
            .Setup(x => x.ValidateErrorHandlingAsync(It.IsAny<McpServerConfig>(), It.IsAny<ErrorHandlingConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ErrorHandlingTestResult
            {
                Status = TestStatus.Passed,
                Score = 100,
                Message = "Validated 3 error scenario(s); 3 handled correctly.",
                ErrorScenariosTestCount = 3,
                ErrorScenariosHandledCorrectly = 3,
                ErrorScenarioResults = new List<ErrorScenarioResult>
                {
                    new()
                    {
                        ScenarioName = "Invalid Method Call",
                        ErrorType = "invalid-method",
                        HandledCorrectly = true,
                        ExpectedResponse = "JSON-RPC error code -32601 (Method not found).",
                        ActualResponse = "HTTP 200; JSON-RPC error -32601: Method not found",
                        GracefulRecovery = true
                    }
                }
            });

        _applicabilityResolverMock
            .Setup(x => x.Build(It.IsAny<ValidationSessionContext>(), It.IsAny<McpValidatorConfiguration>(), It.IsAny<IReadOnlyList<string>>()))
            .Returns((ValidationSessionContext session, McpValidatorConfiguration configuration, IReadOnlyList<string> selectedProfiles) => new ValidationApplicabilityContext
            {
                NegotiatedProtocolVersion = session.ProtocolVersion ?? configuration.Server.ProtocolVersion ?? "2025-11-25",
                SchemaVersion = session.ProtocolVersion ?? configuration.Server.ProtocolVersion ?? "2025-11-25",
                Transport = configuration.Server.Transport,
                AccessMode = configuration.Server.Profile.ToString(),
                ServerProfile = session.ServerProfile.ToString(),
                IsAuthenticated = !string.IsNullOrWhiteSpace(configuration.Server.Authentication?.Token),
                AdvertisedCapabilities = Array.Empty<string>(),
                AdvertisedSurfaces = Array.Empty<string>(),
                SelectedClientProfiles = selectedProfiles,
                EnvironmentHints = new Dictionary<string, string>()
            });

        _validatorService = new McpValidatorService(
            _protocolValidatorMock.Object,
            _toolValidatorMock.Object,
            _resourceValidatorMock.Object,
            _promptValidatorMock.Object,
            _securityValidatorMock.Object,
            _performanceValidatorMock.Object,
            _errorHandlingValidatorMock.Object,
            _sessionBuilderMock.Object,
            _applicabilityResolverMock.Object,
            _protocolFeaturePackRegistry,
            _scenarioPackRegistry,
            _protocolRuleRegistry,
            _httpClientMock.Object,
            _scoringStrategyMock.Object,
            _healthCheckServiceMock.Object,
            _telemetryServiceMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task ValidateServerAsync_WithAllValidatorsSuccessful_ShouldReturnPassedResult()
    {
        // Arrange
        var config = new McpValidatorConfiguration
        {
            Server = new McpServerConfig { Endpoint = "https://test-server.com/mcp" },
            Validation = new ValidationConfig
            {
                Categories = new ValidationScenarios
                {
                    ProtocolCompliance = new ProtocolComplianceConfig
                    {
                        TestJsonRpcCompliance = true,
                        TestInitialization = true
                    },
                    ToolTesting = new ToolTestingConfig
                    {
                        TestToolDiscovery = true
                    },
                    SecurityTesting = new SecurityTestingConfig
                    {
                        TestAuthenticationBypass = true
                    },
                    PerformanceTesting = new PerformanceTestingConfig
                    {
                        TestConcurrentRequests = true
                    }
                }
            }
        };

        // Mock successful protocol validation
        _protocolValidatorMock
            .Setup(x => x.ValidateJsonRpcComplianceAsync(It.IsAny<McpServerConfig>(), It.IsAny<ProtocolComplianceConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ComplianceTestResult
            {
                Status = TestStatus.Passed,
                ComplianceScore = 100.0,
                Violations = new List<ComplianceViolation>
                {
                    new()
                    {
                        CheckId = "MCP.INIT.VERSION_NEGOTIATION",
                        Category = "Initialization",
                        Severity = ViolationSeverity.Medium,
                        Description = "Server negotiated the requested version after retry."
                    }
                }
            });

        _protocolValidatorMock
            .Setup(x => x.ValidateInitializationAsync(It.IsAny<McpServerConfig>(), It.IsAny<ProtocolComplianceConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ComplianceTestResult { Status = TestStatus.Passed, ComplianceScore = 100.0 });

        _protocolValidatorMock
            .Setup(x => x.ValidateNotificationHandlingAsync(It.IsAny<McpServerConfig>(), It.IsAny<ProtocolComplianceConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ComplianceTestResult { Status = TestStatus.Passed, ComplianceScore = 100.0 });

        // Mock successful tool validation
        _toolValidatorMock
            .Setup(x => x.ValidateToolDiscoveryAsync(
                It.IsAny<McpServerConfig>(),
                It.Is<ToolTestingConfig>(cfg => cfg.CapabilitySnapshot != null),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ToolTestResult
            {
                Status = TestStatus.Passed,
                ToolResults = new List<IndividualToolResult>
                {
                    new()
                    {
                        ToolName = "delete_repo",
                        DisplayTitle = "Delete Repository",
                        Status = TestStatus.Passed,
                        Findings = new List<ValidationFinding>
                        {
                            new()
                            {
                                RuleId = ValidationFindingRuleIds.ToolGuidelineDestructiveHintMissing,
                                Category = "McpGuideline",
                                Component = "delete_repo",
                                Severity = ValidationFindingSeverity.High,
                                Summary = "Tool 'delete_repo' does not declare annotations.destructiveHint."
                            }
                        }
                    }
                }
            });

        // Mock successful security validation using correct interface method
        _securityValidatorMock
            .Setup(x => x.PerformSecurityAssessmentAsync(It.IsAny<McpServerConfig>(), It.IsAny<SecurityTestingConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SecurityTestResult
            {
                Status = TestStatus.Passed,
                AuthenticationTestResult = new AuthenticationTestResult
                {
                    Status = TestStatus.Passed,
                    TestScenarios = new List<AuthenticationScenario>
                    {
                        new()
                        {
                            ScenarioName = "Unauthorized tools/list challenge",
                            Method = "tools/list",
                            TestType = "challenge",
                            ActualBehavior = "Returned 401 with WWW-Authenticate.",
                            Analysis = "Challenge flow aligned with the expected bearer-token pattern.",
                            IsCompliant = true,
                            IsSecure = true,
                            IsStandardsAligned = true,
                            AssessmentDisposition = AuthenticationAssessmentDisposition.StandardsAligned,
                            StatusCode = "401"
                        }
                    }
                }
            });

        // Mock successful performance validation using correct interface method
        _performanceValidatorMock
            .Setup(x => x.PerformLoadTestingAsync(It.IsAny<McpServerConfig>(), It.IsAny<PerformanceTestingConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PerformanceTestResult { Status = TestStatus.Passed });

        // Act
        var result = await _validatorService.ValidateServerAsync(config, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.OverallStatus.Should().Be(ValidationStatus.Passed);
        result.ComplianceScore.Should().BeGreaterThan(0);
        result.ScoringDetails.Should().NotBeNull();
        result.ScoringDetails!.OverallScore.Should().Be(result.ComplianceScore);
        config.Validation.Categories.ToolTesting.CapabilitySnapshot.Should().BeSameAs(_defaultCapabilitySnapshot);
        config.Validation.Categories.ResourceTesting.CapabilitySnapshot.Should().BeSameAs(_defaultCapabilitySnapshot);
        config.Validation.Categories.PromptTesting.CapabilitySnapshot.Should().BeSameAs(_defaultCapabilitySnapshot);
        result.CapabilitySnapshot.Should().BeSameAs(_defaultCapabilitySnapshot);
        result.Run.SchemaVersion.Should().Be(result.ProtocolVersion);
        result.Run.ApplicabilityContext.Should().NotBeNull();
        result.Run.ApplicabilityContext!.NegotiatedProtocolVersion.Should().Be(result.ProtocolVersion);
        result.Run.ApplicabilityContext.Transport.Should().Be(config.Server.Transport);
        result.Evidence.AppliedPacks.Select(pack => pack.Key.Value)
            .Should().Contain(["protocol-features/mcp-embedded", "scenario-pack/observed-surface", "rule-pack/protocol-core"]);
        result.Assessments.Layers.Should().Contain(layer => layer.LayerId == "protocol-core" && layer.Status == TestStatus.Passed);
        result.Evidence.Coverage.Should().Contain(coverage =>
            coverage.LayerId == "protocol-core" &&
            coverage.Scope == "json-rpc" &&
            coverage.Status == ValidationCoverageStatus.Covered);
        result.Assessments.Scenarios.Should().Contain(scenario =>
            scenario.ScenarioId == "tool-catalog-smoke" &&
            scenario.Status == TestStatus.Passed);
        result.Assessments.Scenarios.Should().Contain(scenario =>
            scenario.ScenarioId == "security-authentication-challenge" &&
            scenario.Status == TestStatus.Passed);
        result.Assessments.Scenarios.Should().Contain(scenario =>
            scenario.ScenarioId == "error-handling-matrix" &&
            scenario.Status == TestStatus.Passed);
        result.Evidence.Observations.Should().Contain(observation =>
            observation.Id == "tool-delete-repo" &&
            observation.LayerId == "tool-surface");
        result.Evidence.Observations.Should().Contain(observation =>
            observation.Id == "protocol-mcp-init-version-negotiation" &&
            observation.LayerId == "protocol-core");
        result.Evidence.Observations.Should().Contain(observation =>
            observation.Id == "error-invalid-method-call" &&
            observation.LayerId == "error-handling");
        result.Evidence.Coverage.Should().Contain(coverage =>
            coverage.LayerId == "error-handling" &&
            coverage.Scope == "error-handling" &&
            coverage.Status == ValidationCoverageStatus.Covered);
    }

    [Fact]
    public async Task ValidateServerAsync_WithAuthenticatedProfile_ShouldClampFunctionalProbeConcurrency()
    {
        var config = new McpValidatorConfiguration
        {
            Server = new McpServerConfig
            {
                Endpoint = "https://test-server.com/mcp",
                Profile = McpServerProfile.Authenticated,
                Authentication = new AuthenticationConfig { Type = "bearer", Token = "token" }
            },
            Validation = new ValidationConfig
            {
                Categories = new ValidationScenarios()
            }
        };

        await _validatorService.ValidateServerAsync(config, CancellationToken.None);

        _httpClientMock.Verify(client => client.SetConcurrencyLimit(1), Times.AtLeastOnce);
    }

    [Fact]
    public async Task ValidateServerAsync_WithProtocolFailure_ShouldReturnFailedResult()
    {
        // Arrange
        var config = new McpValidatorConfiguration
        {
            Server = new McpServerConfig { Endpoint = "https://test-server.com/mcp" },
            Validation = new ValidationConfig
            {
                Categories = new ValidationScenarios
                {
                    ProtocolCompliance = new ProtocolComplianceConfig
                    {
                        TestJsonRpcCompliance = true
                    }
                }
            }
        };

        // Mock failed protocol validation with proper ComplianceViolation objects
        _protocolValidatorMock
            .Setup(x => x.ValidateJsonRpcComplianceAsync(It.IsAny<McpServerConfig>(), It.IsAny<ProtocolComplianceConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ComplianceTestResult 
            { 
                Status = TestStatus.Failed, 
                ComplianceScore = 50.0,
                Violations = new List<ComplianceViolation> 
                { 
                    new ComplianceViolation 
                    { 
                        Category = "JSON-RPC", 
                        Description = "Missing jsonrpc field",
                        Severity = ViolationSeverity.High
                    } 
                }
            });

        // Setup scoring strategy to reflect failure
        _scoringStrategyMock.Setup(x => x.CalculateScore(It.IsAny<ValidationResult>()))
            .Returns(new ScoringResult { OverallScore = 50.0, Status = ValidationStatus.Failed });

        // Act
        var result = await _validatorService.ValidateServerAsync(config, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.OverallStatus.Should().Be(ValidationStatus.Failed);
        result.ComplianceScore.Should().BeLessThan(100);
        config.Validation.Categories.ToolTesting.CapabilitySnapshot.Should().BeSameAs(_defaultCapabilitySnapshot);
        config.Validation.Categories.ResourceTesting.CapabilitySnapshot.Should().BeSameAs(_defaultCapabilitySnapshot);
        config.Validation.Categories.PromptTesting.CapabilitySnapshot.Should().BeSameAs(_defaultCapabilitySnapshot);
        result.CapabilitySnapshot.Should().BeSameAs(_defaultCapabilitySnapshot);
    }

    [Fact]
    public async Task ValidateServerAsync_ShouldRunPerformanceAfterCoreCategories()
    {
        var protocolCompleted = false;

        var config = new McpValidatorConfiguration
        {
            Server = new McpServerConfig { Endpoint = "https://test-server.com/mcp" },
            Validation = new ValidationConfig
            {
                Categories = new ValidationScenarios
                {
                    ProtocolCompliance = new ProtocolComplianceConfig
                    {
                        TestJsonRpcCompliance = true
                    },
                    PerformanceTesting = new PerformanceTestingConfig
                    {
                        TestConcurrentRequests = true
                    }
                }
            }
        };

        _protocolValidatorMock
            .Setup(x => x.ValidateJsonRpcComplianceAsync(It.IsAny<McpServerConfig>(), It.IsAny<ProtocolComplianceConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                protocolCompleted = true;
                return new ComplianceTestResult { Status = TestStatus.Passed, ComplianceScore = 100.0 };
            });

        _performanceValidatorMock
            .Setup(x => x.PerformLoadTestingAsync(It.IsAny<McpServerConfig>(), It.IsAny<PerformanceTestingConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                protocolCompleted.Should().BeTrue();
                return new PerformanceTestResult { Status = TestStatus.Passed, Score = 100.0 };
            });

        await _validatorService.ValidateServerAsync(config, CancellationToken.None);

        protocolCompleted.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateServerAsync_WithPublicTimeoutAndNoPerformanceMeasurements_ShouldCalibrateToAdvisory()
    {
        var config = new McpValidatorConfiguration
        {
            Server = new McpServerConfig
            {
                Endpoint = "https://test-server.com/mcp",
                Profile = McpServerProfile.Public
            },
            Validation = new ValidationConfig
            {
                Categories = new ValidationScenarios
                {
                    ProtocolCompliance = new ProtocolComplianceConfig
                    {
                        TestJsonRpcCompliance = false,
                        TestInitialization = false,
                        TestCapabilities = false,
                        TestNotifications = false,
                        TestMessageFormat = false
                    },
                    ToolTesting = new ToolTestingConfig
                    {
                        TestToolDiscovery = false,
                        TestToolExecution = false,
                        TestParameterValidation = false
                    },
                    ResourceTesting = new ResourceTestingConfig
                    {
                        TestResourceDiscovery = false,
                        TestResourceReading = false,
                        TestUriValidation = false,
                        TestSubscriptions = false
                    },
                    PromptTesting = new PromptTestingConfig
                    {
                        TestPromptDiscovery = false,
                        TestPromptExecution = false,
                        TestArgumentValidation = false
                    },
                    SecurityTesting = new SecurityTestingConfig
                    {
                        TestInputValidation = false,
                        TestInjectionAttacks = false,
                        TestAuthenticationBypass = false,
                        TestBufferOverflow = false,
                        TestMalformedMessages = false,
                        TestResourceExhaustion = false
                    },
                    PerformanceTesting = new PerformanceTestingConfig
                    {
                        TestConcurrentRequests = true,
                        TestResponseTimes = false,
                        TestMemoryUsage = false,
                        TestThroughput = false
                    },
                    ErrorHandling = new ErrorHandlingConfig
                    {
                        TestInvalidMethods = false,
                        TestMalformedJson = false,
                        TestConnectionInterruption = false,
                        TestTimeoutHandling = false
                    }
                }
            }
        };

        _performanceValidatorMock
            .Setup(x => x.PerformLoadTestingAsync(It.IsAny<McpServerConfig>(), It.IsAny<PerformanceTestingConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PerformanceTestResult
            {
                Status = TestStatus.Failed,
                Score = 0,
                Message = ValidationConstants.Messages.OperationTimedOutOrWasCancelled,
                CriticalErrors = new List<string> { ValidationConstants.Messages.OperationTimedOutOrWasCancelled }
            });

        var result = await _validatorService.ValidateServerAsync(config, CancellationToken.None);

        result.PerformanceTesting.Should().NotBeNull();
        result.PerformanceTesting!.Status.Should().Be(TestStatus.Skipped);
        result.PerformanceTesting.Score.Should().Be(ValidationCalibration.AdvisoryPerformanceScore);
        result.PerformanceTesting.Message.Should().Contain("advisory");
        result.PerformanceTesting.Findings.Should().Contain(f => f.RuleId == ValidationFindingRuleIds.PerformancePublicRemoteTimeoutAdvisory);
        result.Summary.SkippedTests.Should().Be(1);
        result.Summary.FailedTests.Should().Be(0);
        result.TrustAssessment.Should().NotBeNull();
        result.TrustAssessment!.OperationalReadiness.Should().Be(ValidationCalibration.AdvisoryPerformanceScore);
    }

    [Fact]
    public async Task ValidateServerAsync_ShouldKeepScoringNotesSeparateFromRecommendations()
    {
        var config = new McpValidatorConfiguration
        {
            Server = new McpServerConfig { Endpoint = "https://test-server.com/mcp" },
            Validation = new ValidationConfig
            {
                Categories = new ValidationScenarios()
            }
        };

        const string scoringNote = "Coverage-weighted score preserved for reporting only.";

        _scoringStrategyMock.Setup(x => x.CalculateScore(It.IsAny<ValidationResult>()))
            .Returns(new ScoringResult
            {
                OverallScore = 100.0,
                Status = ValidationStatus.Passed,
                ScoringNotes = new List<string> { scoringNote }
            });

        var result = await _validatorService.ValidateServerAsync(config, CancellationToken.None);

        result.ScoringNotes.Should().Contain(scoringNote);
        result.Recommendations.Should().NotContain(scoringNote);
    }

    [Fact]
    public async Task ValidateServerAsync_ShouldGenerateEvidenceDerivedRecommendationsInsteadOfBoilerplate()
    {
        var config = new McpValidatorConfiguration
        {
            Server = new McpServerConfig { Endpoint = "https://test-server.com/mcp" },
            Validation = new ValidationConfig
            {
                Categories = new ValidationScenarios
                {
                    ProtocolCompliance = new ProtocolComplianceConfig
                    {
                        TestJsonRpcCompliance = true
                    },
                    ToolTesting = new ToolTestingConfig
                    {
                        TestToolDiscovery = true
                    },
                    PerformanceTesting = new PerformanceTestingConfig
                    {
                        TestConcurrentRequests = true
                    }
                }
            }
        };

        _protocolValidatorMock
            .Setup(x => x.ValidateJsonRpcComplianceAsync(It.IsAny<McpServerConfig>(), It.IsAny<ProtocolComplianceConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ComplianceTestResult
            {
                Status = TestStatus.Failed,
                ComplianceScore = 81,
                Violations = new List<ComplianceViolation>
                {
                    new()
                    {
                        CheckId = "MCP.INIT.VERSION_NEGOTIATION",
                        Category = "Initialization",
                        Severity = ViolationSeverity.High,
                        Description = "Server ignored the requested MCP protocol version.",
                        Recommendation = "Echo the negotiated protocol version in initialize.result."
                    }
                }
            });

        _toolValidatorMock
            .Setup(x => x.ValidateToolDiscoveryAsync(
                It.IsAny<McpServerConfig>(),
                It.Is<ToolTestingConfig>(cfg => cfg.CapabilitySnapshot != null),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ToolTestResult
            {
                Status = TestStatus.Passed,
                ToolsDiscovered = 41,
                Findings = new List<ValidationFinding>
                {
                    new()
                    {
                        RuleId = ValidationFindingRuleIds.ToolGuidelineReadOnlyHintMissing,
                        Category = "McpGuideline",
                        Component = "create_branch",
                        Severity = ValidationFindingSeverity.Low,
                        Recommendation = "Declare readOnlyHint so agents can distinguish read-only tools from state-changing tools."
                    },
                    new()
                    {
                        RuleId = ValidationFindingRuleIds.AiReadinessVagueStringSchema,
                        Category = "AiReadiness",
                        Component = "create_branch",
                        Severity = ValidationFindingSeverity.Medium,
                        Recommendation = "Constrain string parameters with enum, pattern, or format metadata when possible."
                    }
                }
            });

        _performanceValidatorMock
            .Setup(x => x.PerformLoadTestingAsync(It.IsAny<McpServerConfig>(), It.IsAny<PerformanceTestingConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PerformanceTestResult
            {
                Status = TestStatus.Failed,
                Score = 0,
                Message = ValidationConstants.Messages.OperationTimedOutOrWasCancelled,
                CriticalErrors = new List<string> { ValidationConstants.Messages.OperationTimedOutOrWasCancelled }
            });

        _scoringStrategyMock.Setup(x => x.CalculateScore(It.IsAny<ValidationResult>()))
            .Returns(new ScoringResult
            {
                OverallScore = 68.4,
                Status = ValidationStatus.Passed
            });

        var result = await _validatorService.ValidateServerAsync(config, CancellationToken.None);

        result.Recommendations.Should().Contain(recommendation => recommendation.StartsWith("Spec:", StringComparison.Ordinal));
        result.Recommendations.Should().Contain(recommendation => recommendation.StartsWith("Guideline:", StringComparison.Ordinal));
        result.Recommendations.Should().Contain(recommendation => recommendation.StartsWith("Heuristic:", StringComparison.Ordinal));
        result.Recommendations.Should().Contain(recommendation => recommendation.StartsWith("Operational:", StringComparison.Ordinal));
        result.Recommendations.Should().NotContain(recommendation => recommendation.Contains("improving overall compliance score", StringComparison.OrdinalIgnoreCase));
        result.Recommendations.Should().NotContain(recommendation => recommendation.Contains("failed test cases", StringComparison.OrdinalIgnoreCase));
        result.Recommendations.Should().NotContain(recommendation => recommendation.Contains("Ensure full compliance with MCP protocol specification", StringComparison.OrdinalIgnoreCase));
    }
}
