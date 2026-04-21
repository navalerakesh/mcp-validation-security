using FluentAssertions;
using System.Text.Json;
using Mcp.Benchmark.CLI;
using Mcp.Benchmark.CLI.Abstractions;
using Mcp.Benchmark.ClientProfiles;
using Mcp.Benchmark.CLI.Services;
using Mcp.Benchmark.CLI.Utilities;
using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Mcp.Benchmark.Tests.Unit.Commands;

public sealed class ValidateCommandTests : IDisposable
{
    private readonly List<string> _sessionRoots = new();
    private readonly List<string> _tempFiles = new();
    private readonly int _originalExitCode = Environment.ExitCode;

    [Fact]
    public async Task ExecuteAsync_WithAdvisoryPolicy_ShouldReturnExitCodeZeroForValidationFailures()
    {
        var sessionContext = CreateSessionContext();
        var consoleOutput = new Mock<IConsoleOutputService>(MockBehavior.Loose);
        var validatorService = new Mock<IMcpValidatorService>();
        validatorService
            .Setup(service => service.ValidateServerAsync(It.IsAny<McpValidatorConfiguration>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult
            {
                OverallStatus = ValidationStatus.Failed,
                ServerConfig = new McpServerConfig { Endpoint = "https://example.test/mcp", Transport = "http" },
                TrustAssessment = new McpTrustAssessment
                {
                    MustFailCount = 1,
                    TrustLevel = McpTrustLevel.L2_Caution
                }
            });

        var command = new ValidateCommand(
            validatorService.Object,
            consoleOutput.Object,
            new Mock<IClientProfileEvaluator>(MockBehavior.Loose).Object,
            new Mock<IReportGenerator>(MockBehavior.Loose).Object,
            new Mock<IValidationReportRenderer>(MockBehavior.Loose).Object,
            new Mock<IGitHubActionsReporter>(MockBehavior.Loose).Object,
            NullLogger<ValidateCommand>.Instance,
            new Mock<INextStepAdvisor>(MockBehavior.Loose).Object,
            new Mock<ISessionArtifactStore>(MockBehavior.Loose).Object,
            sessionContext);

        await command.ExecuteAsync(
            server: "https://example.test/mcp",
            outputDirectory: null,
            specProfile: null,
            configFile: null,
            verbose: false,
            token: null,
            interactive: false,
            serverProfile: null,
            maxConcurrency: null,
            policyMode: ValidationPolicyModes.Advisory);

        Environment.ExitCode.Should().Be(0);
        consoleOutput.Verify(output => output.WriteSuccess(It.Is<string>(value => value.Contains("ADVISORY", StringComparison.OrdinalIgnoreCase))), Times.AtLeastOnce());
    }

    [Fact]
    public async Task ExecuteAsync_WithStrictPolicy_ShouldReturnExitCodeOneForBalancedPassButLowTrust()
    {
        var sessionContext = CreateSessionContext();
        var consoleOutput = new Mock<IConsoleOutputService>(MockBehavior.Loose);
        var validatorService = new Mock<IMcpValidatorService>();
        validatorService
            .Setup(service => service.ValidateServerAsync(It.IsAny<McpValidatorConfiguration>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult
            {
                OverallStatus = ValidationStatus.Passed,
                ServerConfig = new McpServerConfig { Endpoint = "https://example.test/mcp", Transport = "http" },
                TrustAssessment = new McpTrustAssessment
                {
                    TrustLevel = McpTrustLevel.L3_Acceptable,
                    MustFailCount = 0,
                    ShouldFailCount = 0
                }
            });

        var command = new ValidateCommand(
            validatorService.Object,
            consoleOutput.Object,
            new Mock<IClientProfileEvaluator>(MockBehavior.Loose).Object,
            new Mock<IReportGenerator>(MockBehavior.Loose).Object,
            new Mock<IValidationReportRenderer>(MockBehavior.Loose).Object,
            new Mock<IGitHubActionsReporter>(MockBehavior.Loose).Object,
            NullLogger<ValidateCommand>.Instance,
            new Mock<INextStepAdvisor>(MockBehavior.Loose).Object,
            new Mock<ISessionArtifactStore>(MockBehavior.Loose).Object,
            sessionContext);

        await command.ExecuteAsync(
            server: "https://example.test/mcp",
            outputDirectory: null,
            specProfile: null,
            configFile: null,
            verbose: false,
            token: null,
            interactive: false,
            serverProfile: null,
            maxConcurrency: null,
            policyMode: ValidationPolicyModes.Strict);

        Environment.ExitCode.Should().Be(1);
        consoleOutput.Verify(output => output.WriteError(It.Is<string>(value => value.Contains("STRICT", StringComparison.OrdinalIgnoreCase))), Times.AtLeastOnce());
    }

        [Fact]
        public async Task ExecuteAsync_WithStrictPolicySuppressionFromConfig_ShouldReturnExitCodeZero()
        {
                var sessionContext = CreateSessionContext();
                var consoleOutput = new Mock<IConsoleOutputService>(MockBehavior.Loose);
                var validatorService = new Mock<IMcpValidatorService>();
            McpValidatorConfiguration? capturedConfiguration = null;
                validatorService
                        .Setup(service => service.ValidateServerAsync(It.IsAny<McpValidatorConfiguration>(), It.IsAny<CancellationToken>()))
                .Callback<McpValidatorConfiguration, CancellationToken>((configuration, _) => capturedConfiguration = configuration)
                        .ReturnsAsync(new ValidationResult
                        {
                                OverallStatus = ValidationStatus.Passed,
                                ServerConfig = new McpServerConfig { Endpoint = "https://example.test/mcp", Transport = "http" },
                                TrustAssessment = new McpTrustAssessment
                                {
                                        TrustLevel = McpTrustLevel.L3_Acceptable,
                                        TierChecks = new List<ComplianceTierCheck>()
                                }
                        });

                var command = new ValidateCommand(
                        validatorService.Object,
                        consoleOutput.Object,
                    new Mock<IClientProfileEvaluator>(MockBehavior.Loose).Object,
                        new Mock<IReportGenerator>(MockBehavior.Loose).Object,
                        new Mock<IValidationReportRenderer>(MockBehavior.Loose).Object,
                        new Mock<IGitHubActionsReporter>(MockBehavior.Loose).Object,
                        NullLogger<ValidateCommand>.Instance,
                        new Mock<INextStepAdvisor>(MockBehavior.Loose).Object,
                        new Mock<ISessionArtifactStore>(MockBehavior.Loose).Object,
                        sessionContext);

                var configPath = Path.Combine(Path.GetTempPath(), $"validation-policy-{Guid.NewGuid():N}.json");
                _tempFiles.Add(configPath);
                await File.WriteAllTextAsync(
                        configPath,
                        """
                        {
                            "server": {
                                "endpoint": "https://example.test/mcp",
                                "transport": "http"
                            },
                            "policy": {
                                "mode": "strict",
                                "suppressions": [
                                    {
                                        "id": "suppress-low-trust-during-rollout",
                                        "signalId": "POLICY.TRUST.L4_MINIMUM",
                                        "owner": "navalerakesh",
                                        "reason": "Temporary rollout exception.",
                                        "expiresOn": "2099-01-01T00:00:00Z"
                                    }
                                ]
                            }
                        }
                        """);

                await command.ExecuteAsync(
                        server: string.Empty,
                        outputDirectory: null,
                        specProfile: null,
                        configFile: new FileInfo(configPath),
                        verbose: false,
                        token: null,
                        interactive: false,
                        serverProfile: null,
                        maxConcurrency: null,
                        policyMode: null);

                Environment.ExitCode.Should().Be(0);
                    capturedConfiguration.Should().NotBeNull();
                    capturedConfiguration!.Policy.Mode.Should().Be(ValidationPolicyModes.Strict);
                    capturedConfiguration.Policy.Suppressions.Should().ContainSingle();
                    capturedConfiguration.Policy.Suppressions[0].SignalId.Should().Be("POLICY.TRUST.L4_MINIMUM");
        }

    [Fact]
    public async Task ExecuteAsync_WithOutputDirectory_ShouldPersistFullReportsByDefault()
    {
        var sessionContext = CreateSessionContext();
        var outputRoot = Path.Combine(Path.GetTempPath(), $"validate-output-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outputRoot);
        _sessionRoots.Add(outputRoot);

        var consoleOutput = new Mock<IConsoleOutputService>(MockBehavior.Loose);
        var validatorService = new Mock<IMcpValidatorService>();
        validatorService
            .Setup(service => service.ValidateServerAsync(It.IsAny<McpValidatorConfiguration>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult
            {
                OverallStatus = ValidationStatus.Passed,
                ServerConfig = new McpServerConfig { Endpoint = "https://example.test/mcp", Transport = "http" },
                ValidationConfig = new McpValidatorConfiguration { Reporting = new ReportingConfig() },
                TrustAssessment = new McpTrustAssessment { TrustLevel = McpTrustLevel.L4_Trusted }
            });

        var reportGenerator = new Mock<IReportGenerator>();
        reportGenerator.Setup(generator => generator.GenerateReport(It.IsAny<ValidationResult>())).Returns("# markdown");

        var reportRenderer = new Mock<IValidationReportRenderer>();
        reportRenderer.Setup(renderer => renderer.GenerateHtmlReport(It.IsAny<ValidationResult>(), It.IsAny<ReportingConfig>(), It.IsAny<bool>())).Returns("<html></html>");
        reportRenderer.Setup(renderer => renderer.GenerateSarifReport(It.IsAny<ValidationResult>())).Returns("{}");

        var command = new ValidateCommand(
            validatorService.Object,
            consoleOutput.Object,
            new Mock<IClientProfileEvaluator>(MockBehavior.Loose).Object,
            reportGenerator.Object,
            reportRenderer.Object,
            new Mock<IGitHubActionsReporter>(MockBehavior.Loose).Object,
            NullLogger<ValidateCommand>.Instance,
            new Mock<INextStepAdvisor>(MockBehavior.Loose).Object,
            new Mock<ISessionArtifactStore>(MockBehavior.Loose).Object,
            sessionContext);

        await command.ExecuteAsync(
            server: "https://example.test/mcp",
            outputDirectory: new DirectoryInfo(outputRoot),
            specProfile: null,
            configFile: null,
            verbose: false,
            token: null,
            interactive: false,
            serverProfile: null,
            maxConcurrency: null,
            policyMode: ValidationPolicyModes.Advisory);

        Directory.GetFiles(outputRoot, "*-report.md").Should().ContainSingle();
        Directory.GetFiles(outputRoot, "*-report.html").Should().ContainSingle();
        Directory.GetFiles(outputRoot, "*-result.json").Should().ContainSingle();
        Directory.GetFiles(outputRoot, "*-results.sarif.json").Should().ContainSingle();

        reportRenderer.Verify(renderer => renderer.GenerateHtmlReport(
            It.IsAny<ValidationResult>(),
            It.IsAny<ReportingConfig>(),
            true), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_WithTimedOutPerformanceAndNoMeasurements_ShouldAnnotateSavedJson()
    {
        var sessionContext = CreateSessionContext();
        var outputRoot = Path.Combine(Path.GetTempPath(), $"validate-output-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outputRoot);
        _sessionRoots.Add(outputRoot);

        var consoleOutput = new Mock<IConsoleOutputService>(MockBehavior.Loose);
        var validatorService = new Mock<IMcpValidatorService>();
        validatorService
            .Setup(service => service.ValidateServerAsync(It.IsAny<McpValidatorConfiguration>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult
            {
                OverallStatus = ValidationStatus.Failed,
                ServerConfig = new McpServerConfig { Endpoint = "https://example.test/mcp", Transport = "http" },
                ValidationConfig = new McpValidatorConfiguration { Reporting = new ReportingConfig() },
                PerformanceTesting = new PerformanceTestResult
                {
                    Status = TestStatus.Failed,
                    Message = "Operation timed out or was cancelled",
                    CriticalErrors = new List<string> { "Operation timed out or was cancelled" }
                },
                TrustAssessment = new McpTrustAssessment { TrustLevel = McpTrustLevel.L2_Caution }
            });

        var reportGenerator = new Mock<IReportGenerator>();
        reportGenerator.Setup(generator => generator.GenerateReport(It.IsAny<ValidationResult>())).Returns("# markdown");

        var reportRenderer = new Mock<IValidationReportRenderer>();
        reportRenderer.Setup(renderer => renderer.GenerateHtmlReport(It.IsAny<ValidationResult>(), It.IsAny<ReportingConfig>(), It.IsAny<bool>())).Returns("<html></html>");
        reportRenderer.Setup(renderer => renderer.GenerateSarifReport(It.IsAny<ValidationResult>())).Returns("{}");

        var command = new ValidateCommand(
            validatorService.Object,
            consoleOutput.Object,
            new Mock<IClientProfileEvaluator>(MockBehavior.Loose).Object,
            reportGenerator.Object,
            reportRenderer.Object,
            new Mock<IGitHubActionsReporter>(MockBehavior.Loose).Object,
            NullLogger<ValidateCommand>.Instance,
            new Mock<INextStepAdvisor>(MockBehavior.Loose).Object,
            new Mock<ISessionArtifactStore>(MockBehavior.Loose).Object,
            sessionContext);

        await command.ExecuteAsync(
            server: "https://example.test/mcp",
            outputDirectory: new DirectoryInfo(outputRoot),
            specProfile: null,
            configFile: null,
            verbose: false,
            token: null,
            interactive: false,
            serverProfile: null,
            maxConcurrency: null,
            policyMode: ValidationPolicyModes.Advisory);

        var jsonPath = Directory.GetFiles(outputRoot, "*-result.json").Should().ContainSingle().Subject;
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(jsonPath));
        var performance = document.RootElement.GetProperty("performanceTesting");

        performance.GetProperty("measurementsCaptured").GetBoolean().Should().BeFalse();
        performance.GetProperty("measurementStatus").GetString().Should().Be("Unavailable");
        performance.GetProperty("measurementReason").GetString().Should().Be("Operation timed out or was cancelled");
    }

    [Fact]
    public async Task ExecuteAsync_WithOutputDirectory_ShouldPublishActualArtifactPathsToGitHubReporter()
    {
        var sessionContext = CreateSessionContext();
        var outputRoot = Path.Combine(Path.GetTempPath(), $"validate-output-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outputRoot);
        _sessionRoots.Add(outputRoot);

        var consoleOutput = new Mock<IConsoleOutputService>(MockBehavior.Loose);
        var validatorService = new Mock<IMcpValidatorService>();
        validatorService
            .Setup(service => service.ValidateServerAsync(It.IsAny<McpValidatorConfiguration>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult
            {
                StartTime = new DateTime(2026, 4, 21, 8, 35, 35, DateTimeKind.Utc),
                OverallStatus = ValidationStatus.Passed,
                ServerConfig = new McpServerConfig { Endpoint = "https://example.test/mcp", Transport = "http" },
                ValidationConfig = new McpValidatorConfiguration { Reporting = new ReportingConfig() },
                TrustAssessment = new McpTrustAssessment { TrustLevel = McpTrustLevel.L4_Trusted }
            });

        var reportGenerator = new Mock<IReportGenerator>();
        reportGenerator.Setup(generator => generator.GenerateReport(It.IsAny<ValidationResult>())).Returns("# markdown");

        var reportRenderer = new Mock<IValidationReportRenderer>();
        reportRenderer.Setup(renderer => renderer.GenerateHtmlReport(It.IsAny<ValidationResult>(), It.IsAny<ReportingConfig>(), It.IsAny<bool>())).Returns("<html></html>");
        reportRenderer.Setup(renderer => renderer.GenerateSarifReport(It.IsAny<ValidationResult>())).Returns("{}");

        List<string>? publishedArtifactPaths = null;
        var gitHubReporter = new Mock<IGitHubActionsReporter>(MockBehavior.Loose);
        gitHubReporter
            .Setup(reporter => reporter.PublishValidationResult(It.IsAny<ValidationResult>(), It.IsAny<IEnumerable<string>?>()))
            .Callback<ValidationResult, IEnumerable<string>?>((_, artifactPaths) => publishedArtifactPaths = artifactPaths?.ToList());

        var command = new ValidateCommand(
            validatorService.Object,
            consoleOutput.Object,
            new Mock<IClientProfileEvaluator>(MockBehavior.Loose).Object,
            reportGenerator.Object,
            reportRenderer.Object,
            gitHubReporter.Object,
            NullLogger<ValidateCommand>.Instance,
            new Mock<INextStepAdvisor>(MockBehavior.Loose).Object,
            new Mock<ISessionArtifactStore>(MockBehavior.Loose).Object,
            sessionContext);

        await command.ExecuteAsync(
            server: "https://example.test/mcp",
            outputDirectory: new DirectoryInfo(outputRoot),
            specProfile: null,
            configFile: null,
            verbose: false,
            token: null,
            interactive: false,
            serverProfile: null,
            maxConcurrency: null,
            policyMode: ValidationPolicyModes.Advisory);

        var expectedArtifactPaths = Directory.GetFiles(outputRoot)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        publishedArtifactPaths.Should().NotBeNull();
        publishedArtifactPaths!
            .OrderBy(path => path, StringComparer.Ordinal)
            .Should()
            .Equal(expectedArtifactPaths);
    }

    [Fact]
    public async Task ExecuteAsync_WithClientProfiles_ShouldEvaluateCompatibilityAndPrintSummary()
    {
        var sessionContext = CreateSessionContext();
        var consoleOutput = new Mock<IConsoleOutputService>(MockBehavior.Loose);
        var validatorService = new Mock<IMcpValidatorService>();
        validatorService
            .Setup(service => service.ValidateServerAsync(It.IsAny<McpValidatorConfiguration>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult
            {
                OverallStatus = ValidationStatus.Passed,
                ServerConfig = new McpServerConfig { Endpoint = "https://example.test/mcp", Transport = "http" },
                ValidationConfig = new McpValidatorConfiguration { Reporting = new ReportingConfig() },
                ToolValidation = new ToolTestResult
                {
                    Status = TestStatus.Passed,
                    ToolsDiscovered = 1,
                    ToolResults = new List<IndividualToolResult>
                    {
                        new() { ToolName = "search_docs", Status = TestStatus.Passed }
                    }
                },
                TrustAssessment = new McpTrustAssessment { TrustLevel = McpTrustLevel.L4_Trusted }
            });

        ClientProfileOptions? capturedOptions = null;
        var clientProfileEvaluator = new Mock<IClientProfileEvaluator>();
        clientProfileEvaluator
            .Setup(evaluator => evaluator.Evaluate(It.IsAny<ValidationResult>(), It.IsAny<ClientProfileOptions?>()))
            .Callback<ValidationResult, ClientProfileOptions?>((_, options) => capturedOptions = options)
            .Returns(new ClientCompatibilityReport
            {
                RequestedProfiles = new List<string> { "claude-code" },
                Assessments = new List<ClientProfileAssessment>
                {
                    new()
                    {
                        ProfileId = "claude-code",
                        DisplayName = "Claude Code",
                        Status = ClientProfileCompatibilityStatus.Compatible,
                        Summary = "All applicable compatibility checks passed."
                    }
                }
            });

        var command = new ValidateCommand(
            validatorService.Object,
            consoleOutput.Object,
            clientProfileEvaluator.Object,
            new Mock<IReportGenerator>(MockBehavior.Loose).Object,
            new Mock<IValidationReportRenderer>(MockBehavior.Loose).Object,
            new Mock<IGitHubActionsReporter>(MockBehavior.Loose).Object,
            NullLogger<ValidateCommand>.Instance,
            new Mock<INextStepAdvisor>(MockBehavior.Loose).Object,
            new Mock<ISessionArtifactStore>(MockBehavior.Loose).Object,
            sessionContext);

        await command.ExecuteAsync(
            server: "https://example.test/mcp",
            outputDirectory: null,
            specProfile: null,
            configFile: null,
            verbose: false,
            token: null,
            interactive: false,
            serverProfile: null,
            maxConcurrency: null,
            policyMode: ValidationPolicyModes.Advisory,
            clientProfiles: new[] { "claude-code" });

        capturedOptions.Should().NotBeNull();
        capturedOptions!.Profiles.Should().ContainSingle().Which.Should().Be("claude-code");
        consoleOutput.Verify(output => output.WriteSuccess(It.Is<string>(value => value.Contains("Claude Code", StringComparison.Ordinal))), Times.AtLeastOnce());
    }

    private CliSessionContext CreateSessionContext()
    {
        var sessionId = Guid.NewGuid().ToString("N");
        var root = Path.Combine(Path.GetTempPath(), sessionId);
        var state = Path.Combine(root, "state");
        var logs = Path.Combine(root, "logs");
        Directory.CreateDirectory(state);
        Directory.CreateDirectory(logs);
        _sessionRoots.Add(root);
        return new CliSessionContext(sessionId, root, state, logs, Path.Combine(logs, "session.log"));
    }

    public void Dispose()
    {
        Environment.ExitCode = _originalExitCode;
        foreach (var tempFile in _tempFiles)
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }

        foreach (var root in _sessionRoots)
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}