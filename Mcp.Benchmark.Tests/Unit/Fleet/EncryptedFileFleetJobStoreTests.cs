using System.Security.Cryptography;
using System.Text;
using Mcp.Benchmark.Fleet.Jobs;
using Mcp.Benchmark.Fleet.Governance;

namespace Mcp.Benchmark.Tests.Unit.Fleet;

public sealed class EncryptedFileFleetJobStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"mcpval-fleet-{Guid.NewGuid():N}");
    private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);
    private static readonly DateTimeOffset Now = new(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);
    private const string DigestA = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string DigestB = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    [Fact]
    public async Task Store_ShouldPersistEncryptedTenantScopedJobAcrossInstances()
    {
        var candidate = Create();
        using (var first = new EncryptedFileFleetJobStore(_root, _key))
        {
            await first.CreateOrGetAsync(candidate, Submitter());
        }

        var bytes = await File.ReadAllBytesAsync(Directory.GetFiles(_root, "*.fleet").Single());
        var raw = Encoding.UTF8.GetString(bytes);
        raw.Should().NotContain(candidate.TenantId);
        raw.Should().NotContain(candidate.JobId);
        raw.Should().NotContain(candidate.RequestDigest);

        using var reopened = new EncryptedFileFleetJobStore(_root, _key);
        (await reopened.GetAsync(Reader(), "job-secret-1")).Should().Be(candidate);
        (await reopened.GetAsync(Reader("tenant-b"), "job-secret-1")).Should().BeNull();
    }

    [Fact]
    public async Task CreateOrGet_ShouldBeIdempotentAndRejectPayloadReuse()
    {
        using var store = new EncryptedFileFleetJobStore(_root, _key);
        var original = await store.CreateOrGetAsync(Create(), Submitter());
        var duplicate = await store.CreateOrGetAsync(Create() with { JobId = "job-other" }, Submitter());
        var conflict = () => store.CreateOrGetAsync(Create() with { JobId = "job-conflict", RequestDigest = DigestB }, Submitter());

        duplicate.Should().Be(original);
        await conflict.Should().ThrowAsync<FleetIdempotencyConflictException>();
    }

    [Fact]
    public async Task CompareExchange_ShouldRejectStaleWriterAndPersistEligibleOrdering()
    {
        using var first = new EncryptedFileFleetJobStore(_root, _key);
        using var second = new EncryptedFileFleetJobStore(_root, _key);
        var queued = await first.CreateOrGetAsync(Create(), Submitter());
        var leased = FleetJobStateMachine.Acquire(queued, "tenant-a", "worker", Now, TimeSpan.FromMinutes(1));
        await first.CompareExchangeAsync(leased, queued.Version, Context());

        var stale = () => second.CompareExchangeAsync(leased with { LeaseOwner = "stale" }, queued.Version, Context());
        await stale.Should().ThrowAsync<FleetConcurrencyException>();
        (await first.ListEligibleAsync(Operator(), FleetTransport.Http, Now, 10)).Should().BeEmpty();
        var audit = await first.ReadAuditAsync(Auditor());
        audit.Should().HaveCount(2);
        audit.Select(entry => entry.Action).Should().Equal("submit", "Queued->Leased");
    }

    [Fact]
    public async Task CompareExchange_ShouldRejectImmutableAndStateMachineBypass()
    {
        using var store = new EncryptedFileFleetJobStore(_root, _key);
        var queued = await store.CreateOrGetAsync(Create(), Submitter());

        var transportRewrite = () => store.CompareExchangeAsync(
            queued with { Version = 2, UpdatedAt = Now.AddSeconds(1), Transport = FleetTransport.Stdio }, queued.Version, Context());
        var terminalJump = () => store.CompareExchangeAsync(
            queued with { Version = 2, UpdatedAt = Now.AddSeconds(1), State = FleetJobState.Succeeded, ResultDigest = DigestA }, queued.Version, Context());

        await transportRewrite.Should().ThrowAsync<FleetInvariantException>();
        await terminalJump.Should().ThrowAsync<FleetInvariantException>();

        var leased = FleetJobStateMachine.Acquire(queued, "tenant-a", "worker-a", Now, TimeSpan.FromMinutes(1));
        await store.CompareExchangeAsync(leased, queued.Version, Context());
        var changedOwner = () => store.CompareExchangeAsync(
            leased with { Version = 3, UpdatedAt = Now.AddSeconds(1), State = FleetJobState.Running, LeaseOwner = "worker-b" }, leased.Version, Context("worker-a", Now.AddSeconds(1)));
        await changedOwner.Should().ThrowAsync<FleetInvariantException>();

        var running = FleetJobStateMachine.Start(leased, "tenant-a", "worker-a", Now.AddSeconds(1));
        var unauthorized = () => store.CompareExchangeAsync(running, leased.Version, Context("worker-b", Now.AddSeconds(1)));
        await unauthorized.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task Create_ShouldRejectCallerForgedInitialState()
    {
        using var store = new EncryptedFileFleetJobStore(_root, _key);
        var forged = Create() with { State = FleetJobState.Running, Version = 9, Attempt = 8, LeaseOwner = "forged", LeaseExpiresAt = Now.AddHours(1) };

        var create = () => store.CreateOrGetAsync(forged, Submitter());

        await create.Should().ThrowAsync<FleetInvariantException>();

        var unauthorized = () => store.CreateOrGetAsync(Create(), Reader());
        await unauthorized.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task Read_ShouldRejectOversizedSnapshotBeforeAllocatingPayload()
    {
        using var store = new EncryptedFileFleetJobStore(_root, _key);
        await store.CreateOrGetAsync(Create(), Submitter());
        var path = Directory.GetFiles(_root, "*.fleet").Single();
        await File.WriteAllBytesAsync(path, new byte[(16 * 1024 * 1024) + 64]);

        var read = () => store.GetAsync(Reader(), "job-secret-1");

        await read.Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public async Task Submitter_ShouldCancelQueuedJobAndPersistAuditWithoutWorkerAuthority()
    {
        using var store = new EncryptedFileFleetJobStore(_root, _key);
        var queued = await store.CreateOrGetAsync(Create(), Submitter());

        var cancelled = await store.RequestCancellationAsync(queued.JobId, queued.Version, Submitter(), Now.AddSeconds(1));
        var audit = await store.ReadAuditAsync(Auditor());

        cancelled.State.Should().Be(FleetJobState.Cancelled);
        audit.Select(entry => entry.Action).Should().Equal("submit", "cancel-request");
    }

    [Fact]
    public async Task Cancellation_ShouldRejectBackwardTimeAndCancelExpiredLeaseWithoutRetainingIt()
    {
        using var store = new EncryptedFileFleetJobStore(_root, _key);
        var queued = await store.CreateOrGetAsync(Create(), Submitter());
        var leased = FleetJobStateMachine.Acquire(queued, "tenant-a", "worker", Now, TimeSpan.FromSeconds(10));
        await store.CompareExchangeAsync(leased, queued.Version, Context());

        var backward = () => store.RequestCancellationAsync(leased.JobId, leased.Version, Submitter(), Now.AddSeconds(-1));
        await backward.Should().ThrowAsync<InvalidOperationException>();

        var cancelled = await store.RequestCancellationAsync(leased.JobId, leased.Version, Submitter(), Now.AddSeconds(11));
        cancelled.State.Should().Be(FleetJobState.Cancelled);
        cancelled.LeaseOwner.Should().BeNull();
        cancelled.LeaseExpiresAt.Should().BeNull();
    }

    public void Dispose()
    {
        CryptographicOperations.ZeroMemory(_key);
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private static FleetJob Create() => FleetJobStateMachine.Create(
        "job-secret-1", "tenant-a", DigestA, DigestA, FleetTransport.Http, Now);

    private static FleetMutationContext Context(string? worker = null, DateTimeOffset? now = null) =>
        new(Operator(), worker, now ?? Now);

    private static FleetPrincipal Submitter(string tenant = "tenant-a") =>
        new("submitter", tenant, new HashSet<FleetRole> { FleetRole.Submitter });

    private static FleetPrincipal Reader(string tenant = "tenant-a") =>
        new("reader", tenant, new HashSet<FleetRole> { FleetRole.Reader });

    private static FleetPrincipal Operator(string tenant = "tenant-a") =>
        new("operator", tenant, new HashSet<FleetRole> { FleetRole.Operator });

    private static FleetPrincipal Auditor(string tenant = "tenant-a") =>
        new("auditor", tenant, new HashSet<FleetRole> { FleetRole.Auditor });
}