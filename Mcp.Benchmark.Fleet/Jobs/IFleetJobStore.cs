namespace Mcp.Benchmark.Fleet.Jobs;

using Mcp.Benchmark.Fleet.Governance;

public interface IFleetJobStore
{
    Task<FleetJob> CreateOrGetAsync(FleetJob candidate, FleetPrincipal principal, CancellationToken cancellationToken = default);
    Task<FleetJob?> GetAsync(FleetPrincipal principal, string jobId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<FleetJob>> ListEligibleAsync(
        FleetPrincipal principal,
        FleetTransport transport,
        DateTimeOffset now,
        int limit,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<FleetJobMutationAudit>> ReadAuditAsync(FleetPrincipal principal, CancellationToken cancellationToken = default);
    Task<FleetJob> RequestCancellationAsync(
        string jobId,
        long expectedVersion,
        FleetPrincipal principal,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);
    Task<FleetJob> CompareExchangeAsync(
        FleetJob next,
        long expectedVersion,
        FleetMutationContext context,
        CancellationToken cancellationToken = default);
}

public sealed record FleetMutationContext(FleetPrincipal Principal, string? LeaseOwner, DateTimeOffset Now);

public sealed record FleetJobMutationAudit(
    long Sequence,
    string SubjectId,
    string JobId,
    string Action,
    long FromVersion,
    long ToVersion,
    DateTimeOffset Timestamp);

public sealed class FleetIdempotencyConflictException(string message) : InvalidOperationException(message);

public sealed class FleetConcurrencyException(string message) : InvalidOperationException(message);

public sealed class FleetInvariantException(string message) : InvalidOperationException(message);