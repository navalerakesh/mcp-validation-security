using System.Reflection;
using Mcp.Benchmark.CLI;
using Mcp.Benchmark.CLI.Utilities;
using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Infrastructure.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Mcp.Benchmark.Tests.Architecture;

public sealed class RunScopedCompositionTests
{
    [Fact]
    public async Task CliHost_ShouldIsolateMutableValidationGraphPerCommandScope()
    {
        var programType = typeof(ValidateCommand).Assembly.GetType("Program", throwOnError: true)!;
        var createHostBuilder = programType.GetMethod(
            "CreateHostBuilder",
            BindingFlags.NonPublic | BindingFlags.Static);
        createHostBuilder.Should().NotBeNull();

        var args = new[] { "validate", "--server", "https://1.1.1.1/mcp" };
        var hostBuilder = (IHostBuilder)createHostBuilder!.Invoke(null, [args, new CliSessionContext(Guid.NewGuid().ToString())])!;
        using var host = hostBuilder.Build();
        await using var firstScope = host.Services.CreateAsyncScope();
        await using var secondScope = host.Services.CreateAsyncScope();

        var firstClient = firstScope.ServiceProvider.GetRequiredService<IMcpHttpClient>();
        var repeatedFirstClient = firstScope.ServiceProvider.GetRequiredService<IMcpHttpClient>();
        var secondClient = secondScope.ServiceProvider.GetRequiredService<IMcpHttpClient>();
        var firstValidator = firstScope.ServiceProvider.GetRequiredService<IMcpValidatorService>();
        var secondValidator = secondScope.ServiceProvider.GetRequiredService<IMcpValidatorService>();

        repeatedFirstClient.Should().BeSameAs(firstClient);
        secondClient.Should().NotBeSameAs(firstClient);
        secondValidator.Should().NotBeSameAs(firstValidator);
    }

    [Fact]
    public async Task CliHost_ConfigOnlyStdio_ShouldResolveStdioTransport()
    {
        var configPath = Path.Combine(Path.GetTempPath(), $"mcpval-stdio-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(configPath, "{\"server\":{\"endpoint\":\"node fixture.cjs\",\"transport\":\"stdio\"}}");
        try
        {
            using var host = BuildHost(["validate", "--config", configPath]);
            await using var scope = host.Services.CreateAsyncScope();

            scope.ServiceProvider.GetRequiredService<IMcpHttpClient>().Should().BeOfType<StdioMcpClientAdapter>();
        }
        finally
        {
            File.Delete(configPath);
        }
    }

    [Fact]
    public async Task CliHost_ServerEqualsStdio_ShouldResolveStdioTransport()
    {
        using var host = BuildHost(["validate", "--server=node fixture.cjs"]);
        await using var scope = host.Services.CreateAsyncScope();

        scope.ServiceProvider.GetRequiredService<IMcpHttpClient>().Should().BeOfType<StdioMcpClientAdapter>();
    }

    private static IHost BuildHost(string[] args)
    {
        var programType = typeof(ValidateCommand).Assembly.GetType("Program", throwOnError: true)!;
        var createHostBuilder = programType.GetMethod("CreateHostBuilder", BindingFlags.NonPublic | BindingFlags.Static)!;
        var hostBuilder = (IHostBuilder)createHostBuilder.Invoke(
            null,
            [args, new CliSessionContext(Guid.NewGuid().ToString())])!;
        return hostBuilder.Build();
    }
}