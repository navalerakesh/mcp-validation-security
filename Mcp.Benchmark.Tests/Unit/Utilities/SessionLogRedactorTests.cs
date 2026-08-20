using Mcp.Benchmark.CLI.Utilities.Logging;
using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.Tests.Unit.Utilities;

public sealed class SessionLogRedactorTests
{
    [Theory]
    [InlineData("node server.js --token secret-canary")]
    [InlineData("tool --password 'secret-canary'")]
    [InlineData("tool --api-key=secret-canary")]
    [InlineData("https://user:secret-canary@example.test/mcp")]
    [InlineData("https://example.test/mcp?password=secret-canary&mode=test")]
    [InlineData("Set-Cookie: affinity=secret-canary; Secure; HttpOnly")]
    [InlineData("Cookie: affinity=secret-canary")]
    [InlineData("Mcp-Session-Id: secret-canary")]
    [InlineData("{\"client_secret\":\"secret-canary\"}")]
    [InlineData("access_token=secret-canary")]
    public void Redact_ShouldRemoveFlagStyleSecrets(string message)
    {
        SessionLogRedactor.Redact(message, RedactionLevel.Strict).Should().NotContain("secret-canary");
    }

    [Theory]
    [InlineData("ghp_abcdefghijklmnopqrstuvwxyz123456")]
    [InlineData("github_pat_abcdefghijklmnopqrstuvwxyz123456")]
    [InlineData("eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiJzZWNyZXQifQ.signature-canary")]
    public void Redact_ShouldRemoveKnownTokenShapes(string token)
    {
        SessionLogRedactor.Redact($"value={token}", RedactionLevel.Strict).Should().NotContain(token);
    }
}