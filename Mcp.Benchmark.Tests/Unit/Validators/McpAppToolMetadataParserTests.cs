using System.Text.Json;
using FluentAssertions;
using Mcp.Benchmark.Infrastructure.Validators;

namespace Mcp.Benchmark.Tests.Unit.Validators;

public sealed class McpAppToolMetadataParserTests
{
    [Fact]
    public void Parse_NestedMetadata_ShouldCaptureTypedFields()
    {
        using var document = JsonDocument.Parse("""
            {
              "ui": {
                "resourceUri": "ui://weather/dashboard",
                "visibility": ["model", "app"]
              }
            }
            """);

        var metadata = McpAppToolMetadataParser.Parse(document.RootElement);

        metadata.Should().NotBeNull();
        metadata!.ResourceUri.Should().Be("ui://weather/dashboard");
        metadata.Visibility.Should().Equal("model", "app");
        metadata.IsMalformed.Should().BeFalse();
        metadata.UsesDeprecatedResourceUri.Should().BeFalse();
    }

    [Fact]
    public void Parse_DeprecatedMetadata_ShouldRetainUriAndMarkDeprecation()
    {
        using var document = JsonDocument.Parse("""{"ui/resourceUri":"ui://weather/dashboard"}""");

        var metadata = McpAppToolMetadataParser.Parse(document.RootElement);

        metadata!.ResourceUri.Should().Be("ui://weather/dashboard");
        metadata.UsesDeprecatedResourceUri.Should().BeTrue();
        metadata.IsMalformed.Should().BeFalse();
    }

    [Fact]
    public void Parse_ConflictingUrisAndInvalidVisibility_ShouldMarkMalformedWithoutRawMetadata()
    {
        using var document = JsonDocument.Parse("""
            {
              "ui": { "resourceUri": "ui://weather/new", "visibility": ["model", 42] },
              "ui/resourceUri": "ui://weather/old"
            }
            """);

        var metadata = McpAppToolMetadataParser.Parse(document.RootElement);

        metadata!.IsMalformed.Should().BeTrue();
        metadata.Issues.Should().HaveCount(2);
        metadata.Issues.Should().Contain(issue => issue.Contains("entries must be strings", StringComparison.Ordinal));
        metadata.Issues.Should().Contain(issue => issue.Contains("conflict", StringComparison.Ordinal));
    }
}