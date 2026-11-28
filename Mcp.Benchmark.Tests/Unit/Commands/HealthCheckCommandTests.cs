using FluentAssertions;
using System.CommandLine;
using System.Reflection;
using Mcp.Benchmark.CLI;
using Mcp.Benchmark.CLI.Abstractions;
using Mcp.Benchmark.CLI.Models;
using Mcp.Benchmark.CLI.Services;
using Mcp.Benchmark.CLI.Utilities;
using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Services.SessionArtifacts;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Mcp.Benchmark.Tests.Unit.Commands;

[Collection("Console output")]
public sealed class HealthCheckCommandTests : IDisposable
{
    private readonly List<string> _sessionRoots = new();
    private readonly int _originalExitCode = Environment.ExitCode;

    [Fact]
    public async Task ExecuteAsync_PersistsArtifact_WhenHealthCheckCompletes()
    {
        // Arrange
        var sessionContext = CreateSessionContext();
        var consoleOutput = new Mock<IConsoleOutputService>(MockBehavior.Loose);
        var validatorService = new Mock<IMcpValidatorService>();
        validatorService
            .Setup(s => s.PerformHealthCheckAsync(It.IsAny<McpServerConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HealthCheckResult { IsHealthy = true });

        var artifactStore = new FileSessionArtifactStore(sessionContext.StateDirectory, NullLogger<FileSessionArtifactStore>.Instance);

        var command = new HealthCheckCommand(
            validatorService.Object,
            consoleOutput.Object,
            NullLogger<HealthCheckCommand>.Instance,
            new Mock<INextStepAdvisor>(MockBehavior.Loose).Object,
            new ExecutionGovernanceService(),
            artifactStore,
            new Mock<IMcpHttpClient>(MockBehavior.Loose).Object,
            sessionContext);

        // Act
        await command.ExecuteAsync(
            "https://1.1.1.1/mcp",
            2000,
            null,
            verbose: false,
            token: null,
            interactive: false,
            serverProfile: null,
            persistenceMode: "session");

        // Assert
        var artifacts = Directory.GetFiles(sessionContext.StateDirectory, "health-check-results-*.json");
        artifacts.Should().NotBeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_WritesSessionLogHint_WhenResultIsUnhealthy()
    {
        // Arrange
        var sessionContext = CreateSessionContext();
        var consoleOutput = new Mock<IConsoleOutputService>(MockBehavior.Loose);
        consoleOutput.Setup(c => c.WriteSessionLogHint(It.IsAny<string?>())).Verifiable();

        var validatorService = new Mock<IMcpValidatorService>();
        validatorService
            .Setup(s => s.PerformHealthCheckAsync(It.IsAny<McpServerConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HealthCheckResult { IsHealthy = false, ErrorMessage = "offline" });

        var artifactStore = new FileSessionArtifactStore(sessionContext.StateDirectory, NullLogger<FileSessionArtifactStore>.Instance);

        var command = new HealthCheckCommand(
            validatorService.Object,
            consoleOutput.Object,
            NullLogger<HealthCheckCommand>.Instance,
            new Mock<INextStepAdvisor>(MockBehavior.Loose).Object,
            new ExecutionGovernanceService(),
            artifactStore,
            new Mock<IMcpHttpClient>(MockBehavior.Loose).Object,
            sessionContext);

        // Act
        await command.ExecuteAsync("https://1.1.1.1/mcp", 2000, null, verbose: false, token: null, interactive: false, serverProfile: null);

        // Assert
        consoleOutput.Verify(c => c.WriteSessionLogHint(It.Is<string?>(s => s != null && s.Contains("Health-check", StringComparison.OrdinalIgnoreCase))), Times.AtLeastOnce());
    }

    [Fact]
    public async Task ExecuteAsync_WithDryRun_ShouldNotContactTarget()
    {
        var sessionContext = CreateSessionContext();
        var consoleOutput = new Mock<IConsoleOutputService>(MockBehavior.Loose);
        var validatorService = new Mock<IMcpValidatorService>(MockBehavior.Strict);

        var command = new HealthCheckCommand(
            validatorService.Object,
            consoleOutput.Object,
            NullLogger<HealthCheckCommand>.Instance,
            new Mock<INextStepAdvisor>(MockBehavior.Loose).Object,
            new ExecutionGovernanceService(),
            new Mock<ISessionArtifactStore>(MockBehavior.Loose).Object,
            new Mock<IMcpHttpClient>(MockBehavior.Loose).Object,
            sessionContext);

        await command.ExecuteAsync(
            "https://1.1.1.1/mcp",
            timeoutMs: 2000,
            configFile: null,
            verbose: false,
            token: null,
            interactive: false,
            serverProfile: null,
            dryRun: true);

        Environment.ExitCode.Should().Be(0);
        validatorService.Verify(service => service.PerformHealthCheckAsync(It.IsAny<McpServerConfig>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("health-check", "{}", null, 30, ConfigurationSourceKind.Default)]
    [InlineData("discover", "{}", null, 120, ConfigurationSourceKind.Default)]
    [InlineData("health-check", "{\"execution\":{\"timeoutSeconds\":45}}", null, 45, ConfigurationSourceKind.File)]
    [InlineData("discover", "{\"execution\":{\"timeoutSeconds\":45}}", null, 45, ConfigurationSourceKind.File)]
    [InlineData("health-check", "{\"server\":{\"timeoutMs\":2500}}", null, 3, ConfigurationSourceKind.File)]
    [InlineData("discover", "{\"server\":{\"timeoutMs\":2500}}", null, 3, ConfigurationSourceKind.File)]
    [InlineData("health-check", "{\"server\":{\"timeoutMs\":2500},\"execution\":{\"timeoutSeconds\":45}}", null, 45, ConfigurationSourceKind.File)]
    [InlineData("discover", "{\"server\":{\"timeoutMs\":2500},\"execution\":{\"timeoutSeconds\":45}}", null, 45, ConfigurationSourceKind.File)]
    [InlineData("health-check", "{\"execution\":{\"timeoutSeconds\":45}}", 5000, 5, ConfigurationSourceKind.CommandLine)]
    [InlineData("discover", "{\"execution\":{\"timeoutSeconds\":45}}", 5000, 5, ConfigurationSourceKind.CommandLine)]
    [InlineData("health-check", "{}", 0, 0, null)]
    [InlineData("discover", "{}", -1, 0, null)]
    [InlineData("health-check", "{\"server\":{\"timeoutMs\":0}}", null, 0, null)]
    [InlineData("discover", "{\"server\":{\"timeoutMs\":-1}}", null, 0, null)]
    public async Task Cli_TimeoutResolution_ShouldHonorExplicitValuesAndConfiguration(
        string commandName, string configurationJson, int? cliTimeout, int expectedSeconds, ConfigurationSourceKind? expectedSource)
    {
        var session = CreateSessionContext();
        Directory.CreateDirectory(session.StateDirectory);
        var configPath = Path.Combine(session.StateDirectory, "configuration.json");
        await File.WriteAllTextAsync(configPath, configurationJson);
        var realGovernance = new ExecutionGovernanceService();
        var governance = new Mock<IExecutionGovernanceService>();
        ExecutionPlan? captured = null;
        governance.Setup(service => service.BuildCommandPlan(It.IsAny<CliSessionContext>(), It.IsAny<string>(), It.IsAny<McpServerConfig>(),
                It.IsAny<ExecutionPolicy?>(), It.IsAny<string?>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<IReadOnlyList<string>>()))
            .Returns((CliSessionContext context, string commandName, McpServerConfig serverConfig, ExecutionPolicy? policy,
                string? output, IReadOnlyList<string> checks, IReadOnlyList<string> artifacts) =>
                captured = realGovernance.BuildCommandPlan(context, commandName, serverConfig, policy, output, checks, artifacts));
        var validator = new Mock<IMcpValidatorService>(MockBehavior.Strict);
        var transport = new Mock<IMcpHttpClient>(MockBehavior.Strict);
        var command = new HealthCheckCommand(validator.Object, new Mock<IConsoleOutputService>().Object,
            NullLogger<HealthCheckCommand>.Instance, new Mock<INextStepAdvisor>().Object,
            governance.Object, new Mock<ISessionArtifactStore>().Object, transport.Object, session);
        var discovery = new DiscoverCommand(validator.Object, new Mock<IConsoleOutputService>().Object,
            NullLogger<DiscoverCommand>.Instance, new Mock<INextStepAdvisor>().Object,
            governance.Object, new Mock<ISessionArtifactStore>().Object, transport.Object, session);
        await using var services = new ServiceCollection().AddScoped(_ => command).AddScoped(_ => discovery).BuildServiceProvider();
        var root = (RootCommand)typeof(Program).GetMethod("CreateRootCommand", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [services])!;

        var arguments = new List<string> { commandName, "--server", "https://1.1.1.1/mcp", "--config", configPath, "--dry-run" };
        if (cliTimeout.HasValue) arguments.AddRange(["--timeout", cliTimeout.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
        var parsed = root.Parse(arguments.ToArray());
        parsed.Errors.Should().BeEmpty();
        (await parsed.InvokeAsync()).Should().Be(expectedSource.HasValue ? 0 : 64);

        if (expectedSource.HasValue)
        {
            Environment.ExitCode.Should().Be(0);
            captured.Should().NotBeNull();
            captured!.TimeoutSeconds.Should().Be(expectedSeconds);
            captured.ConfigurationSources!.Single(origin => origin.Setting.EndsWith(".timeoutSeconds", StringComparison.OrdinalIgnoreCase))
                .Source.Should().Be(expectedSource.Value);
        }
        else
        {
            Environment.ExitCode.Should().Be(64);
            captured.Should().BeNull();
        }
        validator.VerifyNoOtherCalls();
        transport.VerifyNoOtherCalls();
        governance.Verify(service => service.ValidateTargetResolutionAsync(It.IsAny<ExecutionPlan>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_WithStdioTarget_ShouldExecuteOriginalCommandAndPersistRedactedTarget()
    {
        const string stdioCommand = "node /reviewed/server.cjs";
        var sessionContext = CreateSessionContext();
        var validatorService = new Mock<IMcpValidatorService>();
        validatorService
            .Setup(service => service.PerformHealthCheckAsync(It.IsAny<McpServerConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HealthCheckResult { IsHealthy = true });
        var httpClient = new Mock<IMcpHttpClient>(MockBehavior.Loose);
        var artifactStore = new FileSessionArtifactStore(sessionContext.StateDirectory, NullLogger<FileSessionArtifactStore>.Instance);
        var command = new HealthCheckCommand(
            validatorService.Object,
            new Mock<IConsoleOutputService>(MockBehavior.Loose).Object,
            NullLogger<HealthCheckCommand>.Instance,
            new Mock<INextStepAdvisor>(MockBehavior.Loose).Object,
            new ExecutionGovernanceService(),
            artifactStore,
            httpClient.Object,
            sessionContext);

        await command.ExecuteAsync(
            stdioCommand,
            2000,
            null,
            verbose: false,
            token: null,
            interactive: false,
            serverProfile: null,
            persistenceMode: "session");

        httpClient.Verify(client => client.StartSessionAsync(
            stdioCommand,
            It.IsAny<IReadOnlyDictionary<string, string>?>(),
            It.IsAny<CancellationToken>()), Times.Once);
        var artifact = Directory.GetFiles(sessionContext.StateDirectory, "health-check-results-*.json").Should().ContainSingle().Subject;
        (await File.ReadAllTextAsync(artifact)).Should().Contain("__STDIO_COMMAND_REDACTED__").And.NotContain(stdioCommand);
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
        foreach (var root in _sessionRoots)
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
