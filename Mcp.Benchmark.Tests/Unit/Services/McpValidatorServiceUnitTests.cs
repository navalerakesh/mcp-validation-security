using System.Collections.Generic;
using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using Moq;
using FluentAssertions;
using Xunit;

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
    private readonly Mock<IMcpHttpClient> _httpClientMock;
    private readonly Mock<IValidationSessionBuilder> _sessionBuilderMock;
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
        _httpClientMock = new Mock<IMcpHttpClient>();
        _sessionBuilderMock = new Mock<IValidationSessionBuilder>();
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

        _validatorService = new McpValidatorService(
            _protocolValidatorMock.Object,
            _toolValidatorMock.Object,
            _resourceValidatorMock.Object,
            _promptValidatorMock.Object,
            _securityValidatorMock.Object,
            _performanceValidatorMock.Object,
                _sessionBuilderMock.Object,
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
            .ReturnsAsync(new ComplianceTestResult { Status = TestStatus.Passed, ComplianceScore = 100.0 });

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
            .ReturnsAsync(new ToolTestResult { Status = TestStatus.Passed });

        // Mock successful security validation using correct interface method
        _securityValidatorMock
            .Setup(x => x.PerformSecurityAssessmentAsync(It.IsAny<McpServerConfig>(), It.IsAny<SecurityTestingConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SecurityTestResult { Status = TestStatus.Passed });

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
}
