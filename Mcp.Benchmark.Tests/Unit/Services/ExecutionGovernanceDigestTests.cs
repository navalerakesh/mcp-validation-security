using Mcp.Benchmark.CLI.Models;
using Mcp.Benchmark.CLI.Services;
using Mcp.Benchmark.CLI.Utilities;
using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.Tests.Unit.Services;

public sealed class ExecutionGovernanceDigestTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"mcpval-audit-{Guid.NewGuid():N}");

    [Theory]
    [InlineData("http", "https://example.test/mcp?access_token=secret#fragment", "https://example.test/mcp")]
    [InlineData("stdio", "node /tmp/owned-server.cjs", "node /tmp/owned-server.cjs")]
    public void CapturedRunTarget_ShouldMatchPlanAndRemainImmutable(string transport, string endpoint, string digestInput)
    {
        var configuration = new McpValidatorConfiguration { Server = new McpServerConfig { Transport = transport, Endpoint = endpoint } };
        var request = ValidationRunRequest.Capture(configuration, "admitted-run");
        var plan = new ExecutionGovernanceService().BuildCommandPlan(new CliSessionContext("session-test"), "validate",
            configuration.Server, configuration.Execution, outputDirectory: null, plannedChecks: [], plannedArtifacts: []);

        configuration.Server.Endpoint = "https://changed.invalid/mcp";
        request.CreateConfiguration().Server.Endpoint = "https://mutated-copy.invalid/mcp";

        request.TargetIdentitySha256.Should().Be(ArtifactDigestUtility.ComputeText(digestInput));
        request.TargetIdentitySha256.Should().Be(plan.TargetDigest);
        request.CreateConfiguration().Server.Endpoint.Should().Be(endpoint);
    }

    [Theory]
    [InlineData(ValidationStatus.Error, ValidationOutcome.Error, 70)]
    [InlineData(ValidationStatus.Cancelled, ValidationOutcome.Cancelled, 124)]
    [InlineData(ValidationStatus.InProgress, null, 2)]
    public void BuildAuditManifest_ShouldRetainRecordedTermination(ValidationStatus status, ValidationOutcome? outcome, int exit)
    {
        var result = new ValidationResult { OverallStatus = status };
        var manifest = new ExecutionGovernanceService().BuildAuditManifest(new ExecutionPlan(), result, [], null);

        manifest.RunTerminationOutcome.Should().Be(outcome);
        manifest.RecommendedExitCode.Should().Be(exit);
    }

    [Fact]
    public void BuildAuditManifest_PlanOnly_ShouldNotInventExecutionOutcome()
    {
        var manifest = new ExecutionGovernanceService().BuildAuditManifest(new ExecutionPlan(), null, [], null);

        manifest.RunTerminationOutcome.Should().BeNull();
        manifest.RecommendedExitCode.Should().BeNull();
    }

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
            ClientCompatibility = new ClientCompatibilityReport { RequestedProfiles = ["claude-code"] }
        };
        result.Assessments.OfficialConformance = new OfficialConformanceAssessment
        {
            VerificationStatus = OfficialConformanceVerificationStatus.Verified,
            SourceEvidenceSha256 = new string('4', 64),
            Runner = new OfficialConformanceRunnerIdentity
            {
                PackageName = "@modelcontextprotocol/conformance",
                Version = "0.2.0-alpha.12",
                SourceCommit = new string('5', 40),
                PackageSha256 = new string('6', 64)
            },
            RequirementSet = new OfficialConformanceRequirementSet
            {
                Revision = "2026-07-28",
                Sha256 = new string('7', 64)
            },
            Target = new OfficialConformanceTargetIdentity
            {
                IdentitySha256 = plan.TargetDigest,
                Transport = "http",
                ProtocolVersion = "2026-07-28"
            }
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
        manifest.Digests.ExternalEvidenceSha256.Should().Contain(
            "official-conformance",
            result.Assessments.OfficialConformance.SourceEvidenceSha256);
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
