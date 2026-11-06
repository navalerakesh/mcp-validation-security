using System.Text.Json;
using FluentAssertions;
using Mcp.Benchmark.Core.Constants;
using Mcp.Benchmark.Infrastructure.Services;

namespace Mcp.Benchmark.Tests.Unit.Services;

public sealed class ModernExtensionAdvertisementParserTests
{
    [Fact]
    public void Parse_KnownSettings_ShouldRetainOnlyTypedValues()
    {
        using var document = JsonDocument.Parse("""
            {
              "io.modelcontextprotocol/ui": {
                "mimeTypes": ["text/html;profile=mcp-app"],
                "untrusted": { "secret": "must-not-survive" }
              },
              "io.modelcontextprotocol/skills": { "directoryRead": true }
            }
            """);

        var advertisements = ModernExtensionAdvertisementParser.Parse(document.RootElement);

        advertisements[McpExtensionCatalog.Apps.Id].IsValid.Should().BeTrue();
        advertisements[McpExtensionCatalog.Apps.Id].MimeTypes.Should().Equal(McpExtensionCatalog.Apps.HtmlMimeType);
        advertisements[McpExtensionCatalog.Skills.Id].DirectoryRead.Should().BeTrue();
        JsonSerializer.Serialize(advertisements).Should().NotContain("secret");
    }

    [Fact]
    public void Parse_MalformedKnownSettings_ShouldRetainErrorsWithoutThrowing()
    {
        using var document = JsonDocument.Parse("""
            {
              "io.modelcontextprotocol/ui": { "mimeTypes": [42] },
              "io.modelcontextprotocol/skills": { "directoryRead": "yes" }
            }
            """);

        var advertisements = ModernExtensionAdvertisementParser.Parse(document.RootElement);

        advertisements.Values.Should().OnlyContain(value => !value.IsValid && value.Errors.Count > 0);
    }
}