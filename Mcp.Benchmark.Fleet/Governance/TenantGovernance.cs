using System.Security.Cryptography;
using System.Text;

namespace Mcp.Benchmark.Fleet.Governance;

public enum FleetPermission
{
    SubmitJob,
    ReadJob,
    CancelJob,
    OperateWorker,
    ReadAudit,
    ManageTenant
}

public enum FleetRole
{
    Submitter,
    Reader,
    Operator,
    Auditor,
    TenantAdmin
}

public sealed record FleetPrincipal(string SubjectId, string TenantId, IReadOnlySet<FleetRole> Roles);

public static class FleetAuthorization
{
    private static readonly IReadOnlyDictionary<FleetRole, IReadOnlySet<FleetPermission>> Grants =
        new Dictionary<FleetRole, IReadOnlySet<FleetPermission>>
        {
            [FleetRole.Submitter] = new HashSet<FleetPermission> { FleetPermission.SubmitJob, FleetPermission.ReadJob, FleetPermission.CancelJob },
            [FleetRole.Reader] = new HashSet<FleetPermission> { FleetPermission.ReadJob },
            [FleetRole.Operator] = new HashSet<FleetPermission> { FleetPermission.ReadJob, FleetPermission.CancelJob, FleetPermission.OperateWorker },
            [FleetRole.Auditor] = new HashSet<FleetPermission> { FleetPermission.ReadJob, FleetPermission.ReadAudit },
            [FleetRole.TenantAdmin] = Enum.GetValues<FleetPermission>().ToHashSet()
        };

    public static void Demand(FleetPrincipal principal, string resourceTenantId, FleetPermission permission)
    {
        if (!string.Equals(principal.TenantId, resourceTenantId, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException("Cross-tenant access is forbidden.");
        }
        if (!principal.Roles.Any(role => Grants.TryGetValue(role, out var permissions) && permissions.Contains(permission)))
        {
            throw new UnauthorizedAccessException($"Permission '{permission}' is required.");
        }
    }
}

public sealed record TenantQuotaPolicy
{
    public int MaximumQueuedJobs { get; init; } = 100;
    public int MaximumActiveJobs { get; init; } = 10;
    public int MaximumDailyRequests { get; init; } = 10_000;
    public long MaximumStoredBytes { get; init; } = 1_073_741_824;
    public decimal MaximumDailyCost { get; init; } = 100;
}

public sealed record TenantUsage(int QueuedJobs, int ActiveJobs, int DailyRequests, long StoredBytes, decimal DailyCost);

public sealed record QuotaDecision(bool Allowed, string? Code);

public static class TenantQuotaEvaluator
{
    public static QuotaDecision Evaluate(TenantQuotaPolicy policy, TenantUsage usage, int requestedBudget, long reservedBytes, decimal reservedCost)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(usage);
        if (policy.MaximumQueuedJobs < 1 || policy.MaximumActiveJobs < 1 || policy.MaximumDailyRequests < 1 ||
            policy.MaximumStoredBytes < 1 || policy.MaximumDailyCost <= 0 || usage.QueuedJobs < 0 || usage.ActiveJobs < 0 ||
            usage.DailyRequests < 0 || usage.StoredBytes < 0 || usage.DailyCost < 0 || requestedBudget < 1 ||
            reservedBytes < 0 || reservedCost < 0) return new(false, "invalid-reservation");
        if (usage.QueuedJobs >= policy.MaximumQueuedJobs) return new(false, "queued-jobs");
        if (usage.ActiveJobs >= policy.MaximumActiveJobs) return new(false, "active-jobs");
        if (requestedBudget > policy.MaximumDailyRequests - (long)usage.DailyRequests) return new(false, "daily-requests");
        if (usage.StoredBytes > policy.MaximumStoredBytes - reservedBytes) return new(false, "stored-bytes");
        if (usage.DailyCost > policy.MaximumDailyCost - reservedCost) return new(false, "daily-cost");
        return new(true, null);
    }
}

