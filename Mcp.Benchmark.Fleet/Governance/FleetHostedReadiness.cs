using System.Security.Cryptography;
using System.Text;

namespace Mcp.Benchmark.Fleet.Governance;

public enum FleetSensitiveSurface
{
    Authentication,
    Transport,
    Scoring,
    Schema,
    Release
}

public sealed record FleetTrustedSigner(string SubjectId, ECDsa PublicKey);

public sealed record FleetApproval(
    string OwnerId,
    FleetSensitiveSurface Surface,
    string ChangeDigest,
    DateTimeOffset ApprovedAt,
    string KeyId,
    string SignatureBase64);

public static class FleetApprovalPolicy
{
    public static bool HasOwnerApproval(
        IReadOnlyCollection<FleetApproval> approvals,
        FleetSensitiveSurface surface,
        string changeDigest,
        string ownerId,
        DateTimeOffset now,
        TimeSpan maximumAge,
        IReadOnlyDictionary<string, FleetTrustedSigner> trustedSigners)
    {
        if (maximumAge <= TimeSpan.Zero || maximumAge > TimeSpan.FromDays(30)) return false;
        return approvals
            .Where(approval => approval.Surface == surface)
            .Where(approval => string.Equals(approval.OwnerId, ownerId, StringComparison.Ordinal))
            .Where(approval => string.Equals(approval.ChangeDigest, changeDigest, StringComparison.Ordinal))
            .Where(approval => approval.ApprovedAt <= now && approval.ApprovedAt >= now.Subtract(maximumAge))
            .Where(approval => FleetEvidenceSignatures.VerifyApproval(approval, trustedSigners))
            .Any();
    }
}

public sealed record FleetExternalReviewEvidence
{
    public required string ReviewType { get; init; }
    public required string Provider { get; init; }
    public required string ReportDigest { get; init; }
    public required string BuildDigest { get; init; }
    public required string EnvironmentDigest { get; init; }
    public required DateTimeOffset CompletedAt { get; init; }
    public required DateTimeOffset ExpiresAt { get; init; }
    public bool BlockingFindingsOpen { get; init; }
    public required string KeyId { get; init; }
    public required string SignatureBase64 { get; init; }
}

public sealed record FleetDeploymentCapabilities
{
    public bool SharedDurableJobStore { get; init; }
    public bool SharedEncryptedArtifactStore { get; init; }
    public bool TenantIdentityProvider { get; init; }
    public bool TenantAuditSink { get; init; }
    public bool HttpWorkerIsolation { get; init; }
    public bool StdioWorkerIsolation { get; init; }
    public bool NetworkEgressEnforced { get; init; }
    public bool QueueBackpressureEnabled { get; init; }
    public bool BackupRestoreExercised { get; init; }
    public bool DisasterRecoveryExercised { get; init; }
    public bool OwnerGovernanceEnforced { get; init; }
}

public sealed record FleetDeploymentEvidence
{
    public required FleetDeploymentCapabilities Capabilities { get; init; }
    public required string BuildDigest { get; init; }
    public required string EnvironmentDigest { get; init; }
    public required string Issuer { get; init; }
    public required DateTimeOffset IssuedAt { get; init; }
    public required DateTimeOffset ExpiresAt { get; init; }
    public required string KeyId { get; init; }
    public required string SignatureBase64 { get; init; }
}

public static class FleetEvidenceSignatures
{
    public static FleetApproval SignApproval(
        string ownerId,
        FleetSensitiveSurface surface,
        string changeDigest,
        DateTimeOffset approvedAt,
        string keyId,
        ECDsa privateKey) =>
        new(ownerId, surface, changeDigest, approvedAt, keyId,
            Sign(ApprovalPayload(ownerId, surface, changeDigest, approvedAt, keyId), privateKey));

    public static FleetDeploymentEvidence SignDeployment(
        FleetDeploymentCapabilities capabilities,
        string buildDigest,
        string environmentDigest,
        string issuer,
        DateTimeOffset issuedAt,
        DateTimeOffset expiresAt,
        string keyId,
        ECDsa privateKey)
    {
        var unsigned = new FleetDeploymentEvidence
        {
            Capabilities = capabilities,
            BuildDigest = buildDigest,
            EnvironmentDigest = environmentDigest,
            Issuer = issuer,
            IssuedAt = issuedAt,
            ExpiresAt = expiresAt,
            KeyId = keyId,
            SignatureBase64 = string.Empty
        };
        return unsigned with { SignatureBase64 = Sign(DeploymentPayload(unsigned), privateKey) };
    }

