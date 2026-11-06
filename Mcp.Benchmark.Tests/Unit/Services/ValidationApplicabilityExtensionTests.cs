using FluentAssertions;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Infrastructure.Registries;
using Mcp.Benchmark.Infrastructure.Services;
using Mcp.Compliance.Spec;

namespace Mcp.Benchmark.Tests.Unit.Services;

public sealed class ValidationApplicabilityExtensionTests
{
    [Fact]
    public void Build_WithModernDiscovery_ShouldExposeSortedDistinctExtensionIds()
    {
        var configuration = new McpValidatorConfiguration
        {
            Server = new McpServerConfig { ProtocolVersion = "2026-07-28", Transport = "http" }
        };
        var session = new ValidationSessionContext(configuration, configuration.Server)
        {
            ProtocolVersion = "2026-07-28",
            ModernDiscovery = new TransportResult<ModernDiscoveryEvidence>
            {
                IsSuccessful = true,
                Payload = new ModernDiscoveryEvidence
                {
                    IsValid = true,
                    ExtensionIds = ["io.modelcontextprotocol/apps", "io.modelcontextprotocol/skills", "io.modelcontextprotocol/apps"]
                }
            }
        };

        var context = new ValidationApplicabilityResolver(new EmbeddedSchemaRegistry())
            .Build(session, configuration, [], ["io.modelcontextprotocol/skills"]);

        context.AdvertisedExtensionIds.Should().Equal(
            "io.modelcontextprotocol/apps",
            "io.modelcontextprotocol/skills");
        context.SelectedExtensionIds.Should().Equal("io.modelcontextprotocol/skills");
    }

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(false, false, false)]
    public void Matcher_ShouldResolveRequiredExtensionFromAdvertisedOrSelectedIds(
        bool advertised,
        bool selected,
        bool expected)
    {
        const string extensionId = "io.modelcontextprotocol/apps";
        var applicability = new ValidationApplicability { RequiredExtensionIds = [extensionId] };
        var context = new ValidationApplicabilityContext
        {
            NegotiatedProtocolVersion = "2026-07-28",
            SchemaVersion = "2026-07-28",
            Transport = "http",
            AccessMode = "public",
            AdvertisedExtensionIds = advertised ? [extensionId] : ["io.modelcontextprotocol/apps-preview"],
            SelectedExtensionIds = selected ? [extensionId] : []
        };

        ValidationPackApplicabilityMatcher.Matches(applicability, context).Should().Be(expected);
    }
}