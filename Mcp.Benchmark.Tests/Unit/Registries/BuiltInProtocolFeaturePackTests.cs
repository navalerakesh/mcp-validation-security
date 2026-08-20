using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Infrastructure.Registries;
using Mcp.Compliance.Spec;

namespace Mcp.Benchmark.Tests.Unit.Registries;

public sealed class BuiltInProtocolFeaturePackTests
{
    private readonly BuiltInProtocolFeaturePack _pack = new(new EmbeddedSchemaRegistry());

    [Fact]
    public void BuildFeatureSet_CurrentVersion_UsesModernStatelessContract()
    {
        var features = _pack.BuildFeatureSet(CreateContext("2026-07-28"));

        features.Era.Should().Be(McpProtocolEra.Modern);
        features.SupportsServerDiscovery.Should().BeTrue();
        features.UsesPerRequestMetadata.Should().BeTrue();
        features.IsSessionless.Should().BeTrue();
        features.SupportsSubscriptionsListen.Should().BeTrue();
        features.RequiresCacheMetadata.Should().BeTrue();
        features.SupportsMultiRoundTripRequests.Should().BeTrue();
        features.FeatureLifecycle["logging/setLevel"].Should().Be(ProtocolFeatureLifecycle.Removed);
        features.SupportsExtensionNegotiation.Should().BeTrue();
        features.UsesTasksExtension.Should().BeTrue();
        features.SupportsBatchJsonRpc.Should().BeFalse();
    }

    [Fact]
    public void BuildFeatureSet_PreviousVersion_PreservesLegacyContract()
    {
        var features = _pack.BuildFeatureSet(CreateContext("2025-11-25"));

        features.Era.Should().Be(McpProtocolEra.Legacy);
        features.FeatureLifecycle["logging/setLevel"].Should().Be(ProtocolFeatureLifecycle.Deprecated);
        features.SupportsServerDiscovery.Should().BeFalse();
        features.UsesPerRequestMetadata.Should().BeFalse();
        features.IsSessionless.Should().BeFalse();
        features.SupportsSubscriptionsListen.Should().BeFalse();
        features.UsesTasksExtension.Should().BeFalse();
        features.SupportsTasksSurface.Should().BeTrue();
    }

    private static ValidationApplicabilityContext CreateContext(string version) => new()
    {
        NegotiatedProtocolVersion = version,
        SchemaVersion = version,
        Transport = "http",
        AccessMode = "public"
    };
}