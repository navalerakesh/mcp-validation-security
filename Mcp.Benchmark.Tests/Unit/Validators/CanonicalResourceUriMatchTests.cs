using System.Reflection;
using FluentAssertions;
using Mcp.Benchmark.Infrastructure.Validators;

namespace Mcp.Benchmark.Tests.Unit.Validators;

/// <summary>
/// RFC 8707 §2 + MCP Authorization §2.5.1.1: the OAuth `resource` parameter MUST identify
/// the canonical URI of the MCP server clients connect to. These tests pin the comparison
/// semantics so a regression cannot silently let mismatched audiences slip through.
/// </summary>
public class CanonicalResourceUriMatchTests
{
    private static bool Match(string metadataResource, string serverEndpoint)
    {
        var method = typeof(McpCompliantAuthValidator).GetMethod(
            "ResourceUriMatchesEndpoint",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("ResourceUriMatchesEndpoint helper is not present.");

        var metadataUri = new Uri(metadataResource, UriKind.Absolute);
        var endpointUri = new Uri(serverEndpoint, UriKind.Absolute);
        return (bool)method.Invoke(null, new object[] { metadataUri, endpointUri })!;
    }

    [Theory]
    [InlineData("https://mcp.example.com/mcp", "https://mcp.example.com/mcp")]
    [InlineData("https://mcp.example.com/mcp", "https://mcp.example.com/mcp/")]      // trailing slash equivalence (spec §2.5.1.1)
    [InlineData("https://MCP.Example.COM/mcp", "https://mcp.example.com/mcp")]        // case-insensitive host (spec §2.5.1.1)
    [InlineData("HTTPS://mcp.example.com/mcp", "https://mcp.example.com/mcp")]        // case-insensitive scheme
    public void Matching_CanonicalUris_AreEquivalent(string metadataResource, string endpoint)
    {
        Match(metadataResource, endpoint).Should().BeTrue();
    }

    [Theory]
    [InlineData("https://mcp.example.com/mcp", "https://other.example.com/mcp")]      // different host
    [InlineData("https://mcp.example.com/mcp", "https://mcp.example.com/other")]      // different path
    [InlineData("https://mcp.example.com:8443/mcp", "https://mcp.example.com/mcp")]   // different port
    [InlineData("http://mcp.example.com/mcp", "https://mcp.example.com/mcp")]         // different scheme
    public void Mismatching_CanonicalUris_AreNotEquivalent(string metadataResource, string endpoint)
    {
        Match(metadataResource, endpoint).Should().BeFalse();
    }
}
