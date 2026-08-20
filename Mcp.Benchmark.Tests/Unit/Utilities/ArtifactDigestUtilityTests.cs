using Mcp.Benchmark.CLI.Utilities;

namespace Mcp.Benchmark.Tests.Unit.Utilities;

public sealed class ArtifactDigestUtilityTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"mcpval-digest-{Guid.NewGuid():N}.json");

    [Fact]
    public void ComputeObject_EquivalentInputs_ShouldReplayDeterministically()
    {
        var first = ArtifactDigestUtility.ComputeObject(new Dictionary<string, object> { ["mode"] = "safe", ["requests"] = 10 });
        var second = ArtifactDigestUtility.ComputeObject(new Dictionary<string, object> { ["requests"] = 10, ["mode"] = "safe" });

        first.Should().Be(second);
        first.Should().MatchRegex("^[a-f0-9]{64}$");
    }

    [Fact]
    public void ComputeFile_ShouldHashFinalBytes()
    {
        File.WriteAllText(_path, "first");
        var first = ArtifactDigestUtility.ComputeFile(_path);
        File.WriteAllText(_path, "second");
        var second = ArtifactDigestUtility.ComputeFile(_path);

        first.Should().NotBe(second);
    }

    public void Dispose()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }
}
