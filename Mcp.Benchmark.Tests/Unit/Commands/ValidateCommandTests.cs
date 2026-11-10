using FluentAssertions;
using System.Security.Cryptography;
using System.Text.Json;
using Mcp.Benchmark.CLI;
using Mcp.Benchmark.CLI.Abstractions;
using Mcp.Benchmark.CLI.Models;
using Mcp.Benchmark.ClientProfiles;
using Mcp.Benchmark.CLI.Services;
using Mcp.Benchmark.CLI.Utilities;
using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Constants;
using Mcp.Benchmark.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Mcp.Benchmark.Tests.Unit.Commands;

public sealed class ValidateCommandTests : IDisposable
{
    private readonly List<string> _sessionRoots = new();
    private readonly List<string> _tempFiles = new();
    private readonly int _originalExitCode = Environment.ExitCode;

    [Theory]
    [InlineData(null)]
    [InlineData("null")]
    [InlineData("{\"server\":null}")]
    [InlineData("{\"server\":{\"heders\":{\"X-Route\":\"route\"}}}")]
    [InlineData("{\"server\":{\"endpoint\":\"first\",\"Endpoint\":\"second\"}}")]
    public async Task ExecuteAsync_WithInvalidExplicitConfiguration_ShouldRejectBeforeValidation(string? json)
    {
        var configPath = Path.Combine(Path.GetTempPath(), $"invalid-config-{Guid.NewGuid():N}.json");
        _tempFiles.Add(configPath);
        if (json is not null) await File.WriteAllTextAsync(configPath, json);
        var consoleOutput = new Mock<IConsoleOutputService>();
        var validatorService = new Mock<IMcpValidatorService>();
        var command = new ValidateCommand(
            validatorService.Object,
            consoleOutput.Object,
            new Mock<IClientProfileEvaluator>().Object,
            new Mock<IReportGenerator>().Object,
            new Mock<IValidationReportRenderer>().Object,
            new Mock<IGitHubActionsReporter>().Object,
            NullLogger<ValidateCommand>.Instance,
            new Mock<INextStepAdvisor>().Object,
            new ExecutionGovernanceService(),
            new NoOpModelEvaluationExecutor(),
            new Mock<ISessionArtifactStore>().Object,
            new Mock<IMcpHttpClient>().Object,
            CreateSessionContext());

        await command.ExecuteAsync(
            server: "https://1.1.1.1/mcp",
            outputDirectory: null,
            specProfile: null,
            configFile: new FileInfo(configPath),
            verbose: false,
            token: null,
            interactive: false,
            serverProfile: null,
            maxConcurrency: null);

        Environment.ExitCode.Should().NotBe(0);
        validatorService.Invocations.Should().BeEmpty();
        var expectedDiagnostic = json is null ? "not found" : "Configuration";
        consoleOutput.Verify(output => output.WriteError(It.Is<string>(message =>
            message.Contains(expectedDiagnostic, StringComparison.OrdinalIgnoreCase))), Times.AtLeastOnce());
    }

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
                ServerConfig = new McpServerConfig { Endpoint = "https://1.1.1.1/mcp", Transport = "http" }
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
            new ExecutionGovernanceService(),
            new NoOpModelEvaluationExecutor(),
            new Mock<ISessionArtifactStore>(MockBehavior.Loose).Object,
            new Mock<IMcpHttpClient>(MockBehavior.Loose).Object,
            sessionContext);

        await command.ExecuteAsync(
            server: "https://1.1.1.1/mcp",
            outputDirectory: null,
            specProfile: null,
            configFile: null,
            verbose: false,
            token: null,
            interactive: false,
            serverProfile: null,
            maxConcurrency: null,
            policyMode: ValidationPolicyModes.Advisory);

        Environment.ExitCode.Should().Be(0, string.Join("; ", consoleOutput.Invocations
            .Where(invocation => invocation.Method.Name == nameof(IConsoleOutputService.WriteError))
            .SelectMany(invocation => invocation.Arguments)
            .Select(argument => argument?.ToString())));
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
                ServerConfig = new McpServerConfig { Endpoint = "https://1.1.1.1/mcp", Transport = "http" }
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
            new ExecutionGovernanceService(),
            new NoOpModelEvaluationExecutor(),
            new Mock<ISessionArtifactStore>(MockBehavior.Loose).Object,
            new Mock<IMcpHttpClient>(MockBehavior.Loose).Object,
            sessionContext);

        await command.ExecuteAsync(
            server: "https://1.1.1.1/mcp",
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
        public async Task ExecuteAsync_WithStrictPolicySuppressionFromConfig_ShouldNotOverrideAssuranceFailure()
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
                                ServerConfig = new McpServerConfig { Endpoint = "https://1.1.1.1/mcp", Transport = "http" }
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
                        new ExecutionGovernanceService(),
                        new NoOpModelEvaluationExecutor(),
                        new Mock<ISessionArtifactStore>(MockBehavior.Loose).Object,
                        new Mock<IMcpHttpClient>(MockBehavior.Loose).Object,
                        sessionContext);

                var configPath = Path.Combine(Path.GetTempPath(), $"validation-policy-{Guid.NewGuid():N}.json");
                _tempFiles.Add(configPath);
                await File.WriteAllTextAsync(
                        configPath,
                        """
                        {
                            "server": {
                                "endpoint": "https://1.1.1.1/mcp",
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

                Environment.ExitCode.Should().Be(1);
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
                ServerConfig = new McpServerConfig { Endpoint = "https://1.1.1.1/mcp", Transport = "http" },
                ValidationConfig = new McpValidatorConfiguration { Reporting = new ReportingConfig() }
            });

        var reportGenerator = new Mock<IReportGenerator>();
        reportGenerator.Setup(generator => generator.GenerateReport(It.IsAny<ValidationResult>())).Returns("# markdown");

        var reportRenderer = new Mock<IValidationReportRenderer>();
        reportRenderer.Setup(renderer => renderer.GenerateHtmlReport(It.IsAny<ValidationResult>(), It.IsAny<ReportingConfig>(), It.IsAny<bool>())).Returns("<html></html>");
        reportRenderer.Setup(renderer => renderer.GenerateSarifReport(It.IsAny<ValidationResult>())).Returns("{}");
        reportRenderer.Setup(renderer => renderer.GenerateJunitReport(It.IsAny<ValidationResult>())).Returns("<testsuites />");

        var command = new ValidateCommand(
            validatorService.Object,
            consoleOutput.Object,
            new Mock<IClientProfileEvaluator>(MockBehavior.Loose).Object,
            reportGenerator.Object,
            reportRenderer.Object,
            new Mock<IGitHubActionsReporter>(MockBehavior.Loose).Object,
            NullLogger<ValidateCommand>.Instance,
            new Mock<INextStepAdvisor>(MockBehavior.Loose).Object,
            new ExecutionGovernanceService(),
            new NoOpModelEvaluationExecutor(),
            new Mock<ISessionArtifactStore>(MockBehavior.Loose).Object,
            new Mock<IMcpHttpClient>(MockBehavior.Loose).Object,
            sessionContext);

        await command.ExecuteAsync(
            server: "https://1.1.1.1/mcp",
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
        Directory.GetFiles(outputRoot, "*-results.junit.xml").Should().ContainSingle();

        reportRenderer.Verify(renderer => renderer.GenerateHtmlReport(
            It.IsAny<ValidationResult>(),
            It.IsAny<ReportingConfig>(),
            true), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_WithoutExplicitMaxRequests_ShouldUseRaisedDefaultRequestBudget()
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
                ServerConfig = new McpServerConfig { Endpoint = "https://1.1.1.1/mcp", Transport = "http" },
                ValidationConfig = new McpValidatorConfiguration { Reporting = new ReportingConfig() }
            });

        ExecutionPolicy? capturedExecutionPolicy = null;
        var httpClient = new Mock<IMcpHttpClient>(MockBehavior.Loose);
        httpClient
            .Setup(client => client.ConfigureExecutionPolicy(It.IsAny<ExecutionPolicy?>()))
            .Callback<ExecutionPolicy?>(policy => capturedExecutionPolicy = policy?.Clone());

        var command = new ValidateCommand(
            validatorService.Object,
            consoleOutput.Object,
            new Mock<IClientProfileEvaluator>(MockBehavior.Loose).Object,
            new Mock<IReportGenerator>(MockBehavior.Loose).Object,
            new Mock<IValidationReportRenderer>(MockBehavior.Loose).Object,
            new Mock<IGitHubActionsReporter>(MockBehavior.Loose).Object,
            NullLogger<ValidateCommand>.Instance,
            new Mock<INextStepAdvisor>(MockBehavior.Loose).Object,
            new ExecutionGovernanceService(),
            new NoOpModelEvaluationExecutor(),
            new Mock<ISessionArtifactStore>(MockBehavior.Loose).Object,
            httpClient.Object,
            sessionContext);

        await command.ExecuteAsync(
            server: "https://1.1.1.1/mcp",
            outputDirectory: null,
            specProfile: null,
            configFile: null,
            verbose: false,
            token: null,
            interactive: false,
            serverProfile: null,
            maxConcurrency: null,
            policyMode: ValidationPolicyModes.Advisory);

        capturedExecutionPolicy.Should().NotBeNull();
        capturedExecutionPolicy!.MaxRequests.Should().Be(256);
        capturedConfiguration.Should().NotBeNull();
        var categories = capturedConfiguration!.Validation.Categories;
        categories.ToolTesting.TestToolExecution.Should().BeFalse();
        categories.ToolTesting.TestParameterValidation.Should().BeFalse();
        categories.ResourceTesting.TestResourceReading.Should().BeFalse();
        categories.PromptTesting.TestPromptExecution.Should().BeFalse();
        categories.SecurityTesting.TestInputValidation.Should().BeFalse();
        categories.PerformanceTesting.TestConcurrentRequests.Should().BeFalse();
        categories.ErrorHandling.TestMalformedJson.Should().BeFalse();
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
                ServerConfig = new McpServerConfig { Endpoint = "https://1.1.1.1/mcp", Transport = "http" },
                ValidationConfig = new McpValidatorConfiguration { Reporting = new ReportingConfig() },
                PerformanceTesting = new PerformanceTestResult
                {
                    Status = TestStatus.Failed,
                    Message = "Operation timed out or was cancelled",
                    CriticalErrors = new List<string> { "Operation timed out or was cancelled" }
                }
            });

        var reportGenerator = new Mock<IReportGenerator>();
        reportGenerator.Setup(generator => generator.GenerateReport(It.IsAny<ValidationResult>())).Returns("# markdown");

        var reportRenderer = new Mock<IValidationReportRenderer>();
        reportRenderer.Setup(renderer => renderer.GenerateHtmlReport(It.IsAny<ValidationResult>(), It.IsAny<ReportingConfig>(), It.IsAny<bool>())).Returns("<html></html>");
        reportRenderer.Setup(renderer => renderer.GenerateSarifReport(It.IsAny<ValidationResult>())).Returns("{}");
        reportRenderer.Setup(renderer => renderer.GenerateJunitReport(It.IsAny<ValidationResult>())).Returns("<testsuites />");

        var command = new ValidateCommand(
            validatorService.Object,
            consoleOutput.Object,
            new Mock<IClientProfileEvaluator>(MockBehavior.Loose).Object,
            reportGenerator.Object,
            reportRenderer.Object,
            new Mock<IGitHubActionsReporter>(MockBehavior.Loose).Object,
            NullLogger<ValidateCommand>.Instance,
            new Mock<INextStepAdvisor>(MockBehavior.Loose).Object,
            new ExecutionGovernanceService(),
            new NoOpModelEvaluationExecutor(),
            new Mock<ISessionArtifactStore>(MockBehavior.Loose).Object,
            new Mock<IMcpHttpClient>(MockBehavior.Loose).Object,
            sessionContext);

        await command.ExecuteAsync(
            server: "https://1.1.1.1/mcp",
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
        var performance = document.RootElement
            .GetProperty("measurements")
            .GetProperty("performanceTesting");

        performance.GetProperty("measurementsCaptured").GetBoolean().Should().BeFalse();
        performance.GetProperty("measurementStatus").GetString().Should().Be("Unavailable");
        performance.GetProperty("measurementReason").GetString().Should().Be("Operation timed out or was cancelled");
    }

    [Fact]
    public async Task ExecuteAsync_WithFailedProbeAndZeroMetrics_ShouldAnnotateSavedJsonAsUnavailable()
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
                ServerConfig = new McpServerConfig { Endpoint = "https://1.1.1.1/mcp", Transport = "http" },
                ValidationConfig = new McpValidatorConfiguration { Reporting = new ReportingConfig() },
                PerformanceTesting = new PerformanceTestResult
                {
                    Status = TestStatus.Failed,
                    LoadTesting = new LoadTestResult
                    {
                        TotalRequests = 20,
                        SuccessfulRequests = 0,
                        FailedRequests = 20,
                        AverageResponseTimeMs = 0,
                        P95ResponseTimeMs = 0,
                        RequestsPerSecond = 0

                    }
                }
            });

        var reportGenerator = new Mock<IReportGenerator>();
        reportGenerator.Setup(generator => generator.GenerateReport(It.IsAny<ValidationResult>())).Returns("# markdown");

        var reportRenderer = new Mock<IValidationReportRenderer>();
        reportRenderer.Setup(renderer => renderer.GenerateHtmlReport(It.IsAny<ValidationResult>(), It.IsAny<ReportingConfig>(), It.IsAny<bool>())).Returns("<html></html>");
        reportRenderer.Setup(renderer => renderer.GenerateSarifReport(It.IsAny<ValidationResult>())).Returns("{}");
        reportRenderer.Setup(renderer => renderer.GenerateJunitReport(It.IsAny<ValidationResult>())).Returns("<testsuites />");

        var command = new ValidateCommand(
            validatorService.Object,
            consoleOutput.Object,
            new Mock<IClientProfileEvaluator>(MockBehavior.Loose).Object,
            reportGenerator.Object,
            reportRenderer.Object,
            new Mock<IGitHubActionsReporter>(MockBehavior.Loose).Object,
            NullLogger<ValidateCommand>.Instance,
            new Mock<INextStepAdvisor>(MockBehavior.Loose).Object,
            new ExecutionGovernanceService(),
            new NoOpModelEvaluationExecutor(),
            new Mock<ISessionArtifactStore>(MockBehavior.Loose).Object,
            new Mock<IMcpHttpClient>(MockBehavior.Loose).Object,
            sessionContext);

        await command.ExecuteAsync(
            server: "https://1.1.1.1/mcp",
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
        var performance = document.RootElement
            .GetProperty("measurements")
            .GetProperty("performanceTesting");

        performance.GetProperty("measurementsCaptured").GetBoolean().Should().BeFalse();
        performance.GetProperty("measurementStatus").GetString().Should().Be("Unavailable");
        performance.GetProperty("measurementReason").GetString().Should().Be("Performance probe attempted 20 request(s) but captured no timing samples because every request failed.");
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
                ServerConfig = new McpServerConfig { Endpoint = "https://1.1.1.1/mcp", Transport = "http" },
                ValidationConfig = new McpValidatorConfiguration { Reporting = new ReportingConfig() }
            });

        var reportGenerator = new Mock<IReportGenerator>();
        reportGenerator.Setup(generator => generator.GenerateReport(It.IsAny<ValidationResult>())).Returns("# markdown");

        var reportRenderer = new Mock<IValidationReportRenderer>();
        reportRenderer.Setup(renderer => renderer.GenerateHtmlReport(It.IsAny<ValidationResult>(), It.IsAny<ReportingConfig>(), It.IsAny<bool>())).Returns("<html></html>");
        reportRenderer.Setup(renderer => renderer.GenerateSarifReport(It.IsAny<ValidationResult>())).Returns("{}");
        reportRenderer.Setup(renderer => renderer.GenerateJunitReport(It.IsAny<ValidationResult>())).Returns("<testsuites />");

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
            new ExecutionGovernanceService(),
            new NoOpModelEvaluationExecutor(),
            new Mock<ISessionArtifactStore>(MockBehavior.Loose).Object,
            new Mock<IMcpHttpClient>(MockBehavior.Loose).Object,
            sessionContext);

        await command.ExecuteAsync(
            server: "https://1.1.1.1/mcp",
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
                ServerConfig = new McpServerConfig { Endpoint = "https://1.1.1.1/mcp", Transport = "http" },
                ValidationConfig = new McpValidatorConfiguration { Reporting = new ReportingConfig() },
                ToolValidation = new ToolTestResult
                {
                    Status = TestStatus.Passed,
                    ToolsDiscovered = 1,
                    ToolResults = new List<IndividualToolResult>
                    {
                        new() { ToolName = "search_docs", Status = TestStatus.Passed }
                    }
                }
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
            new ExecutionGovernanceService(),
            new NoOpModelEvaluationExecutor(),
            new Mock<ISessionArtifactStore>(MockBehavior.Loose).Object,
            new Mock<IMcpHttpClient>(MockBehavior.Loose).Object,
            sessionContext);

        await command.ExecuteAsync(
            server: "https://1.1.1.1/mcp",
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

    [Fact]
    public async Task ExecuteAsync_WithDryRun_ShouldNotContactTarget()
    {
        var sessionContext = CreateSessionContext();
        var consoleOutput = new Mock<IConsoleOutputService>(MockBehavior.Loose);
        var validatorService = new Mock<IMcpValidatorService>(MockBehavior.Strict);

        var command = new ValidateCommand(
            validatorService.Object,
            consoleOutput.Object,
            new Mock<IClientProfileEvaluator>(MockBehavior.Loose).Object,
            new Mock<IReportGenerator>(MockBehavior.Loose).Object,
            new Mock<IValidationReportRenderer>(MockBehavior.Loose).Object,
            new Mock<IGitHubActionsReporter>(MockBehavior.Loose).Object,
            NullLogger<ValidateCommand>.Instance,
            new Mock<INextStepAdvisor>(MockBehavior.Loose).Object,
            new ExecutionGovernanceService(),
            new NoOpModelEvaluationExecutor(),
            new Mock<ISessionArtifactStore>(MockBehavior.Loose).Object,
            new Mock<IMcpHttpClient>(MockBehavior.Loose).Object,
            sessionContext);

        await command.ExecuteAsync(
            server: "https://1.1.1.1/mcp",
            outputDirectory: null,
            specProfile: null,
            configFile: null,
            verbose: false,
            token: null,
            interactive: false,
            serverProfile: null,
            maxConcurrency: null,
            policyMode: ValidationPolicyModes.Advisory,
            executionMode: null,
            dryRun: true);

        Environment.ExitCode.Should().Be(0);
        validatorService.Verify(service => service.ValidateServerAsync(It.IsAny<McpValidatorConfiguration>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_ProviderDryRun_ShouldEmitPlanWithoutTrustInputsOrTargetContact()
    {
        var validatorService = new Mock<IMcpValidatorService>(MockBehavior.Strict);
        var command = new ValidateCommand(
            validatorService.Object,
            new Mock<IConsoleOutputService>(MockBehavior.Loose).Object,
            new Mock<IClientProfileEvaluator>(MockBehavior.Loose).Object,
            new Mock<IReportGenerator>(MockBehavior.Loose).Object,
            new Mock<IValidationReportRenderer>(MockBehavior.Loose).Object,
            new Mock<IGitHubActionsReporter>(MockBehavior.Loose).Object,
            NullLogger<ValidateCommand>.Instance,
            new Mock<INextStepAdvisor>(MockBehavior.Loose).Object,
            new ExecutionGovernanceService(),
            new NoOpModelEvaluationExecutor(),
            new Mock<ISessionArtifactStore>(MockBehavior.Loose).Object,
            new Mock<IMcpHttpClient>(MockBehavior.Loose).Object,
            CreateSessionContext());

        await command.ExecuteAsync(
            server: "https://1.1.1.1/mcp",
            outputDirectory: null,
            specProfile: null,
            configFile: null,
            verbose: false,
            assuranceProfile: "provider",
            dryRun: true);

        Environment.ExitCode.Should().Be(0);
        validatorService.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ExecuteAsync_WithModelEvaluationEnabled_ShouldEmitSeparateCompanionArtifact()
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
                ServerConfig = new McpServerConfig { Endpoint = "https://1.1.1.1/mcp", Transport = "http" },
                ValidationConfig = new McpValidatorConfiguration { Reporting = new ReportingConfig() },
                ToolValidation = new ToolTestResult
                {
                    AiReadinessFindings =
                    [
                        new()
                        {
                            RuleId = ValidationFindingRuleIds.AiReadinessVagueStringSchema,
                            Category = "AiReadiness",
                            Component = "search_docs",
                            Severity = ValidationFindingSeverity.Medium,
                            Summary = "Tool 'search_docs' exposes a vague freeform parameter.",
                            Metadata =
                            {
                                [AiReadinessEvidenceKinds.MetadataKey] = AiReadinessEvidenceKinds.DeterministicSchemaHeuristic
                            }
                        }
                    ]
                }
            });

        var reportGenerator = new Mock<IReportGenerator>();
        reportGenerator.Setup(generator => generator.GenerateReport(It.IsAny<ValidationResult>())).Returns("# markdown");

        var reportRenderer = new Mock<IValidationReportRenderer>();
        reportRenderer.Setup(renderer => renderer.GenerateHtmlReport(It.IsAny<ValidationResult>(), It.IsAny<ReportingConfig>(), It.IsAny<bool>())).Returns("<html></html>");
        reportRenderer.Setup(renderer => renderer.GenerateSarifReport(It.IsAny<ValidationResult>())).Returns("{}");
        reportRenderer.Setup(renderer => renderer.GenerateJunitReport(It.IsAny<ValidationResult>())).Returns("<testsuites />");

                var configPath = Path.Combine(Path.GetTempPath(), $"model-eval-{Guid.NewGuid():N}.json");
                _tempFiles.Add(configPath);
                await File.WriteAllTextAsync(
                        configPath,
                        """
                        {
                            "evaluation": {
                                "modelEvaluation": {
                                    "enabled": true,
                                    "provider": "builtin-rubric",
                                    "model": "builtin-rubric-v1",
                                    "promptSet": "baseline-v1"
                                }
                            }
                        }
                        """);

        var command = new ValidateCommand(
            validatorService.Object,
            consoleOutput.Object,
            new Mock<IClientProfileEvaluator>(MockBehavior.Loose).Object,
            reportGenerator.Object,
            reportRenderer.Object,
            new Mock<IGitHubActionsReporter>(MockBehavior.Loose).Object,
            NullLogger<ValidateCommand>.Instance,
            new Mock<INextStepAdvisor>(MockBehavior.Loose).Object,
            new ExecutionGovernanceService(),
            new NoOpModelEvaluationExecutor(),
            new Mock<ISessionArtifactStore>(MockBehavior.Loose).Object,
            new Mock<IMcpHttpClient>(MockBehavior.Loose).Object,
            sessionContext);

        await command.ExecuteAsync(
            server: "https://1.1.1.1/mcp",
            outputDirectory: new DirectoryInfo(outputRoot),
            specProfile: null,
            configFile: new FileInfo(configPath),
            verbose: false,
            token: null,
            interactive: false,
            serverProfile: null,
            maxConcurrency: null,
            policyMode: ValidationPolicyModes.Advisory,
            enableModelEval: true);

        Directory.GetFiles(outputRoot, "*-model-evaluation.json").Should().ContainSingle();
        var modelEvaluationPath = Directory.GetFiles(outputRoot, "*-model-evaluation.json").Single();
        var modelEvaluationJson = await File.ReadAllTextAsync(modelEvaluationPath);
        using var modelEvaluationDocument = JsonDocument.Parse(modelEvaluationJson);
        modelEvaluationDocument.RootElement.GetProperty("documentType").GetString().Should().Be(ArtifactContracts.ModelEvaluationDocumentType);
        modelEvaluationDocument.RootElement.GetProperty("documentSchemaVersion").GetString().Should().Be(ArtifactContracts.ModelEvaluationSchemaVersion);
        var cost = modelEvaluationDocument.RootElement.GetProperty("cost");
        cost.GetProperty("inputTokens").GetInt64().Should().Be(0);
        cost.GetProperty("outputTokens").GetInt64().Should().Be(0);
        cost.GetProperty("amount").GetDecimal().Should().Be(0);
        modelEvaluationJson.Should().Contain("\"status\": \"completed\"");
        modelEvaluationJson.Should().Contain("\"relatedDeterministicFindings\"");
        modelEvaluationJson.Should().Contain(ValidationFindingRuleIds.AiReadinessVagueStringSchema);
        modelEvaluationJson.Should().Contain(AiReadinessEvidenceKinds.DeterministicSchemaHeuristic);
        var resultJsonPath = Directory.GetFiles(outputRoot, "*-result.json").Should().ContainSingle().Subject;
        var resultJson = await File.ReadAllTextAsync(resultJsonPath);
        resultJson.Should().NotContain("modelEvaluation");
    }

    [Fact]
    public async Task ExecuteAsync_WithModelEvaluationEnabledAndNoProvider_ShouldFailFast()
    {
        var sessionContext = CreateSessionContext();
        var outputRoot = Path.Combine(Path.GetTempPath(), $"validate-output-{Guid.NewGuid():N}");
        _sessionRoots.Add(outputRoot);
        var consoleOutput = new Mock<IConsoleOutputService>(MockBehavior.Loose);
        var validatorService = new Mock<IMcpValidatorService>();
        validatorService
            .Setup(service => service.ValidateServerAsync(It.IsAny<McpValidatorConfiguration>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult
            {
                OverallStatus = ValidationStatus.Passed,
                ServerConfig = new McpServerConfig { Endpoint = "https://1.1.1.1/mcp", Transport = "http" },
                ValidationConfig = new McpValidatorConfiguration { Reporting = new ReportingConfig() }
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
            new ExecutionGovernanceService(),
            new NoOpModelEvaluationExecutor(),
            new Mock<ISessionArtifactStore>(MockBehavior.Loose).Object,
            new Mock<IMcpHttpClient>(MockBehavior.Loose).Object,
            sessionContext);

        await command.ExecuteAsync(
            server: "https://1.1.1.1/mcp",
            outputDirectory: new DirectoryInfo(outputRoot),
            specProfile: null,
            configFile: null,
            verbose: false,
            token: null,
            interactive: false,
            serverProfile: null,
            maxConcurrency: null,
            policyMode: ValidationPolicyModes.Advisory,
            enableModelEval: true);

        Environment.ExitCode.Should().Be(64);
        consoleOutput.Verify(output => output.WriteError(It.Is<string>(message => message.Contains("no provider was configured", StringComparison.OrdinalIgnoreCase))), Times.AtLeastOnce());
    }

    [Fact]
    public async Task ExecuteAsync_WithSignedAttestation_ShouldVerifyCanonicalBytesWithoutPersistingPrivateKey()
    {
        var sessionContext = CreateSessionContext();
        var outputRoot = Path.Combine(Path.GetTempPath(), $"validate-output-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outputRoot);
        _sessionRoots.Add(outputRoot);
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var privateKeyPem = key.ExportECPrivateKeyPem();
        var previousKey = Environment.GetEnvironmentVariable("MCPVAL_ATTESTATION_PRIVATE_KEY_PEM");
        Environment.SetEnvironmentVariable("MCPVAL_ATTESTATION_PRIVATE_KEY_PEM", privateKeyPem);

        try
        {
            var validatorService = new Mock<IMcpValidatorService>();
            validatorService
                .Setup(service => service.ValidateServerAsync(It.IsAny<McpValidatorConfiguration>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ValidationResult
                {
                    ValidationId = "signed-validation",
                    OverallStatus = ValidationStatus.Passed,
                    ComplianceScore = 100,
                    ServerConfig = new McpServerConfig { Endpoint = "https://1.1.1.1/mcp", Transport = "http" },
                    ValidationConfig = new McpValidatorConfiguration(),
                    Run = new ValidationRunDocument
                    {
                        ValidationId = "signed-validation",
                        OperationalMetrics = new ValidationOperationalMetrics { RunCorrelationId = "service-run" }
                    }
                });
            var reportGenerator = new Mock<IReportGenerator>();
            reportGenerator.Setup(generator => generator.GenerateReport(It.IsAny<ValidationResult>())).Returns("report");
            var renderer = new Mock<IValidationReportRenderer>();
            renderer.Setup(value => value.GenerateHtmlReport(It.IsAny<ValidationResult>(), It.IsAny<ReportingConfig>(), It.IsAny<bool>())).Returns("<html></html>");
            renderer.Setup(value => value.GenerateSarifReport(It.IsAny<ValidationResult>())).Returns("{}");
            renderer.Setup(value => value.GenerateJunitReport(It.IsAny<ValidationResult>())).Returns("<testsuites />");
            var command = new ValidateCommand(
                validatorService.Object,
                new Mock<IConsoleOutputService>(MockBehavior.Loose).Object,
                new Mock<IClientProfileEvaluator>(MockBehavior.Loose).Object,
                reportGenerator.Object,
                renderer.Object,
                new Mock<IGitHubActionsReporter>(MockBehavior.Loose).Object,
                NullLogger<ValidateCommand>.Instance,
                new Mock<INextStepAdvisor>(MockBehavior.Loose).Object,
                new ExecutionGovernanceService(),
                new NoOpModelEvaluationExecutor(),
                new Mock<ISessionArtifactStore>(MockBehavior.Loose).Object,
                new Mock<IMcpHttpClient>(MockBehavior.Loose).Object,
                sessionContext);

            await command.ExecuteAsync(
                server: "https://1.1.1.1/mcp",
                outputDirectory: new DirectoryInfo(outputRoot),
                specProfile: null,
                configFile: null,
                verbose: false,
                policyMode: ValidationPolicyModes.Advisory,
                signAttestation: true);

            var resultPath = Directory.GetFiles(outputRoot, "*-result.json").Should().ContainSingle().Subject;
            var attestationPath = Directory.GetFiles(outputRoot, "*-attestation.json").Should().ContainSingle().Subject;
            var attestation = JsonSerializer.Deserialize<ValidationAttestation>(
                await File.ReadAllTextAsync(attestationPath),
                ArtifactJsonOptions.Create(writeIndented: false, propertyNameCaseInsensitive: true));

            attestation.Should().NotBeNull();
            ValidationAttestationSigner.Verify(attestation!, await File.ReadAllBytesAsync(resultPath)).Should().BeTrue();
            using var resultDocument = JsonDocument.Parse(await File.ReadAllTextAsync(resultPath));
            var run = resultDocument.RootElement.GetProperty("run");
            run.GetProperty("operationalMetrics").GetProperty("runCorrelationId").GetString()
                .Should().Be(run.GetProperty("validationId").GetString());
            foreach (var artifactPath in Directory.GetFiles(outputRoot))
            {
                (await File.ReadAllTextAsync(artifactPath)).Should().NotContain(privateKeyPem);
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("MCPVAL_ATTESTATION_PRIVATE_KEY_PEM", previousKey);
        }
    }

    [Fact]
    public async Task ExecuteAsync_ProviderWithoutTrustInputs_ShouldFailBeforeTargetContact()
    {
        var validatorService = new Mock<IMcpValidatorService>(MockBehavior.Strict);
        var console = new Mock<IConsoleOutputService>(MockBehavior.Loose);
        var command = new ValidateCommand(
            validatorService.Object,
            console.Object,
            new Mock<IClientProfileEvaluator>(MockBehavior.Loose).Object,
            new Mock<IReportGenerator>(MockBehavior.Loose).Object,
            new Mock<IValidationReportRenderer>(MockBehavior.Loose).Object,
            new Mock<IGitHubActionsReporter>(MockBehavior.Loose).Object,
            NullLogger<ValidateCommand>.Instance,
            new Mock<INextStepAdvisor>(MockBehavior.Loose).Object,
            new ExecutionGovernanceService(),
            new NoOpModelEvaluationExecutor(),
            new Mock<ISessionArtifactStore>(MockBehavior.Loose).Object,
            new Mock<IMcpHttpClient>(MockBehavior.Loose).Object,
            CreateSessionContext());

        await command.ExecuteAsync(
            server: "https://1.1.1.1/mcp",
            outputDirectory: null,
            specProfile: null,
            configFile: null,
            verbose: false,
            assuranceProfile: "provider");

        Environment.ExitCode.Should().Be(64);
        validatorService.VerifyNoOtherCalls();
        console.Verify(output => output.WriteError(It.Is<string>(message => message.Contains("requires --sign-attestation", StringComparison.Ordinal))), Times.Once());
    }

    [Fact]
    public void ComputeProviderExecutionPlanDigest_ShouldIgnoreSessionIdentityAndBindAuthority()
    {
        var first = CreateProviderPlan("session-a", ExecutionMode.Standard, 20);
        var sameAuthority = CreateProviderPlan("session-b", ExecutionMode.Standard, 20);
        var changedAuthority = CreateProviderPlan("session-a", ExecutionMode.Elevated, 21);

        ValidateCommand.ComputeProviderExecutionPlanDigest(first)
            .Should().Be(ValidateCommand.ComputeProviderExecutionPlanDigest(sameAuthority));
        ValidateCommand.ComputeProviderExecutionPlanDigest(first)
            .Should().NotBe(ValidateCommand.ComputeProviderExecutionPlanDigest(changedAuthority));
    }

    [Fact]
    public void BuildCurrentProviderRunReceipt_ShouldBindIndependentAssessmentsAndClaims()
    {
        var result = new ValidationResult
        {
            ValidationId = "current-run",
            StartTime = new DateTime(2026, 11, 5, 11, 59, 0, DateTimeKind.Utc),
            Assurance = new AssuranceEvaluation
            {
                Profile = AssuranceProfileCatalog.Provider,
                Claims =
                [
                    new AssuranceClaim
                    {
                        ClaimId = "claim:review",
                        Subject = "tool",
                        Dimension = AssuranceDimension.Security,
                        RequirementLevel = AssuranceRequirementLevel.SecurityControl,
                        Authority = AssuranceAuthority.Heuristic,
                        Source = new AssuranceSourceReference { Revision = "test" },
                        Outcome = AssuranceClaimOutcome.ReviewRequired,
                        Summary = "Review."
                    }
                ],
                Assessments = new AssuranceAssessmentSet
                {
                    CoreConformance = new CoreConformanceAssessment { Status = CoreConformanceStatus.Conformant },
                    SecurityAssurance = new SecurityAssuranceAssessment { Status = SecurityAssuranceStatus.Verified },
                    OperationalReadiness = new OperationalReadinessAssessment { Status = OperationalReadinessStatus.Ready }
                },
                CertificateEligibility = new CertificateEligibilityAssessment
                {
                    ProfileId = "provider",
                    Status = CertificateEligibilityStatus.ReviewRequired
                }
            }
        };

        var receipt = ValidateCommand.BuildCurrentProviderRunReceipt(
            result,
            new string('e', 64),
            new DateTimeOffset(2026, 11, 5, 12, 0, 0, TimeSpan.Zero));

        receipt.RunId.Should().Be("current-run");
        receipt.ResultSha256.Should().MatchRegex("^[a-f0-9]{64}$");
        receipt.NonSuccessClaimIds.Should().Equal("claim:review");
        receipt.CoreConformance.Should().Be(CoreConformanceStatus.Conformant);
    }

    private static ExecutionPlan CreateProviderPlan(string sessionId, ExecutionMode mode, int maxRequests) => new()
    {
        SessionId = sessionId,
        TargetDigest = new string('a', 64),
        Transport = "http",
        ResolvedSchemaVersion = "2026-07-28",
        ProtocolEra = McpProtocolEra.Modern,
        ExecutionMode = mode,
        MaxRequests = maxRequests,
        MaxConcurrency = 2,
        TimeoutSeconds = 30,
        MaxResponseBytes = 1024,
        AllowedHosts = ["example.test"],
        AllowedOrigins = ["https://example.test"],
        PlannedChecks = ["security", "protocol"]
    };

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