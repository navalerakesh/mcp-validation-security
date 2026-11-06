using System.Security.Cryptography;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Utilities;

namespace Mcp.Benchmark.Core.Services;

public static class ProviderAuthorizationVerifier
{
    private const string GrantAlgorithm = "ECDSA_P256_SHA256_P1363";
    private const string HarnessAlgorithm = "ECDSA_P256_SHA256_P1363";
    private const string P256Oid = "1.2.840.10045.3.1.7";

    public static ProviderAuthorizationVerification Verify(
        ProviderAuthorizationGrant grant,
        string trustedPublicKeyPem)
    {
        ArgumentNullException.ThrowIfNull(grant);
        ArgumentException.ThrowIfNullOrWhiteSpace(trustedPublicKeyPem);
        var reasons = new List<string>();
        if (!string.Equals(grant.SignatureAlgorithm, GrantAlgorithm, StringComparison.Ordinal))
        {
            reasons.Add($"Unsupported provider authorization signature algorithm '{grant.SignatureAlgorithm}'.");
        }

        var expectedDigest = ComputeGrantDigest(grant);
        if (!string.Equals(grant.GrantDocumentSha256, expectedDigest, StringComparison.Ordinal))
        {
            reasons.Add("Provider authorization grant digest does not match its canonical payload.");
        }

        string? verifiedKeyId = null;
        try
        {
            using var key = ECDsa.Create();
            key.ImportFromPem(trustedPublicKeyPem);
            EnsureP256(key);
            var publicKey = key.ExportSubjectPublicKeyInfo();
            verifiedKeyId = Convert.ToHexString(SHA256.HashData(publicKey)).ToLowerInvariant();
            if (!string.Equals(grant.SigningKeyId, verifiedKeyId, StringComparison.Ordinal))
            {
                reasons.Add("Provider authorization signing key does not match the operator-trusted key.");
            }
            else if (!TryDecodeSignature(grant.SignatureBase64, out var signature) ||
                !key.VerifyHash(
                    Convert.FromHexString(expectedDigest),
                    signature,
                    DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
            {
                reasons.Add("Provider authorization signature verification failed.");
            }
        }
        catch (Exception exception) when (exception is ArgumentException or CryptographicException or FormatException)
        {
            reasons.Add("Provider authorization trusted key or signature is malformed.");
        }

        return new ProviderAuthorizationVerification
        {
            Status = reasons.Count == 0
                ? ProviderAuthorizationVerificationStatus.Verified
                : ProviderAuthorizationVerificationStatus.Invalid,
            GrantId = grant.GrantId,
            VerifiedKeyId = verifiedKeyId,
            Reasons = reasons
        };
    }

    public static ProviderAuthorizationVerification Verify(
        ProviderHarnessManifest manifest,
        string trustedPublicKeyPem)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var grantVerification = Verify(manifest.Authorization, trustedPublicKeyPem);
        var reasons = grantVerification.Reasons.ToList();
        var expectedDigest = ComputeManifestDigest(manifest);
        if (!string.Equals(manifest.SignatureAlgorithm, HarnessAlgorithm, StringComparison.Ordinal))
        {
            reasons.Add($"Unsupported provider harness signature algorithm '{manifest.SignatureAlgorithm}'.");
        }
        if (!string.Equals(manifest.ManifestDocumentSha256, expectedDigest, StringComparison.Ordinal))
        {
            reasons.Add("Provider harness digest does not match its canonical payload.");
        }

        try
        {
            using var key = ECDsa.Create();
            key.ImportFromPem(trustedPublicKeyPem);
            EnsureP256(key);
            var verifiedKeyId = Convert.ToHexString(SHA256.HashData(key.ExportSubjectPublicKeyInfo())).ToLowerInvariant();
            if (!string.Equals(manifest.SigningKeyId, verifiedKeyId, StringComparison.Ordinal) ||
                !string.Equals(manifest.SigningKeyId, manifest.Authorization.SigningKeyId, StringComparison.Ordinal))
            {
                reasons.Add("Provider harness signing key does not match the authorized operator-trusted key.");
            }
            else if (!TryDecodeSignature(manifest.SignatureBase64, out var signature) ||
                !key.VerifyHash(
                    Convert.FromHexString(expectedDigest),
                    signature,
                    DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
            {
                reasons.Add("Provider harness signature verification failed.");
            }
        }
        catch (Exception exception) when (exception is ArgumentException or CryptographicException or FormatException)
        {
            reasons.Add("Provider harness trusted key or signature is malformed.");
        }

        return new ProviderAuthorizationVerification
        {
            Status = reasons.Count == 0
                ? ProviderAuthorizationVerificationStatus.Verified
                : ProviderAuthorizationVerificationStatus.Invalid,
            GrantId = manifest.Authorization.GrantId,
            VerifiedKeyId = grantVerification.VerifiedKeyId,
            Reasons = reasons.Distinct(StringComparer.Ordinal).ToArray()
        };
    }

    public static string ComputeGrantDigest(ProviderAuthorizationGrant grant)
    {
        ArgumentNullException.ThrowIfNull(grant);
        return CanonicalJsonDigest.ComputeObject(new
        {
            grant.GrantId,
            grant.Authority,
            grant.TargetIdentitySha256,
            grant.ValidFromUtc,
            grant.ExpiresAtUtc,
            AllowedExecutionModes = grant.AllowedExecutionModes.OrderBy(mode => mode).ToArray(),
            AllowedProbeClasses = grant.AllowedProbeClasses.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            grant.SignatureAlgorithm,
            grant.SigningKeyId
        });
    }

    public static string ComputeManifestDigest(ProviderHarnessManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        return CanonicalJsonDigest.ComputeObject(new
        {
            manifest.DocumentType,
            manifest.DocumentSchemaVersion,
            manifest.ProviderId,
            manifest.TargetIdentitySha256,
            manifest.ProtocolRevision,
            AuthorizationGrantDocumentSha256 = manifest.Authorization.GrantDocumentSha256,
            AuthorizationSignatureBase64 = manifest.Authorization.SignatureBase64,
            manifest.RequiredRunCount,
            manifest.MinimumConsistency,
            Runs = manifest.Runs
                .OrderBy(run => run.RunId, StringComparer.Ordinal)
                .Select(run => new
                {
                    run.RunId,
                    run.ResultSha256,
                    run.ExecutionPlanSha256,
                    run.StartedAtUtc,
                    run.EndedAtUtc,
                    run.CoreConformance,
                    run.SecurityAssurance,
                    run.OperationalReadiness,
                    NonSuccessClaimIds = run.NonSuccessClaimIds.OrderBy(value => value, StringComparer.Ordinal).ToArray()
                })
                .ToArray(),
            manifest.SignatureAlgorithm,
            manifest.SigningKeyId
        });
    }

    private static void EnsureP256(ECDsa key)
    {
        var parameters = key.ExportParameters(includePrivateParameters: false);
        if (key.KeySize != 256 || !string.Equals(parameters.Curve.Oid.Value, P256Oid, StringComparison.Ordinal))
        {
            throw new ArgumentException("Provider authorization keys must use the NIST P-256 curve.");
        }
    }

    private static bool TryDecodeSignature(string value, out byte[] signature)
    {
        signature = [];
        if (string.IsNullOrWhiteSpace(value)) return false;
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