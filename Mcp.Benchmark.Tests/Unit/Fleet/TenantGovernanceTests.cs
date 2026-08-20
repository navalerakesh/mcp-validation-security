using Mcp.Benchmark.Fleet.Governance;
using Mcp.Benchmark.Fleet.Jobs;
using Mcp.Benchmark.Fleet.Scheduling;

namespace Mcp.Benchmark.Tests.Unit.Fleet;

public sealed class TenantGovernanceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);
    private const string Digest = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Fact]
    public void AuthorizationQuotaAndRetention_ShouldFailClosed()
    {
        var submitter = new FleetPrincipal("subject", "tenant-a", new HashSet<FleetRole> { FleetRole.Submitter });
        FleetAuthorization.Demand(submitter, "tenant-a", FleetPermission.SubmitJob);
        var crossTenant = () => FleetAuthorization.Demand(submitter, "tenant-b", FleetPermission.ReadJob);
        var audit = () => FleetAuthorization.Demand(submitter, "tenant-a", FleetPermission.ReadAudit);
        var quota = TenantQuotaEvaluator.Evaluate(new TenantQuotaPolicy { MaximumDailyCost = 10 }, new TenantUsage(0, 0, 0, 0, 9), 1, 0, 2);
        var held = new FleetArtifactRetention { TenantId = "tenant-a", ArtifactId = "artifact", DeleteAfter = Now.AddDays(-1), LegalHold = true };
        var admin = new FleetPrincipal("admin", "tenant-a", new HashSet<FleetRole> { FleetRole.TenantAdmin });

        crossTenant.Should().Throw<UnauthorizedAccessException>();
        audit.Should().Throw<UnauthorizedAccessException>();
        quota.Should().Be(new QuotaDecision(false, "daily-cost"));
        FleetRetentionPolicy.CanDelete(held, admin, Now).Should().BeFalse();
    }

    [Fact]
    public void AuditChain_ShouldDetectMutation()
    {
        var key = Enumerable.Repeat((byte)0x42, 32).ToArray();
        var trusted = new Dictionary<string, byte[]> { ["audit-key-1"] = key };
        var first = FleetAuditChain.Append(null, "tenant-a", "subject", "job.submit", Digest, Now, "audit-key-1", key);
        var second = FleetAuditChain.Append(first, "tenant-a", "worker", "job.complete", Digest, Now.AddSeconds(1), "audit-key-1", key);
        var checkpoint = FleetAuditChain.CreateCheckpoint(second, "audit-key-1", key);

        FleetAuditChain.Verify([first, second], trusted, checkpoint).Should().BeTrue();
        FleetAuditChain.Verify([first], trusted, checkpoint).Should().BeFalse();
        FleetAuditChain.Verify([], trusted, checkpoint).Should().BeFalse();
        FleetAuditChain.Verify([first, second with { Action = "job.delete" }], trusted, checkpoint).Should().BeFalse();
        FleetAuditChain.Verify([first, second], new Dictionary<string, byte[]> { ["audit-key-1"] = new byte[32] }, checkpoint).Should().BeFalse();
    }

    [Fact]
    public async Task SchedulerAndDispatcher_ShouldEnforceFairnessRateLimitAndTransportIsolation()
    {
        var tenantA = Candidate("a-1", "tenant-a", FleetTransport.Http, "target-a", Now);
        var tenantB = Candidate("b-1", "tenant-b", FleetTransport.Http, "target-b", Now);
        var blocked = Candidate("c-1", "tenant-c", FleetTransport.Http, "target-c", Now);
        var state = new FleetSchedulingState(
            new Dictionary<string, int> { ["tenant-c"] = 1 },
            new Dictionary<string, DateTimeOffset> { ["target-a"] = Now.AddMinutes(1) },
            MaximumActivePerTenant: 1);

        FleetFairScheduler.Select([tenantA, tenantB, blocked], FleetTransport.Http, state, Now, tenantAfter: null)
            .Should().Be(tenantB);

        var delayed = Candidate("d-1", "tenant-d", FleetTransport.Http, "target-d", Now) with
        {
            Job = Candidate("d-1", "tenant-d", FleetTransport.Http, "target-d", Now).Job with { NextAttemptAt = Now.AddMinutes(1) }
        };
        FleetFairScheduler.Select([delayed], FleetTransport.Http, state, Now, tenantAfter: null).Should().BeNull();

        var http = new RecordingPool(FleetTransport.Http);
        var stdio = new RecordingPool(FleetTransport.Stdio);
        var dispatcher = new FleetWorkerDispatcher([http, stdio]);
        await dispatcher.DispatchAsync(tenantB.Job, CancellationToken.None);
        http.Executed.Should().ContainSingle().Which.Should().Be(tenantB.Job.JobId);
        stdio.Executed.Should().BeEmpty();
    }

    private static FleetQueueCandidate Candidate(string jobId, string tenantId, FleetTransport transport, string target, DateTimeOffset enqueued) =>
        new(FleetJobStateMachine.Create(jobId, tenantId, Digest, Digest, transport, enqueued), target, enqueued);

    private sealed class RecordingPool(FleetTransport transport) : IFleetWorkerPool
    {
        public FleetTransport Transport { get; } = transport;
        public List<string> Executed { get; } = [];
        public Task ExecuteAsync(FleetJob job, CancellationToken cancellationToken)
        {
            Executed.Add(job.JobId);
            return Task.CompletedTask;
        }
    }
}