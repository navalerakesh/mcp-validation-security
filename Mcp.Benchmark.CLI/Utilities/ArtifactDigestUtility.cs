using System.Security.Cryptography;
using Mcp.Benchmark.Core.Utilities;

namespace Mcp.Benchmark.CLI.Utilities;

internal static class ArtifactDigestUtility
{
    public static string ComputeText(string value)
        => CanonicalJsonDigest.ComputeText(value);

    public static string ComputeObject<T>(T value)
        => CanonicalJsonDigest.ComputeObject(value, ArtifactJsonOptions.Create(writeIndented: false));

    public static string ComputeFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

}
