using Mcp.Benchmark.CLI.Models;
using System.Text.Json;

namespace Mcp.Benchmark.Tests.Unit.Utilities;

public sealed class WindowsSupervisorProtocolTests
{
    [Theory]
    [InlineData("--version")]
    [InlineData("validate")]
    [InlineData("health-check")]
    [InlineData("discover")]
    public void ParseLaunch_AdmitsOnlyVersionedBoundedCliOperations(string command)
    {
        var launch = WindowsSupervisorProtocol.ParseLaunch(JsonSerializer.Serialize(new
        {
            protocolVersion = 1, arguments = new[] { command }, timeoutMs = 1000
        }));
        launch.Arguments.Should().Equal(command);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{\"protocolVersion\":2,\"arguments\":[\"validate\"],\"timeoutMs\":1000}")]
    [InlineData("{\"protocolVersion\":1,\"arguments\":[\"__mcpval-windows-supervisor\"],\"timeoutMs\":1000}")]
    [InlineData("{\"protocolVersion\":1,\"arguments\":[\"validate\"],\"timeoutMs\":0}")]
    [InlineData("{\"protocolVersion\":1,\"arguments\":[null],\"timeoutMs\":1000}")]
    [InlineData("{\"protocolVersion\":1,\"arguments\":[\"validate\"],\"timeoutMs\":1000,\"timeoutMs\":2000}")]
    [InlineData("{\"protocolVersion\":1,\"arguments\":[\"validate\"],\"timeoutMs\":1000,\"unknown\":true}")]
    public void ParseLaunch_RejectsMalformedOrIncompatibleFrames(string json)
    {
        var parse = () => WindowsSupervisorProtocol.ParseLaunch(json);
        parse.Should().Throw<Exception>();
    }

    [Fact]
    public async Task ControlReader_RequiresBoundedCompleteFrames()
    {
        var complete = await WindowsSupervisorProtocol.ReadControlLineAsync(new StringReader("{}\n"), CancellationToken.None);
        complete.Should().Be("{}");
        var oversized = () => WindowsSupervisorProtocol.ReadControlLineAsync(
            new StringReader(new string('x', WindowsSupervisorProtocol.MaximumControlCharacters + 1)), CancellationToken.None);
        await oversized.Should().ThrowAsync<InvalidDataException>();
        var truncated = () => WindowsSupervisorProtocol.ReadControlLineAsync(new StringReader("{}"), CancellationToken.None);
        await truncated.Should().ThrowAsync<InvalidDataException>();
    }

    [Theory]
    [InlineData("", "\"\"")]
    [InlineData("a b", "\"a b\"")]
    [InlineData("tail\\", "\"tail\\\\\"")]
    [InlineData("a\"b", "\"a\\\"b\"")]
    public void CommandLine_PreservesArgumentBoundaries(string argument, string expected)
    {
        WindowsSupervisorProtocol.BuildCommandLine("mcpval.exe", [argument]).Should().Be("\"mcpval.exe\" " + expected);
    }
}