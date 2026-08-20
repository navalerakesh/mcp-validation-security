using Mcp.Benchmark.Infrastructure.Utilities;

namespace Mcp.Benchmark.Tests.Unit.Http;

public sealed class ModernListSemanticsTests
{
    [Fact]
    public void Assess_EquivalentPropertyOrdering_ShouldProduceSameFingerprint()
    {
        var first = ModernListSemantics.Assess(
            "{\"result\":{\"resultType\":\"complete\",\"cacheScope\":\"public\",\"ttlMs\":60,\"tools\":[{\"name\":\"search\",\"description\":\"docs\"}]}}",
            "tools");
        var second = ModernListSemantics.Assess(
            "{\"result\":{\"tools\":[{\"description\":\"docs\",\"name\":\"search\"}],\"ttlMs\":60,\"cacheScope\":\"public\",\"resultType\":\"complete\"}}",
            "tools");

        first.IsValid.Should().BeTrue();
        second.IsValid.Should().BeTrue();
        second.Fingerprint.Should().Be(first.Fingerprint);
    }

    [Fact]
    public void Assess_ChangedOrderOrCursor_ShouldProduceDifferentFingerprint()
    {
        var first = ModernListSemantics.Assess(
            "{\"result\":{\"resultType\":\"complete\",\"cacheScope\":\"private\",\"ttlMs\":0,\"nextCursor\":\"a\",\"tools\":[{\"name\":\"one\"},{\"name\":\"two\"}]}}",
            "tools");
        var second = ModernListSemantics.Assess(
            "{\"result\":{\"resultType\":\"complete\",\"cacheScope\":\"private\",\"ttlMs\":0,\"nextCursor\":\"b\",\"tools\":[{\"name\":\"two\"},{\"name\":\"one\"}]}}",
            "tools");

        first.IsValid.Should().BeTrue();
        second.IsValid.Should().BeTrue();
        second.Fingerprint.Should().NotBe(first.Fingerprint);
    }

    [Theory]
    [InlineData("{\"result\":{\"resultType\":\"complete\",\"ttlMs\":0,\"tools\":[]}}")]
    [InlineData("{\"result\":{\"resultType\":\"complete\",\"cacheScope\":\"shared\",\"ttlMs\":0,\"tools\":[]}}")]
    [InlineData("{\"result\":{\"resultType\":\"complete\",\"cacheScope\":\"public\",\"ttlMs\":-1,\"tools\":[]}}")]
    public void Assess_InvalidCacheMetadata_ShouldFail(string json)
    {
        ModernListSemantics.Assess(json, "tools").IsValid.Should().BeFalse();
    }
}
