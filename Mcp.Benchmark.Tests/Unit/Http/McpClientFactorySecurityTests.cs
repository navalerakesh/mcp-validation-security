using System.Reflection;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Infrastructure.Http;
using ModelContextProtocol.Client;

namespace Mcp.Benchmark.Tests.Unit.Http;

public sealed class McpClientFactorySecurityTests
{
    [Fact]
    public void CreateCacheKey_DoesNotRetainRawCredentials()
    {
        const string token = "token-canary-not-for-storage";
        const string password = "password-canary-not-for-storage";
        const string sensitiveHeader = "header-canary-not-for-storage";
        var authentication = new AuthenticationConfig
        {
            Type = "bearer",
            Token = token,
            Username = "test-user",
            Password = password
        };
        var headers = new Dictionary<string, string>
        {
            ["X-Sensitive-Test"] = sensitiveHeader
        };
        var method = typeof(McpClientFactory).GetMethod("CreateCacheKey", BindingFlags.NonPublic | BindingFlags.Static);

        method.Should().NotBeNull();
        var key = method!.Invoke(null, ["https://example.test/mcp", "2025-11-25", authentication, headers, null]);
        var storedRepresentation = key?.ToString();

        storedRepresentation.Should().NotBeNull();
        storedRepresentation.Should().NotContain(token);
        storedRepresentation.Should().NotContain(password);
        storedRepresentation.Should().NotContain(sensitiveHeader);
    }

    [Theory]
    [InlineData(McpProtocolEraSelection.Auto, "2026-07-28", HttpTransportMode.AutoDetect)]
    [InlineData(McpProtocolEraSelection.Modern, "2026-07-28", HttpTransportMode.StreamableHttp)]
    [InlineData(McpProtocolEraSelection.Legacy, "2025-11-25", HttpTransportMode.AutoDetect)]
    [InlineData(McpProtocolEraSelection.Legacy, "2024-11-05", HttpTransportMode.Sse)]
    public void ResolveHttpTransportMode_ShouldRespectEraAndProtocolProfile(
        McpProtocolEraSelection selection,
        string protocolVersion,
        HttpTransportMode expected)
    {
        var server = new McpServerConfig
        {
            ProtocolEra = selection,
            ProtocolVersion = protocolVersion
        };

        McpClientFactory.ResolveHttpTransportMode(server).Should().Be(expected);
    }

    [Fact]
    public void ResolveToken_EnvironmentReference_ShouldResolveTransientlyWithDirectTokenPrecedence()
    {
        const string variableName = "MCPVAL_TEST_SECRET_REF";
        var previous = Environment.GetEnvironmentVariable(variableName);
        Environment.SetEnvironmentVariable(variableName, "environment-secret");
        try
        {
            var authentication = new AuthenticationConfig
            {
                Type = "bearer",
                TokenRef = new SecretRef { Provider = SecretRefProviders.Environment, Name = variableName }
            };

            McpAuthenticationHelper.ResolveToken(authentication).Should().Be("environment-secret");
            authentication.Token = "direct-secret";
            McpAuthenticationHelper.ResolveToken(authentication).Should().Be("direct-secret");
        }
        finally
        {
            Environment.SetEnvironmentVariable(variableName, previous);
        }
    }
}