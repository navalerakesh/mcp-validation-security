using Mcp.Benchmark.Fleet.Jobs;

namespace Mcp.Benchmark.Tests.Unit.Fleet;

public sealed class FleetJobStateMachineTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);
    private const string Digest = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Fact]
    public void Lifecycle_ShouldRequireTenantAndActiveLeaseOwnership()
    {
        var queued = Create();
        var leased = FleetJobStateMachine.Acquire(queued, "tenant-a", "worker-http-1", Now, TimeSpan.FromMinutes(1));
        var running = FleetJobStateMachine.Start(leased, "tenant-a", "worker-http-1", Now.AddSeconds(1));

        var wrongTenant = () => FleetJobStateMachine.Complete(running, "tenant-b", "worker-http-1", Digest, Now.AddSeconds(2));
        var wrongWorker = () => FleetJobStateMachine.Complete(running, "tenant-a", "worker-http-2", Digest, Now.AddSeconds(2));
        var completed = FleetJobStateMachine.Complete(running, "tenant-a", "worker-http-1", Digest, Now.AddSeconds(2));

        wrongTenant.Should().Throw<UnauthorizedAccessException>();
        wrongWorker.Should().Throw<InvalidOperationException>();
        completed.State.Should().Be(FleetJobState.Succeeded);
        completed.Version.Should().Be(4);
        completed.LeaseOwner.Should().BeNull();
    }

    [Fact]
    public void Failure_ShouldRetryThenDeadLetterAtBoundedAttemptLimit()
    {
        var first = FleetJobStateMachine.Start(
            FleetJobStateMachine.Acquire(Create(maxAttempts: 2), "tenant-a", "worker", Now, TimeSpan.FromMinutes(1)),
            "tenant-a", "worker", Now);
        var retry = FleetJobStateMachine.Fail(first, "tenant-a", "worker", "transient", Now, TimeSpan.FromSeconds(5));
        var second = FleetJobStateMachine.Start(
            FleetJobStateMachine.Acquire(retry, "tenant-a", "worker", Now.AddSeconds(5), TimeSpan.FromMinutes(1)),
            "tenant-a", "worker", Now.AddSeconds(5));
        var dead = FleetJobStateMachine.Fail(second, "tenant-a", "worker", "transient", Now.AddSeconds(6), TimeSpan.FromSeconds(5));

        retry.State.Should().Be(FleetJobState.Queued);
        retry.NextAttemptAt.Should().Be(Now.AddSeconds(5));
        dead.State.Should().Be(FleetJobState.DeadLettered);
    }

    [Fact]
    public void CancellationAndExpiredLeaseRecovery_ShouldFailClosed()
    {
        var leased = FleetJobStateMachine.Acquire(Create(), "tenant-a", "worker", Now, TimeSpan.FromSeconds(10));
        var cancelRequested = FleetJobStateMachine.RequestCancellation(leased, "tenant-a", Now.AddSeconds(1));
        var cancelled = FleetJobStateMachine.RecoverExpiredLease(cancelRequested, Now.AddSeconds(11));
        var staleStart = () => FleetJobStateMachine.Start(leased, "tenant-a", "worker", Now.AddSeconds(11));

        cancelRequested.State.Should().Be(FleetJobState.CancelRequested);
        cancelled.State.Should().Be(FleetJobState.Cancelled);
        staleStart.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void PersistedOutcomes_ShouldRequireExclusiveSuccessOrFailureEvidence()
    {
        var leased = FleetJobStateMachine.Acquire(Create(), "tenant-a", "worker", Now, TimeSpan.FromMinutes(1));
        var running = FleetJobStateMachine.Start(leased, "tenant-a", "worker", Now.AddSeconds(1));
        var invalidRetry = running with
        {
            Version = running.Version + 1,
            UpdatedAt = Now.AddSeconds(2),
            State = FleetJobState.Queued,
            LeaseOwner = null,
            LeaseExpiresAt = null
        };
        var invalidSuccess = FleetJobStateMachine.Complete(running with { FailureCode = "old-failure" }, "tenant-a", "worker", Digest, Now.AddSeconds(2))
            with { FailureCode = "old-failure" };

        var retryValidation = () => FleetJobStateMachine.ValidatePersistedTransition(
            running, invalidRetry, new FleetMutationContext(Operator(), "worker", Now.AddSeconds(2)));
        var successValidation = () => FleetJobStateMachine.ValidatePersistedTransition(
            running, invalidSuccess, new FleetMutationContext(Operator(), "worker", Now.AddSeconds(2)));

        retryValidation.Should().Throw<InvalidOperationException>();
        successValidation.Should().Throw<InvalidOperationException>();
    }

    private static FleetJob Create(int maxAttempts = 3) => FleetJobStateMachine.Create(
        "job-1", "tenant-a", Digest, Digest, FleetTransport.Http, Now, maxAttempts);

    private static Mcp.Benchmark.Fleet.Governance.FleetPrincipal Operator() =>
        new("operator", "tenant-a", new HashSet<Mcp.Benchmark.Fleet.Governance.FleetRole> { Mcp.Benchmark.Fleet.Governance.FleetRole.Operator });
}