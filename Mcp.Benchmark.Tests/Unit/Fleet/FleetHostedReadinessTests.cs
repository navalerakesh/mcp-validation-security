using Mcp.Benchmark.Fleet.Governance;
using System.Security.Cryptography;

namespace Mcp.Benchmark.Tests.Unit.Fleet;

public sealed class FleetHostedReadinessTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Readiness_ShouldFailClosedUntilEveryDeploymentAndExternalGatePasses()
    {
        using var authority = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var reviewer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var ownerKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var trusted = new Dictionary<string, FleetTrustedSigner>
        {
            ["deployment-key"] = new("deployment-authority", authority),
            ["review-key"] = new("independent-provider", reviewer),
            ["owner-key"] = new("navalerakesh", ownerKey)
        };
        var approvals = SignAllApprovals(ownerKey);
        var empty = Evaluate(SignDeployment(new FleetDeploymentCapabilities(), authority), [], approvals, trusted);
        var reviews = new[]
        {
            SignReview("threat-model", reviewer),
            SignReview("penetration-test", reviewer),
            SignReview("calibration-review", reviewer)
        };
        var deployment = SignDeployment(AllCapabilities(), authority);
        var ready = Evaluate(deployment, reviews, approvals, trusted);
        var tampered = Evaluate(deployment with { EnvironmentDigest = OtherDigest }, reviews, approvals, trusted);

        empty.Ready.Should().BeFalse();
        empty.Blockers.Should().Contain("network-egress-enforcement");
        ready.Ready.Should().BeFalse();
        ready.Blockers.Should().ContainSingle("hosted-enforcement-composition-not-implemented");
        tampered.Ready.Should().BeFalse();
        tampered.Blockers.Should().Contain("signed-deployment-evidence");
    }

    [Fact]
    public void SensitiveChanges_ShouldRequireCurrentTrustedOwnerApproval()
    {
        const string digest = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        using var ownerKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var otherKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var trusted = new Dictionary<string, FleetTrustedSigner>
        {
            ["owner-key"] = new("navalerakesh", ownerKey),
            ["other-key"] = new("other", otherKey)
        };
        var ownerApprovals = new[]
        {
            FleetEvidenceSignatures.SignApproval("navalerakesh", FleetSensitiveSurface.Transport, digest, Now, "owner-key", ownerKey)
        };
        var nonOwnerApprovals = new[]
        {
            FleetEvidenceSignatures.SignApproval("other", FleetSensitiveSurface.Transport, digest, Now, "other-key", otherKey)
        };

        FleetApprovalPolicy.HasOwnerApproval(
            ownerApprovals, FleetSensitiveSurface.Transport, digest, "navalerakesh", Now, TimeSpan.FromDays(7), trusted).Should().BeTrue();
        FleetApprovalPolicy.HasOwnerApproval(
            nonOwnerApprovals, FleetSensitiveSurface.Transport, digest, "navalerakesh", Now, TimeSpan.FromDays(7), trusted).Should().BeFalse();
        FleetApprovalPolicy.HasOwnerApproval(
            ownerApprovals.Select(approval => approval with { ChangeDigest = OtherDigest }).ToArray(),
            FleetSensitiveSurface.Transport, OtherDigest, "navalerakesh", Now, TimeSpan.FromDays(7), trusted).Should().BeFalse();
    }

    [Fact]
    public void SloEvaluator_ShouldNameEachBreachedOperationalObjective()
    {
        var breaches = FleetSloEvaluator.Evaluate(
            new FleetSloPolicy(0.999, TimeSpan.FromSeconds(30), 0.01, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30)),
            new FleetSloSnapshot(0.99, TimeSpan.FromMinutes(1), 0.02, TimeSpan.FromMinutes(10), TimeSpan.FromHours(1)));

        breaches.Should().BeEquivalentTo("availability", "queue-p95", "error-rate", "recovery-point", "recovery-time");
        FleetSloEvaluator.Evaluate(
            new FleetSloPolicy(0.999, TimeSpan.FromSeconds(30), 0.01, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30)),
            new FleetSloSnapshot(double.NaN, TimeSpan.Zero, 0, TimeSpan.Zero, TimeSpan.Zero))
            .Should().ContainSingle("invalid-snapshot");
        FleetSloEvaluator.Evaluate(
            new FleetSloPolicy(-1, TimeSpan.Zero, 2, TimeSpan.Zero, TimeSpan.Zero),
            new FleetSloSnapshot(1, TimeSpan.Zero, 0, TimeSpan.Zero, TimeSpan.Zero))
            .Should().ContainSingle("invalid-policy");
    }

    private static FleetDeploymentCapabilities AllCapabilities() => new()
    {
        SharedDurableJobStore = true,
        SharedEncryptedArtifactStore = true,
        TenantIdentityProvider = true,
        TenantAuditSink = true,
        HttpWorkerIsolation = true,
        StdioWorkerIsolation = true,
        NetworkEgressEnforced = true,
        QueueBackpressureEnabled = true,
        BackupRestoreExercised = true,
        DisasterRecoveryExercised = true,
        OwnerGovernanceEnforced = true
    };

    private const string BuildDigest = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string EnvironmentDigest = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private const string OtherDigest = "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc";

    private static FleetReadinessDecision Evaluate(
        FleetDeploymentEvidence deployment,
        IReadOnlyCollection<FleetExternalReviewEvidence> reviews,
        IReadOnlyCollection<FleetApproval> approvals,
        IReadOnlyDictionary<string, FleetTrustedSigner> trusted) =>
        FleetHostedReadiness.Evaluate(deployment, reviews, approvals, "navalerakesh", BuildDigest, EnvironmentDigest, Now, trusted);

    private static FleetDeploymentEvidence SignDeployment(FleetDeploymentCapabilities capabilities, ECDsa key) =>
        FleetEvidenceSignatures.SignDeployment(
            capabilities, BuildDigest, EnvironmentDigest, "deployment-authority",
            Now.AddMinutes(-1), Now.AddDays(1), "deployment-key", key);

    private static FleetExternalReviewEvidence SignReview(string type, ECDsa key) =>
        FleetEvidenceSignatures.SignReview(
            type, "independent-provider", BuildDigest, BuildDigest, EnvironmentDigest,
            Now.AddDays(-1), Now.AddYears(1), blockingFindingsOpen: false, "review-key", key);

    private static FleetApproval[] SignAllApprovals(ECDsa ownerKey) =>
        Enum.GetValues<FleetSensitiveSurface>()
            .Select(surface => FleetEvidenceSignatures.SignApproval("navalerakesh", surface, BuildDigest, Now, "owner-key", ownerKey))
            .ToArray();
}