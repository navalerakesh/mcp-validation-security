using Mcp.Benchmark.CLI.Models;
using Mcp.Benchmark.CLI.Services;
using Mcp.Benchmark.CLI.Utilities;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Constants;

namespace Mcp.Benchmark.Tests.Unit.Services;

public sealed class ExecutionGovernanceResolutionTests
{
    private readonly ExecutionGovernanceService _service = new();

    [Theory]
    [InlineData("http://127.0.0.1/mcp")]
    [InlineData("http://localhost/mcp")]
    [InlineData("http://[::ffff:127.0.0.1]/mcp")]
    public async Task ValidateTargetResolutionAsync_RestrictedTarget_ReturnsError(string target)
    {
        var errors = await _service.ValidateTargetResolutionAsync(CreatePlan(target));

        errors.Should().ContainSingle();
        errors[0].Should().Contain("private, local, or reserved address");
    }

    [Fact]
    public async Task ValidateTargetResolutionAsync_PublicLiteral_ReturnsNoErrors()
    {
        var errors = await _service.ValidateTargetResolutionAsync(CreatePlan("https://1.1.1.1/mcp"));

        errors.Should().BeEmpty();
    }

    [Fact]
    public async Task ValidateTargetResolutionAsync_ExplicitPrivateOverride_ReturnsNoErrors()
    {
        var errors = await _service.ValidateTargetResolutionAsync(
            CreatePlan("http://127.0.0.1/mcp", allowPrivateAddresses: true));

        errors.Should().BeEmpty();
    }

    [Fact]
    public void BuildCommandPlan_CustomTargetPort_ShouldCreateExactAllowedOrigin()
    {
        var plan = _service.BuildCommandPlan(
            new CliSessionContext("session-test"),
            "health-check",
            new McpServerConfig { Endpoint = "https://example.test:8443/mcp", Transport = "http" },
            new ExecutionPolicy(),
            outputDirectory: null,
            plannedChecks: ["health-check"],
            plannedArtifacts: []);

        plan.IsValid.Should().BeTrue();
        plan.AllowedHosts.Should().Equal("example.test");
        plan.AllowedOrigins.Should().Equal("https://example.test:8443");
    }

    [Fact]
    public void BuildCommandPlan_TargetWithUrlSecrets_ShouldPersistOnlyRedactedIdentity()
    {
        var plan = _service.BuildCommandPlan(
            new CliSessionContext("session-test"),
            "health-check",
            new McpServerConfig
            {
                Endpoint = "https://example.test/mcp?access_token=secret#fragment",
                Transport = "http"
            },
            new ExecutionPolicy(),
            outputDirectory: null,
            plannedChecks: ["health-check"],
            plannedArtifacts: []);

        plan.Target.Should().Be("https://example.test/mcp");
        plan.TargetDigest.Should().Be(ArtifactDigestUtility.ComputeText(plan.Target));
        plan.ConfigDigest.Should().NotBe(ArtifactDigestUtility.ComputeText("secret"));
    }

    [Theory]
    [InlineData(McpProtocolEraSelection.Auto, "latest", "2026-07-28", McpProtocolEra.Modern, true)]
    [InlineData(McpProtocolEraSelection.Modern, "latest", "2026-07-28", McpProtocolEra.Modern, true)]
    [InlineData(McpProtocolEraSelection.Legacy, "latest", "2025-11-25", McpProtocolEra.Legacy, true)]
    [InlineData(McpProtocolEraSelection.Modern, "2025-11-25", "2025-11-25", McpProtocolEra.Legacy, false)]
    [InlineData(McpProtocolEraSelection.Legacy, "2026-07-28", "2026-07-28", McpProtocolEra.Modern, false)]
    public void BuildCommandPlan_ProtocolEraSelection_ShouldResolveOrRejectConflicts(
        McpProtocolEraSelection selection,
        string requestedProfile,
        string expectedVersion,
        McpProtocolEra expectedEra,
        bool expectedValid)
    {
        var plan = _service.BuildCommandPlan(
            new CliSessionContext("session-test"),
            "health-check",
            new McpServerConfig
            {
                Endpoint = "https://example.test/mcp",
                Transport = "http",
                ProtocolVersion = requestedProfile,
                ProtocolEra = selection
            },
            new ExecutionPolicy(),
            outputDirectory: null,
            plannedChecks: ["health-check"],
            plannedArtifacts: []);

        plan.ResolvedSchemaVersion.Should().Be(expectedVersion);
        plan.ProtocolEra.Should().Be(expectedEra);
        plan.ProtocolEraSelection.Should().Be(selection);
        plan.IsValid.Should().Be(expectedValid);
    }

    [Fact]
    public void BuildCommandPlan_InvalidLimitsAndOrigin_ShouldReturnPlanErrors()
    {
        var policy = new ExecutionPolicy
        {
            MaxRequests = ExecutionPolicyDefaults.MaximumRequests + 1,
            MaxConcurrency = 129,
            TimeoutSeconds = int.MaxValue,
            AllowedOrigins = ["not-an-origin"]
        };

        var plan = _service.BuildCommandPlan(
            new CliSessionContext("session-test"),
            "health-check",
            new McpServerConfig { Endpoint = "https://example.test/mcp", Transport = "http" },
            policy,
            outputDirectory: null,
            plannedChecks: ["health-check"],
            plannedArtifacts: []);

        plan.IsValid.Should().BeFalse();
        plan.ValidationErrors.Should().Contain(error => error.Contains("maxRequests", StringComparison.Ordinal));
        plan.ValidationErrors.Should().Contain(error => error.Contains("maxConcurrency", StringComparison.Ordinal));
        plan.ValidationErrors.Should().Contain(error => error.Contains("timeoutSeconds", StringComparison.Ordinal));
        plan.ValidationErrors.Should().Contain(error => error.Contains("allowedOrigins", StringComparison.Ordinal));
    }

    private static ExecutionPlan CreatePlan(string target, bool allowPrivateAddresses = false) => new()
    {
        Target = target,
        Transport = "http",
        AllowPrivateAddresses = allowPrivateAddresses
    };
}