using FluentAssertions;
using Mcp.Benchmark.CLI;
using Mcp.Benchmark.CLI.Abstractions;
using Mcp.Benchmark.CLI.Services;
using Mcp.Benchmark.CLI.Utilities;
using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Services.SessionArtifacts;
using Microsoft.Extensions.Logging.Abstractions;

namespace Mcp.Benchmark.Tests.Unit.Commands;

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
            "https://mcp.example.org",
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
        await command.ExecuteAsync("https://mcp.example.org", 2000, null, verbose: false, token: null, interactive: false, serverProfile: null);

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
            "https://mcp.example.org",
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