    public static FleetExternalReviewEvidence SignReview(
        string reviewType,
        string provider,
        string reportDigest,
        string buildDigest,
        string environmentDigest,
        DateTimeOffset completedAt,
        DateTimeOffset expiresAt,
        bool blockingFindingsOpen,
        string keyId,
        ECDsa privateKey)
    {
        var unsigned = new FleetExternalReviewEvidence
        {
            ReviewType = reviewType,
            Provider = provider,
            ReportDigest = reportDigest,
            BuildDigest = buildDigest,
            EnvironmentDigest = environmentDigest,
            CompletedAt = completedAt,
            ExpiresAt = expiresAt,
            BlockingFindingsOpen = blockingFindingsOpen,
            KeyId = keyId,
            SignatureBase64 = string.Empty
        };
        return unsigned with { SignatureBase64 = Sign(ReviewPayload(unsigned), privateKey) };
    }

    internal static bool VerifyApproval(FleetApproval approval, IReadOnlyDictionary<string, FleetTrustedSigner> trustedSigners) =>
        Verify(approval.KeyId, approval.OwnerId, approval.SignatureBase64,
            ApprovalPayload(approval.OwnerId, approval.Surface, approval.ChangeDigest, approval.ApprovedAt, approval.KeyId), trustedSigners);

    internal static bool VerifyDeployment(FleetDeploymentEvidence evidence, IReadOnlyDictionary<string, FleetTrustedSigner> trustedSigners) =>
        Verify(evidence.KeyId, evidence.Issuer, evidence.SignatureBase64, DeploymentPayload(evidence), trustedSigners);

    internal static bool VerifyReview(FleetExternalReviewEvidence evidence, IReadOnlyDictionary<string, FleetTrustedSigner> trustedSigners) =>
        Verify(evidence.KeyId, evidence.Provider, evidence.SignatureBase64, ReviewPayload(evidence), trustedSigners);