public sealed record FleetArtifactRetention
{
    public required string TenantId { get; init; }
    public required string ArtifactId { get; init; }
    public required DateTimeOffset DeleteAfter { get; init; }
    public bool LegalHold { get; init; }
}

public static class FleetRetentionPolicy
{
    public static bool CanDelete(FleetArtifactRetention artifact, FleetPrincipal principal, DateTimeOffset now)
    {
        FleetAuthorization.Demand(principal, artifact.TenantId, FleetPermission.ManageTenant);
        return !artifact.LegalHold && artifact.DeleteAfter <= now;
    }
}

public sealed record FleetAuditRecord
{
    public required long Sequence { get; init; }
    public required string TenantId { get; init; }
    public required string SubjectId { get; init; }
    public required string Action { get; init; }
    public required string ResourceDigest { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
    public required string KeyId { get; init; }
    public required string PreviousHash { get; init; }
    public required string RecordHash { get; init; }
}

public sealed record FleetAuditCheckpoint(long Sequence, string HeadHash, string KeyId, string CheckpointMac);

public static class FleetAuditChain
{
    public const string GenesisHash = "0000000000000000000000000000000000000000000000000000000000000000";

    public static FleetAuditRecord Append(
        FleetAuditRecord? previous,
        string tenantId,
        string subjectId,
        string action,
        string resourceDigest,
        DateTimeOffset timestamp,
        string keyId,
        ReadOnlySpan<byte> authenticationKey)
    {
        if (authenticationKey.Length < 32) throw new ArgumentException("Audit authentication keys require at least 256 bits.", nameof(authenticationKey));
        var sequence = previous?.Sequence + 1 ?? 1;
        var previousHash = previous?.RecordHash ?? GenesisHash;
        var payload = Encode(sequence, tenantId, subjectId, action, resourceDigest, timestamp, keyId, previousHash);
        return new FleetAuditRecord
        {
            Sequence = sequence,
            TenantId = tenantId,
            SubjectId = subjectId,
            Action = action,
            ResourceDigest = resourceDigest,
            Timestamp = timestamp,
            KeyId = keyId,
            PreviousHash = previousHash,
            RecordHash = Convert.ToHexString(HMACSHA256.HashData(authenticationKey, payload)).ToLowerInvariant()
        };
    }

    public static FleetAuditCheckpoint CreateCheckpoint(FleetAuditRecord head, string keyId, ReadOnlySpan<byte> authenticationKey)
    {
        if (authenticationKey.Length < 32) throw new ArgumentException("Audit authentication keys require at least 256 bits.", nameof(authenticationKey));
        var payload = Encode(head.Sequence, head.RecordHash, keyId);
        return new FleetAuditCheckpoint(
            head.Sequence,
            head.RecordHash,
            keyId,
            Convert.ToHexString(HMACSHA256.HashData(authenticationKey, payload)).ToLowerInvariant());
    }

    public static bool Verify(
        IReadOnlyList<FleetAuditRecord> records,
        IReadOnlyDictionary<string, byte[]> trustedKeys,
        FleetAuditCheckpoint checkpoint)
    {
        if (records.Count == 0 || records.Count != checkpoint.Sequence ||
            !trustedKeys.TryGetValue(checkpoint.KeyId, out var checkpointKey)) return false;
        var expectedCheckpoint = CreateCheckpoint(records[^1], checkpoint.KeyId, checkpointKey);
        if (checkpoint != expectedCheckpoint) return false;
        FleetAuditRecord? previous = null;
        foreach (var record in records)
        {
            if (!trustedKeys.TryGetValue(record.KeyId, out var key)) return false;
            var expected = Append(previous, record.TenantId, record.SubjectId, record.Action, record.ResourceDigest, record.Timestamp, record.KeyId, key);
            if (record != expected) return false;
            previous = record;
        }
        return true;
    }

    private static byte[] Encode(long sequence, params object[] fields)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(sequence);
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