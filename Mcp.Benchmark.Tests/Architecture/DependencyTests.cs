using NetArchTest.Rules;
using Xunit;
using Mcp.Benchmark.ClientProfiles;
using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.Tests.Architecture;

public class DependencyTests
{
    private const string CoreNamespace = "Mcp.Benchmark.Core";
    private const string InfrastructureNamespace = "Mcp.Benchmark.Infrastructure";
    private const string CliNamespace = "Mcp.Benchmark.CLI";
    private const string ClientProfilesNamespace = "Mcp.Benchmark.ClientProfiles";

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
}
