using System.Security.Cryptography;

namespace Mcp.Benchmark.Core.Utilities;

public static class P256ArtifactSignatures
{
    public const string Algorithm = "ECDSA_P256_SHA256_P1363";

    public static void EnsureKey(ECDsa key)
    {
        var parameters = key.ExportParameters(includePrivateParameters: false);
        if (key.KeySize != 256 || !string.Equals(parameters.Curve.Oid.Value, ECCurve.NamedCurves.nistP256.Oid.Value, StringComparison.Ordinal))
            throw new ArgumentException("Artifact signature keys must use the NIST P-256 curve.");
    }

    public static bool TryDecodeSignature(string? value, out byte[] signature)
    {
        signature = [];
        if (string.IsNullOrWhiteSpace(value) || value.Length > 88) return false;
        try
        {
            signature = Convert.FromBase64String(value);
            return signature.Length == 64;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}