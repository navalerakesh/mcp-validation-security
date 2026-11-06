using FluentAssertions;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Infrastructure.Scenarios;

namespace Mcp.Benchmark.Tests.Unit.Scenarios;

public sealed class McpAppResourceAnalyzerTests
{
    [Fact]
    public void Analyze_CompliantResource_ShouldProduceDigestWithoutFindings()
    {
        var analysis = McpAppResourceAnalyzer.Analyze("ui://weather/dashboard", Response("""
            <!doctype html><html><head><script src="https://cdn.example.com/app.js"></script><link href="./app.css" rel="stylesheet"></head><body><img src="data:image/png;base64,AA=="><script src="scripts/local.js"></script></body></html>
            """, """
            "csp": { "resourceDomains": ["https://cdn.example.com"] }
            """));

        analysis.ContentSha256.Should().MatchRegex("^[a-f0-9]{64}$");
        analysis.Findings.Should().BeEmpty();
    }

    [Fact]
    public void Analyze_UndeclaredOriginsDangerousContentAndPermissions_ShouldReportStructuredRisk()
    {
        var analysis = McpAppResourceAnalyzer.Analyze("ui://weather/dashboard", Response("""
            <!doctype html><html><head><base href="https://base.example.com/"></head><body>
            <script src="https://evil.example.com/app.js"></script><iframe src="javascript:alert(1)"></iframe><object data="payload.swf"></object>
            </body></html>
            """, """
            "csp": { "resourceDomains": ["https://cdn.example.com"] },
            "permissions": { "camera": {} }
            """));

        analysis.Permissions.Should().Equal("camera");
        analysis.Findings.Select(finding => finding.RuleId).Should().Contain([
            ValidationFindingRuleIds.AppsCspUndeclaredOrigin,
            ValidationFindingRuleIds.AppsDangerousHtmlContent,
            ValidationFindingRuleIds.AppsPermissionRisk
        ]);
        analysis.Findings.Should().OnlyContain(finding => finding.ExcludedFromAggregate);
    }

    [Theory]
    [InlineData("https://user@example.com")]
    [InlineData("https://example.com/path")]
    [InlineData("*")]
    public void Analyze_InvalidCspOrigin_ShouldReportSpecFailure(string origin)
    {
        var analysis = McpAppResourceAnalyzer.Analyze("ui://weather/dashboard", Response(
            "<!doctype html><html></html>",
            $"\"csp\": {{ \"connectDomains\": [\"{origin}\"] }}"));

        analysis.Findings.Should().Contain(finding => finding.RuleId == ValidationFindingRuleIds.AppsCspOriginInvalid);
    }

    private static string Response(string html, string uiMetadata) => $$"""
        {
          "jsonrpc": "2.0",
          "id": 1,
          "result": {
            "contents": [{
              "uri": "ui://weather/dashboard",
              "mimeType": "text/html;profile=mcp-app",
              "text": {{System.Text.Json.JsonSerializer.Serialize(html)}},
              "_meta": { "ui": { {{uiMetadata}} } }
            }]
          }
        }
        """;
}