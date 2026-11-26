using Mcp.Benchmark.CLI;
using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.Tests.Unit.Services;

public sealed class AuthenticationInvocationAdmissionTests
{
    [Theory]
    [InlineData(false, null)]
    [InlineData(false, "supplied-token")]
    [InlineData(true, null)]
    public void Configuration_ShouldNotGrantInteractiveAuthorityWithoutInvocationAdmission(bool interactive, string? token)
    {
        var server = new McpServerConfig
        {
            Authentication = new AuthenticationConfig { AllowInteractive = true, Type = "oauth", Token = "configured-token" }
        };
        CommandConnectionHelper.ApplyAuthenticationOverrides(server, token, interactive);
        server.Authentication.AllowInteractive.Should().Be(interactive);
        server.Authentication.Token.Should().Be(token ?? "configured-token");
        server.Authentication.Type.Should().Be("oauth");
    }

    [Fact]
    public void NoAuthenticationConfiguration_ShouldRemainAbsentForNoninteractivePublicInvocation()
    {
        var server = new McpServerConfig { Authentication = null };
        CommandConnectionHelper.ApplyAuthenticationOverrides(server, null, false);
        server.Authentication.Should().BeNull();
    }
}
