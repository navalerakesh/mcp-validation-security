namespace Mcp.Benchmark.Fleet.Jobs;

public enum FleetJobState
{
    Queued,
    Leased,
    Running,
    CancelRequested,
    Succeeded,
    Failed,
    Cancelled,
    DeadLettered
}

public enum FleetTransport
{
    Http,
    Stdio
}

public sealed record FleetJob
{
    public required string JobId { get; init; }
    public required string TenantId { get; init; }
    public required string IdempotencyKeyHash { get; init; }
    public required string RequestDigest { get; init; }
    public required FleetTransport Transport { get; init; }
    public FleetJobState State { get; init; } = FleetJobState.Queued;
    public long Version { get; init; } = 1;
    public int Attempt { get; init; }
    public int MaxAttempts { get; init; } = 3;
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public DateTimeOffset NextAttemptAt { get; init; }
    public string? LeaseOwner { get; init; }
    public DateTimeOffset? LeaseExpiresAt { get; init; }
    public string? ResultDigest { get; init; }
    public string? FailureCode { get; init; }
}