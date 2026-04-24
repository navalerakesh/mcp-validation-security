using FluentAssertions;
using Mcp.Benchmark.CLI;
using Mcp.Benchmark.CLI.Abstractions;
using Mcp.Benchmark.CLI.Services;
using Mcp.Benchmark.CLI.Utilities;
using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace Mcp.Benchmark.Tests.Unit.Commands;

public sealed class DiscoverCommandTests
{
    [Fact]
    public async Task ExecuteAsync_WithDryRun_ShouldNotContactTarget()
    {
        var sessionContext = new CliSessionContext(Guid.NewGuid().ToString("N"));
        var consoleOutput = new Mock<IConsoleOutputService>(MockBehavior.Loose);
        var validatorService = new Mock<IMcpValidatorService>(MockBehavior.Strict);

        var command = new DiscoverCommand(
            validatorService.Object,
            consoleOutput.Object,
            NullLogger<DiscoverCommand>.Instance,
            new Mock<INextStepAdvisor>(MockBehavior.Loose).Object,
            new ExecutionGovernanceService(),
            new Mock<ISessionArtifactStore>(MockBehavior.Loose).Object,
            new Mock<IMcpHttpClient>(MockBehavior.Loose).Object,
            sessionContext);

        await command.ExecuteAsync(
            server: "https://mcp.example.org",
            format: "json",
            timeoutMs: 2000,
            configFile: null,
            verbose: false,
            token: null,
            interactive: false,
            serverProfile: null,
            dryRun: true);

        Environment.ExitCode.Should().Be(0);
        validatorService.Verify(service => service.DiscoverServerCapabilitiesAsync(It.IsAny<McpServerConfig>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}