using Microsoft.Extensions.DependencyInjection;

namespace Mcp.Benchmark.Tests.Unit.Cli;

public sealed class ProgramOpenTelemetryTests
{
    [Fact]
    public void ConfigureOpenTelemetry_WithoutEndpoint_ShouldRemainDisabled()
    {
        var services = new ServiceCollection();

        Program.ConfigureOpenTelemetry(services, null);

        services.Should().BeEmpty();
    }

    [Fact]
    public void ConfigureOpenTelemetry_WithValidEndpoint_ShouldRegisterSdk()
    {
        var services = new ServiceCollection();

        Program.ConfigureOpenTelemetry(services, "http://localhost:4317");

        services.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData("not-a-uri")]
    [InlineData("file:///tmp/otel")]
    public void ConfigureOpenTelemetry_WithUnsafeEndpoint_ShouldReject(string endpoint)
    {
        var services = new ServiceCollection();

        var action = () => Program.ConfigureOpenTelemetry(services, endpoint);

        action.Should().Throw<InvalidOperationException>().WithMessage("*HTTP(S)*");
    }
}
