using Mcp.Benchmark.CLI.Models;
using Mcp.Benchmark.CLI.Services;
using Mcp.Benchmark.CLI.Utilities;
using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.Tests.Unit.Services;

public sealed class ExecutionGovernanceDigestTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"mcpval-audit-{Guid.NewGuid():N}");

    [Fact]
    public void BuildAuditManifest_ShouldDigestFinalArtifactBytesAndVersionedCatalogs()
    {
        Directory.CreateDirectory(_directory);
        var artifactPath = Path.Combine(_directory, "result.json");
        File.WriteAllText(artifactPath, "{\"final\":true}");
        var plan = new ExecutionPlan
        {
            SessionId = "session-1",
            Target = "https://example.test/mcp",
            ValidatorDigest = new string('1', 64),
            ConfigDigest = new string('2', 64),
            TargetDigest = new string('3', 64)
        };
        var result = new ValidationResult
        {
            ValidationId = "validation-1",
            VerdictAssessment = new VerdictAssessment { Policy = DecisionPolicyManifest.CreateCurrent() },
            ClientCompatibility = new ClientCompatibilityReport { RequestedProfiles = ["claude-code"] }
        };
        result.Evidence.AppliedPacks.Add(new ValidationPackDescriptor
        {
            Key = new ValidationDescriptorKey("client-profile-pack/built-in"),
            Kind = ValidationPackKind.ClientProfilePack,
            Revision = new ValidationRevision("2026-04"),
            DisplayName = "Built-in client profiles",
            Stability = ValidationStability.Stable
        });

        var manifest = new ExecutionGovernanceService().BuildAuditManifest(plan, result, [artifactPath], null);

        manifest.Digests.ArtifactsSha256.Should().Contain("result.json", ArtifactDigestUtility.ComputeFile(artifactPath));
        manifest.Digests.RuleCatalogSha256.Should().MatchRegex("^[a-f0-9]{64}$");
        manifest.Digests.ProfileCatalogSha256.Should().Be(ArtifactDigestUtility.ComputeText(
            "client-profile-pack/built-in=2026-04\nprofile=claude-code"));
        manifest.Digests.ValidatorSha256.Should().Be(plan.ValidatorDigest);
        manifest.ArtifactPaths.Should().Equal("result.json");
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
