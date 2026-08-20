using Mcp.Benchmark.Fleet.Jobs;

namespace Mcp.Benchmark.Fleet.Scheduling;

public sealed record FleetQueueCandidate(FleetJob Job, string TargetDigest, DateTimeOffset EnqueuedAt);

public sealed record FleetSchedulingState(
    IReadOnlyDictionary<string, int> ActiveByTenant,
    IReadOnlyDictionary<string, DateTimeOffset> TargetNextAllowedAt,
    int MaximumActivePerTenant);

public static class FleetFairScheduler
{
    public static FleetQueueCandidate? Select(
        IReadOnlyCollection<FleetQueueCandidate> candidates,
        FleetTransport transport,
        FleetSchedulingState state,
        DateTimeOffset now,
        string? tenantAfter)
    {
        var eligibleTenants = candidates
            .Where(candidate => candidate.Job.Transport == transport && candidate.Job.State == FleetJobState.Queued && candidate.Job.NextAttemptAt <= now)
            .Where(candidate => !state.TargetNextAllowedAt.TryGetValue(candidate.TargetDigest, out var next) || next <= now)
            .Where(candidate => !state.ActiveByTenant.TryGetValue(candidate.Job.TenantId, out var active) || active < state.MaximumActivePerTenant)
            .GroupBy(candidate => candidate.Job.TenantId, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .ToArray();
        if (eligibleTenants.Length == 0) return null;

        var selectedTenant = eligibleTenants.FirstOrDefault(group => string.CompareOrdinal(group.Key, tenantAfter) > 0) ?? eligibleTenants[0];
        return selectedTenant
            .OrderBy(candidate => candidate.EnqueuedAt)
            .ThenBy(candidate => candidate.Job.JobId, StringComparer.Ordinal)
            .First();
    }
}

public interface IFleetWorkerPool
{
    FleetTransport Transport { get; }
    Task ExecuteAsync(FleetJob job, CancellationToken cancellationToken);
}

public sealed class FleetWorkerDispatcher
{
    private readonly IReadOnlyDictionary<FleetTransport, IFleetWorkerPool> _pools;

    public FleetWorkerDispatcher(IEnumerable<IFleetWorkerPool> pools)
    {
        _pools = pools.ToDictionary(pool => pool.Transport);
        if (!_pools.Keys.ToHashSet().SetEquals(Enum.GetValues<FleetTransport>()))
        {
            throw new InvalidOperationException("Exactly one isolated worker pool is required for each transport.");
        }
    }

    public Task DispatchAsync(FleetJob job, CancellationToken cancellationToken) =>
        _pools[job.Transport].ExecuteAsync(job, cancellationToken);
}