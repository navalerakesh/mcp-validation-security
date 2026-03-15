using Mcp.Compliance.Spec;
using FluentAssertions;
using Xunit;

namespace Mcp.Benchmark.Tests.Unit.Spec;

/// <summary>
/// Tests for the embedded schema registry and protocol version catalog.
/// </summary>
public class SchemaRegistryTests
{
    private readonly EmbeddedSchemaRegistry _registry = new();

    [Fact]
    public void GetSchema_WithValidVersion_ShouldNotThrow()
    {
        // The registry should handle requests without throwing
        var act = () => _registry.GetSchema(ProtocolVersions.V2025_03_26, "protocol", "schema");
        act.Should().NotThrow();
    }

    [Fact]
    public void ProtocolVersions_ShouldHaveKnownVersions()
    {
        ProtocolVersions.V2024_11_05.Value.Should().Be("2024-11-05");
        ProtocolVersions.V2025_03_26.Value.Should().Be("2025-03-26");
        ProtocolVersions.V2025_06_18.Value.Should().Be("2025-06-18");
        ProtocolVersions.V2025_11_25.Value.Should().Be("2025-11-25");
    }

    [Fact]
    public void ProtocolVersion_ToString_ShouldReturnValue()
    {
        ProtocolVersions.V2025_03_26.ToString().Should().Be("2025-03-26");
    }

    [Fact]
    public void ProtocolVersion_Equality_ShouldWork()
    {
        var v1 = new ProtocolVersion("2025-03-26");
        var v2 = ProtocolVersions.V2025_03_26;
        v1.Should().Be(v2);
    }

    [Fact]
    public void SchemaDescriptor_ShouldHaveProperties()
    {
        var desc = new SchemaDescriptor 
        { 
            Version = ProtocolVersions.V2025_03_26, 
            Area = "protocol", 
            Name = "schema",
            Description = "test description" 
        };
        desc.Name.Should().Be("schema");
        desc.Area.Should().Be("protocol");
        desc.Description.Should().Be("test description");
    }
}
