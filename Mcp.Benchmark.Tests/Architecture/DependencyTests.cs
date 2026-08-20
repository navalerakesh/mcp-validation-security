using NetArchTest.Rules;
using Xunit;
using Mcp.Benchmark.ClientProfiles;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Fleet.Jobs;

namespace Mcp.Benchmark.Tests.Architecture;

public class DependencyTests
{
    private const string CoreNamespace = "Mcp.Benchmark.Core";
    private const string InfrastructureNamespace = "Mcp.Benchmark.Infrastructure";
    private const string CliNamespace = "Mcp.Benchmark.CLI";
    private const string ClientProfilesNamespace = "Mcp.Benchmark.ClientProfiles";
    private const string FleetNamespace = "Mcp.Benchmark.Fleet";

    [Fact]
    public void Core_Should_Not_Depend_On_Infrastructure()
    {
        var result = Types.InAssembly(typeof(McpValidatorConfiguration).Assembly)
            .ShouldNot()
            .HaveDependencyOn(InfrastructureNamespace)
            .GetResult();

        Assert.True(result.IsSuccessful, "Core layer should not depend on Infrastructure layer.");
    }

    [Fact]
    public void Core_Should_Not_Depend_On_CLI()
    {
        var result = Types.InAssembly(typeof(McpValidatorConfiguration).Assembly)
            .ShouldNot()
            .HaveDependencyOn(CliNamespace)
            .GetResult();

        Assert.True(result.IsSuccessful, "Core layer should not depend on CLI layer.");
    }

    [Fact]
    public void Core_Should_Not_Depend_On_ClientProfiles()
    {
        var result = Types.InAssembly(typeof(McpValidatorConfiguration).Assembly)
            .ShouldNot()
            .HaveDependencyOn(ClientProfilesNamespace)
            .GetResult();

        Assert.True(result.IsSuccessful, "Core layer should not depend on the client profiles layer.");
    }

    [Fact]
    public void Infrastructure_Should_Not_Depend_On_CLI()
    {
        // Assuming Infrastructure has a marker type, or we can load by name.
        // Let's use a type we know exists in Infrastructure, e.g. McpHttpClient or similar.
        // If we don't have a reference here, we might need to add one or load assembly by name.
        // For now, let's assume we can access Infrastructure types.
        // If not, we can skip this or add reference.
        // Let's try to find a type in Infrastructure.
        
        var infrastructureAssembly = System.Reflection.Assembly.Load("Mcp.Benchmark.Infrastructure");

        var result = Types.InAssembly(infrastructureAssembly)
            .ShouldNot()
            .HaveDependencyOn(CliNamespace)
            .GetResult();

        Assert.True(result.IsSuccessful, "Infrastructure layer should not depend on CLI layer.");
    }

    [Fact]
    public void Infrastructure_Should_Not_Depend_On_ClientProfiles()
    {
        var infrastructureAssembly = System.Reflection.Assembly.Load("Mcp.Benchmark.Infrastructure");

        var result = Types.InAssembly(infrastructureAssembly)
            .ShouldNot()
            .HaveDependencyOn(ClientProfilesNamespace)
            .GetResult();

        Assert.True(result.IsSuccessful, "Infrastructure layer should not depend on the client profiles layer.");
    }

    [Fact]
    public void Cli_Command_Handlers_Should_Not_Depend_On_Infrastructure()
    {
        var cliAssembly = typeof(Mcp.Benchmark.CLI.ValidateCommand).Assembly;

        var result = Types.InAssembly(cliAssembly)
            .That()
            .ResideInNamespace("Mcp.Benchmark.CLI")
            .And()
            .HaveNameEndingWith("Command")
            .ShouldNot()
            .HaveDependencyOn(InfrastructureNamespace)
            .GetResult();

        Assert.True(result.IsSuccessful, "CLI command handlers should depend on Core abstractions, not Infrastructure.");
    }

    [Fact]
    public void ClientProfiles_Should_Not_Depend_On_Infrastructure_Or_CLI()
    {
        var clientProfilesAssembly = typeof(ClientProfileEvaluator).Assembly;

        var infrastructureResult = Types.InAssembly(clientProfilesAssembly)
            .ShouldNot()
            .HaveDependencyOn(InfrastructureNamespace)
            .GetResult();

        var cliResult = Types.InAssembly(clientProfilesAssembly)
            .ShouldNot()
            .HaveDependencyOn(CliNamespace)
            .GetResult();

        Assert.True(infrastructureResult.IsSuccessful, "Client profiles should not depend on Infrastructure.");
        Assert.True(cliResult.IsSuccessful, "Client profiles should not depend on CLI.");
    }

    [Fact]
    public void Existing_Product_Layers_Should_Not_Depend_On_Fleet()
    {
        var assemblies = new[]
        {
            typeof(McpValidatorConfiguration).Assembly,
            System.Reflection.Assembly.Load("Mcp.Benchmark.Infrastructure"),
            typeof(Mcp.Benchmark.CLI.ValidateCommand).Assembly,
            typeof(ClientProfileEvaluator).Assembly
        };

        foreach (var assembly in assemblies)
        {
            var result = Types.InAssembly(assembly).ShouldNot().HaveDependencyOn(FleetNamespace).GetResult();
            Assert.True(result.IsSuccessful, $"{assembly.GetName().Name} must not depend on hosted Fleet mode.");
        }
    }

    [Fact]
    public void Fleet_Should_Remain_Host_Neutral()
    {
        var fleetAssembly = typeof(FleetJob).Assembly;
        foreach (var forbidden in new[] { CoreNamespace, InfrastructureNamespace, CliNamespace, ClientProfilesNamespace })
        {
            var result = Types.InAssembly(fleetAssembly).ShouldNot().HaveDependencyOn(forbidden).GetResult();
            Assert.True(result.IsSuccessful, $"Fleet contracts must not depend on {forbidden}.");
        }
    }
}
