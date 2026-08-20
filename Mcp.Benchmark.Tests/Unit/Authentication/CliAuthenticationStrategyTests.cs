using Mcp.Benchmark.Infrastructure.Authentication.Strategies;
using Mcp.Benchmark.Core.Constants;
using Microsoft.Extensions.Logging;

namespace Mcp.Benchmark.Tests.Unit.Authentication;

public sealed class CliAuthenticationStrategyTests
{
    [Fact]
    public async Task RunCliCommandAsync_PreservesShellMetacharactersAsOneArgument()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var strategy = new TestCliStrategy(Mock.Of<ILogger>());
        const string untrustedArgument = "tenant & printf injected";

        var output = await strategy.RunAsync("/usr/bin/printf", ["%s", untrustedArgument], CancellationToken.None);

        output.Should().Be(untrustedArgument);
    }

    [Fact]
    public async Task RunCliCommandAsync_WhenCancelled_PropagatesCancellation()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var strategy = new TestCliStrategy(Mock.Of<ILogger>());
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        var action = () => strategy.RunAsync("/bin/sleep", ["10"], cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task RunCliCommandAsync_CompatibilityStringOverload_ShouldRemainShellFree()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var strategy = new TestCliStrategy(Mock.Of<ILogger>());

        var output = await strategy.RunStringAsync(
            "/usr/bin/printf",
            "'%s' 'tenant & printf injected'",
            CancellationToken.None);

        output.Should().Be("tenant & printf injected");
    }

    [Fact]
    public async Task RunCliCommandAsync_OutputExceedsLimit_ShouldTerminateAndReturnNoCredential()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var strategy = new TestCliStrategy(Mock.Of<ILogger>());

        var output = await strategy
            .RunAsync("/usr/bin/yes", Array.Empty<string>(), CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(3));

        output.Should().BeNull();
        ExecutionPolicyDefaults.DefaultMaxSubprocessOutputBytes.Should().BeGreaterThan(0);
    }

    private sealed class TestCliStrategy(ILogger logger) : BaseCliStrategy(logger)
    {
        public Task<string?> RunAsync(
            string executable,
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken) =>
            RunCliCommandAsync(executable, arguments, cancellationToken);

#pragma warning disable CS0618
        public Task<string?> RunStringAsync(
            string executable,
            string arguments,
            CancellationToken cancellationToken) =>
            RunCliCommandAsync(executable, arguments, cancellationToken);
#pragma warning restore CS0618
    }
}