using System.Security.Cryptography;
using Mcp.Benchmark.CLI.Utilities;

namespace Mcp.Benchmark.Tests.Unit.Utilities;

public sealed class ValidationAttestationSignerTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"mcpval-attestation-{Guid.NewGuid():N}.json");

    [Fact]
    public void Sign_ShouldVerifyFinalBytesAndRejectTampering()
    {
        File.WriteAllText(_path, "{\"result\":true}");
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        var attestation = ValidationAttestationSigner.Sign("validation-1", _path, key.ExportECPrivateKeyPem());

        ValidationAttestationSigner.Verify(attestation, File.ReadAllBytes(_path)).Should().BeTrue();
        ValidationAttestationSigner.Verify(attestation, "{\"result\":false}"u8).Should().BeFalse();
        attestation.SubjectSha256.Should().MatchRegex("^[a-f0-9]{64}$");
        attestation.KeyId.Should().MatchRegex("^[a-f0-9]{64}$");
    }

    [Fact]
    public void Sign_WithP384Key_ShouldRejectMislabeledAlgorithm()
    {
        File.WriteAllText(_path, "{}");
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP384);

        var action = () => ValidationAttestationSigner.Sign("validation-1", _path, key.ExportECPrivateKeyPem());

        action.Should().Throw<ArgumentException>().WithMessage("*P-256*");
    }

    [Fact]
    public void ValidatePrivateKey_WithP256PrivateKey_ShouldSucceed()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        var validate = () => ValidationAttestationSigner.ValidatePrivateKey(key.ExportECPrivateKeyPem());

        validate.Should().NotThrow();
    }

    [Fact]
    public void ValidatePrivateKey_WithPublicOnlyOrWrongCurve_ShouldReject()
    {
        using var p256 = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var p384 = ECDsa.Create(ECCurve.NamedCurves.nistP384);

        var publicOnly = () => ValidationAttestationSigner.ValidatePrivateKey(p256.ExportSubjectPublicKeyInfoPem());
        var wrongCurve = () => ValidationAttestationSigner.ValidatePrivateKey(p384.ExportECPrivateKeyPem());

        publicOnly.Should().Throw<ArgumentException>();
        wrongCurve.Should().Throw<ArgumentException>();
    }

    public void Dispose()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }
}