    private static bool Verify(
        string keyId,
        string subjectId,
        string signatureBase64,
        byte[] payload,
        IReadOnlyDictionary<string, FleetTrustedSigner> trustedSigners)
    {
        if (!trustedSigners.TryGetValue(keyId, out var signer) || !string.Equals(signer.SubjectId, subjectId, StringComparison.Ordinal)) return false;
        try
        {
            return signer.PublicKey.VerifyData(payload, Convert.FromBase64String(signatureBase64), HashAlgorithmName.SHA256);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string Sign(byte[] payload, ECDsa privateKey) =>
        Convert.ToBase64String(privateKey.SignData(payload, HashAlgorithmName.SHA256));

    private static byte[] ApprovalPayload(string subject, FleetSensitiveSurface surface, string digest, DateTimeOffset at, string keyId) =>
        Encode("approval", subject, surface.ToString(), digest, at, keyId);

    private static byte[] DeploymentPayload(FleetDeploymentEvidence evidence) => Encode(
        "deployment", evidence.BuildDigest, evidence.EnvironmentDigest, evidence.Issuer, evidence.IssuedAt, evidence.ExpiresAt, evidence.KeyId,
        evidence.Capabilities.SharedDurableJobStore, evidence.Capabilities.SharedEncryptedArtifactStore,
        evidence.Capabilities.TenantIdentityProvider, evidence.Capabilities.TenantAuditSink,
        evidence.Capabilities.HttpWorkerIsolation, evidence.Capabilities.StdioWorkerIsolation,
        evidence.Capabilities.NetworkEgressEnforced, evidence.Capabilities.QueueBackpressureEnabled,
        evidence.Capabilities.BackupRestoreExercised, evidence.Capabilities.DisasterRecoveryExercised,
        evidence.Capabilities.OwnerGovernanceEnforced);

    private static byte[] ReviewPayload(FleetExternalReviewEvidence evidence) => Encode(
        "review", evidence.ReviewType, evidence.Provider, evidence.ReportDigest, evidence.BuildDigest,
        evidence.EnvironmentDigest, evidence.CompletedAt, evidence.ExpiresAt, evidence.BlockingFindingsOpen, evidence.KeyId);

    private static byte[] Encode(params object[] fields)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        foreach (var field in fields)
        {
            var value = field is DateTimeOffset timestamp ? timestamp.ToString("O") : field.ToString() ?? string.Empty;
            var bytes = Encoding.UTF8.GetBytes(value);
            writer.Write(bytes.Length);
            writer.Write(bytes);
        }
        writer.Flush();
        return stream.ToArray();
    }
}

public sealed record FleetReadinessDecision(bool Ready, IReadOnlyList<string> Blockers);

public static class FleetHostedReadiness
{
    public static FleetReadinessDecision Evaluate(
        FleetDeploymentEvidence deployment,
        IReadOnlyCollection<FleetExternalReviewEvidence> reviews,
        IReadOnlyCollection<FleetApproval> approvals,
        string ownerId,
        string expectedBuildDigest,
        string expectedEnvironmentDigest,
        DateTimeOffset now,
        IReadOnlyDictionary<string, FleetTrustedSigner> trustedSigners)
    {
        var blockers = new List<string>();
        var deploymentVerified = deployment.IssuedAt <= now && deployment.ExpiresAt > now &&
            string.Equals(deployment.BuildDigest, expectedBuildDigest, StringComparison.Ordinal) &&
            string.Equals(deployment.EnvironmentDigest, expectedEnvironmentDigest, StringComparison.Ordinal) &&
            FleetEvidenceSignatures.VerifyDeployment(deployment, trustedSigners);
        Require(deploymentVerified, "signed-deployment-evidence");
        var capabilities = deploymentVerified ? deployment.Capabilities : new FleetDeploymentCapabilities();
        Require(capabilities.SharedDurableJobStore, "shared-durable-job-store");
        Require(capabilities.SharedEncryptedArtifactStore, "shared-encrypted-artifact-store");
        Require(capabilities.TenantIdentityProvider, "tenant-identity-provider");
        Require(capabilities.TenantAuditSink, "tenant-audit-sink");
        Require(capabilities.HttpWorkerIsolation, "http-worker-isolation");
        Require(capabilities.StdioWorkerIsolation, "stdio-worker-isolation");
        Require(capabilities.NetworkEgressEnforced, "network-egress-enforcement");
        Require(capabilities.QueueBackpressureEnabled, "queue-backpressure");
        Require(capabilities.BackupRestoreExercised, "backup-restore-exercise");
        Require(capabilities.DisasterRecoveryExercised, "disaster-recovery-exercise");
        Require(capabilities.OwnerGovernanceEnforced, "owner-governance");
        RequireReview("threat-model");
        RequireReview("penetration-test");
        RequireReview("calibration-review");
        foreach (var surface in Enum.GetValues<FleetSensitiveSurface>())
        {
            if (!FleetApprovalPolicy.HasOwnerApproval(
                approvals,
                surface,
                expectedBuildDigest,
                ownerId,
                now,
                TimeSpan.FromDays(30),
                trustedSigners))
            {
                blockers.Add($"owner-approval-{surface.ToString().ToLowerInvariant()}");
            }
        }
        blockers.Add("hosted-enforcement-composition-not-implemented");
        return new FleetReadinessDecision(blockers.Count == 0, blockers);

        void Require(bool satisfied, string blocker)
        {
            if (!satisfied) blockers.Add(blocker);
        }

        void RequireReview(string reviewType)
        {
            var valid = reviews.Any(review =>
                string.Equals(review.ReviewType, reviewType, StringComparison.Ordinal) &&
                string.Equals(review.BuildDigest, expectedBuildDigest, StringComparison.Ordinal) &&
                string.Equals(review.EnvironmentDigest, expectedEnvironmentDigest, StringComparison.Ordinal) &&
                review.CompletedAt <= now && review.ExpiresAt > now && !review.BlockingFindingsOpen &&
                FleetEvidenceSignatures.VerifyReview(review, trustedSigners));
            if (!valid) blockers.Add($"external-{reviewType}");
        }
    }
}

public sealed record FleetSloPolicy(
    double MinimumAvailability,
    TimeSpan MaximumQueueP95,
    double MaximumErrorRate,
    TimeSpan MaximumRecoveryPoint,
    TimeSpan MaximumRecoveryTime);

public sealed record FleetSloSnapshot(
    double Availability,
    TimeSpan QueueP95,
    double ErrorRate,
    TimeSpan RecoveryPoint,
    TimeSpan RecoveryTime);

public static class FleetSloEvaluator
{
    public static IReadOnlyList<string> Evaluate(FleetSloPolicy policy, FleetSloSnapshot snapshot)
    {
        var breaches = new List<string>();
        if (!double.IsFinite(policy.MinimumAvailability) || !double.IsFinite(policy.MaximumErrorRate) ||
            policy.MinimumAvailability is < 0 or > 1 || policy.MaximumErrorRate is < 0 or > 1 ||
            policy.MaximumQueueP95 <= TimeSpan.Zero || policy.MaximumRecoveryPoint <= TimeSpan.Zero || policy.MaximumRecoveryTime <= TimeSpan.Zero)
        {
            breaches.Add("invalid-policy");
            return breaches;
        }
        if (!double.IsFinite(snapshot.Availability) || !double.IsFinite(snapshot.ErrorRate) ||
            snapshot.Availability is < 0 or > 1 || snapshot.ErrorRate is < 0 or > 1 ||
            snapshot.QueueP95 < TimeSpan.Zero || snapshot.RecoveryPoint < TimeSpan.Zero || snapshot.RecoveryTime < TimeSpan.Zero)
        {
            breaches.Add("invalid-snapshot");
            return breaches;
        }
        if (snapshot.Availability < policy.MinimumAvailability) breaches.Add("availability");
        if (snapshot.QueueP95 > policy.MaximumQueueP95) breaches.Add("queue-p95");
        if (snapshot.ErrorRate > policy.MaximumErrorRate) breaches.Add("error-rate");
        if (snapshot.RecoveryPoint > policy.MaximumRecoveryPoint) breaches.Add("recovery-point");
        if (snapshot.RecoveryTime > policy.MaximumRecoveryTime) breaches.Add("recovery-time");
        return breaches;
    }
}