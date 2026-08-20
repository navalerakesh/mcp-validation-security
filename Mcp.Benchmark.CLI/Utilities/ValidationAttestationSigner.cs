using System.Security.Cryptography;
using Mcp.Benchmark.CLI.Models;

namespace Mcp.Benchmark.CLI.Utilities;

internal static class ValidationAttestationSigner
{
    private const string P256Oid = "1.2.840.10045.3.1.7";

    public static ValidationAttestation Sign(string validationId, string subjectPath, string privateKeyPem)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(validationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(privateKeyPem);

        var subjectBytes = File.ReadAllBytes(subjectPath);
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        key.ImportFromPem(privateKeyPem);
        EnsureP256(key);
        var publicKey = key.ExportSubjectPublicKeyInfo();

        return new ValidationAttestation
        {
            ValidationId = validationId,
            SubjectFile = Path.GetFileName(subjectPath),
            SubjectSha256 = Convert.ToHexString(SHA256.HashData(subjectBytes)).ToLowerInvariant(),
            KeyId = Convert.ToHexString(SHA256.HashData(publicKey)).ToLowerInvariant(),
            PublicKeyPem = key.ExportSubjectPublicKeyInfoPem(),
            SignatureBase64 = Convert.ToBase64String(key.SignData(subjectBytes, HashAlgorithmName.SHA256))
        };
    }

    public static bool Verify(ValidationAttestation attestation, ReadOnlySpan<byte> subjectBytes)
    {
        ArgumentNullException.ThrowIfNull(attestation);
        if (!string.Equals(attestation.Algorithm, "ECDSA_P256_SHA256", StringComparison.Ordinal))
        {
            return false;
        }
        using var key = ECDsa.Create();
        key.ImportFromPem(attestation.PublicKeyPem);
        EnsureP256(key);
        return key.VerifyData(subjectBytes, Convert.FromBase64String(attestation.SignatureBase64), HashAlgorithmName.SHA256) &&
            string.Equals(
                attestation.SubjectSha256,
                Convert.ToHexString(SHA256.HashData(subjectBytes)).ToLowerInvariant(),
                StringComparison.Ordinal);
    }

    private static void EnsureP256(ECDsa key)
    {
        var parameters = key.ExportParameters(includePrivateParameters: false);
        if (key.KeySize != 256 || !string.Equals(parameters.Curve.Oid.Value, P256Oid, StringComparison.Ordinal))
        {
            throw new ArgumentException("Attestation keys must use the NIST P-256 curve.");
        }
    }
}
