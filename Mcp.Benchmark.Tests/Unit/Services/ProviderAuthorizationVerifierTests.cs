using System.Security.Cryptography;
using FluentAssertions;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Services;

namespace Mcp.Benchmark.Tests.Unit.Services;

public sealed class ProviderAuthorizationVerifierTests
{
    [Fact]
    public void Verify_TrustedSignedGrant_ShouldVerify()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var grant = SignGrant(key);

        var result = ProviderAuthorizationVerifier.Verify(grant, key.ExportSubjectPublicKeyInfoPem());

        result.Status.Should().Be(ProviderAuthorizationVerificationStatus.Verified);
        result.VerifiedKeyId.Should().Be(grant.SigningKeyId);
        result.Reasons.Should().BeEmpty();
    }

    [Fact]
    public void Verify_TamperedTarget_ShouldInvalidate()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var signed = SignGrant(key);
        var tampered = CopyGrant(signed, targetIdentitySha256: new string('b', 64));

        var result = ProviderAuthorizationVerifier.Verify(tampered, key.ExportSubjectPublicKeyInfoPem());

        result.Status.Should().Be(ProviderAuthorizationVerificationStatus.Invalid);
        result.Reasons.Should().Contain(reason => reason.Contains("digest", StringComparison.Ordinal));
    }

    [Fact]
    public void Verify_DifferentTrustedKey_ShouldInvalidate()
    {
        using var signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var trusted = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        var result = ProviderAuthorizationVerifier.Verify(SignGrant(signer), trusted.ExportSubjectPublicKeyInfoPem());

        result.Status.Should().Be(ProviderAuthorizationVerificationStatus.Invalid);
        result.Reasons.Should().Contain(reason => reason.Contains("trusted key", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("not-base64")]
    [InlineData("")]
    public void Verify_MalformedSignature_ShouldInvalidate(string signature)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var signed = SignGrant(key);
        var malformed = CopyGrant(signed, signatureBase64: signature);

        ProviderAuthorizationVerifier.Verify(malformed, key.ExportSubjectPublicKeyInfoPem()).Status
            .Should().Be(ProviderAuthorizationVerificationStatus.Invalid);
    }

    [Fact]
    public void Verify_TrustedSignedHarness_ShouldVerifyPriorRunReceipts()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var harness = SignHarness(key, SignGrant(key));

        var result = ProviderAuthorizationVerifier.Verify(harness, key.ExportSubjectPublicKeyInfoPem());

        result.Status.Should().Be(ProviderAuthorizationVerificationStatus.Verified);
        result.Reasons.Should().BeEmpty();
    }

    [Fact]
    public void Verify_TamperedPriorRun_ShouldInvalidateHarness()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var signed = SignHarness(key, SignGrant(key));
        var tamperedRun = new ProviderHarnessRunReceipt
        {
            RunId = signed.Runs[0].RunId,
            ResultSha256 = new string('f', 64),
            ExecutionPlanSha256 = signed.Runs[0].ExecutionPlanSha256,
            StartedAtUtc = signed.Runs[0].StartedAtUtc,
            EndedAtUtc = signed.Runs[0].EndedAtUtc,
            CoreConformance = signed.Runs[0].CoreConformance,
            SecurityAssurance = signed.Runs[0].SecurityAssurance,
            OperationalReadiness = signed.Runs[0].OperationalReadiness
        };
        var tampered = CopyHarness(signed, [tamperedRun]);

        var result = ProviderAuthorizationVerifier.Verify(tampered, key.ExportSubjectPublicKeyInfoPem());

        result.Status.Should().Be(ProviderAuthorizationVerificationStatus.Invalid);
        result.Reasons.Should().Contain("Provider harness digest does not match its canonical payload.");
    }

    internal static ProviderAuthorizationGrant SignGrant(ECDsa key)
    {
        var keyId = Convert.ToHexString(SHA256.HashData(key.ExportSubjectPublicKeyInfo())).ToLowerInvariant();
        var unsigned = new ProviderAuthorizationGrant
        {
            GrantId = "grant-1",
            Authority = "provider-security-owner",
            TargetIdentitySha256 = new string('a', 64),
            ValidFromUtc = new DateTimeOffset(2026, 11, 5, 11, 0, 0, TimeSpan.Zero),
            ExpiresAtUtc = new DateTimeOffset(2026, 11, 5, 13, 0, 0, TimeSpan.Zero),
            AllowedExecutionModes = [ExecutionMode.Standard],
            AllowedProbeClasses = ["operations-bounded", "protocol-active", "security-non-destructive"],
            GrantDocumentSha256 = string.Empty,
            SigningKeyId = keyId,
            SignatureBase64 = string.Empty
        };
        var digest = ProviderAuthorizationVerifier.ComputeGrantDigest(unsigned);
        return CopyGrant(
            unsigned,
            grantDocumentSha256: digest,
            signatureBase64: Convert.ToBase64String(key.SignHash(
                Convert.FromHexString(digest),
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation)));
    }

    private static ProviderHarnessManifest SignHarness(ECDsa key, ProviderAuthorizationGrant grant)
    {
        var now = new DateTimeOffset(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);
        var unsigned = new ProviderHarnessManifest
        {
            ProviderId = "provider-1",
            TargetIdentitySha256 = grant.TargetIdentitySha256,
            ProtocolRevision = "2026-07-28",
            Authorization = grant,
            Runs =
            [
                new ProviderHarnessRunReceipt
                {
                    RunId = "run-1",
                    ResultSha256 = new string('b', 64),
                    ExecutionPlanSha256 = new string('c', 64),
                    StartedAtUtc = now.AddMinutes(-2),
                    EndedAtUtc = now.AddMinutes(-1),
                    CoreConformance = CoreConformanceStatus.Conformant,
                    SecurityAssurance = SecurityAssuranceStatus.Verified,
                    OperationalReadiness = OperationalReadinessStatus.Ready
                }
            ],
            SigningKeyId = grant.SigningKeyId,
            ManifestDocumentSha256 = string.Empty,
            SignatureBase64 = string.Empty
        };
        var digest = ProviderAuthorizationVerifier.ComputeManifestDigest(unsigned);
        return new ProviderHarnessManifest
        {
            ProviderId = unsigned.ProviderId,
            TargetIdentitySha256 = unsigned.TargetIdentitySha256,
            ProtocolRevision = unsigned.ProtocolRevision,
            Authorization = unsigned.Authorization,
            RequiredRunCount = unsigned.RequiredRunCount,
            MinimumConsistency = unsigned.MinimumConsistency,
            Runs = unsigned.Runs,
            SignatureAlgorithm = unsigned.SignatureAlgorithm,
            SigningKeyId = unsigned.SigningKeyId,
            ManifestDocumentSha256 = digest,
            SignatureBase64 = Convert.ToBase64String(key.SignHash(
                Convert.FromHexString(digest),
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
        };
    }

    private static ProviderHarnessManifest CopyHarness(
        ProviderHarnessManifest source,
        IReadOnlyList<ProviderHarnessRunReceipt> runs) => new()
    {
        ProviderId = source.ProviderId,
        TargetIdentitySha256 = source.TargetIdentitySha256,
        ProtocolRevision = source.ProtocolRevision,
        Authorization = source.Authorization,
        RequiredRunCount = source.RequiredRunCount,
        MinimumConsistency = source.MinimumConsistency,
        Runs = runs,
        SignatureAlgorithm = source.SignatureAlgorithm,
        SigningKeyId = source.SigningKeyId,
        ManifestDocumentSha256 = source.ManifestDocumentSha256,
        SignatureBase64 = source.SignatureBase64
    };

    private static ProviderAuthorizationGrant CopyGrant(
        ProviderAuthorizationGrant source,
        string? targetIdentitySha256 = null,
        string? grantDocumentSha256 = null,
        string? signatureBase64 = null) => new()
    {
        GrantId = source.GrantId,
        Authority = source.Authority,
        TargetIdentitySha256 = targetIdentitySha256 ?? source.TargetIdentitySha256,
        ValidFromUtc = source.ValidFromUtc,
        ExpiresAtUtc = source.ExpiresAtUtc,
        AllowedExecutionModes = source.AllowedExecutionModes,
        AllowedProbeClasses = source.AllowedProbeClasses,
        GrantDocumentSha256 = grantDocumentSha256 ?? source.GrantDocumentSha256,
        SignatureAlgorithm = source.SignatureAlgorithm,
        SigningKeyId = source.SigningKeyId,
        SignatureBase64 = signatureBase64 ?? source.SignatureBase64
    };
}