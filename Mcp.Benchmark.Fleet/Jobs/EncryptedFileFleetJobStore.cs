using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Mcp.Benchmark.Fleet.Governance;

namespace Mcp.Benchmark.Fleet.Jobs;

public sealed class EncryptedFileFleetJobStore : IFleetJobStore, IDisposable
{
    private static readonly byte[] Magic = "MCFV1"u8.ToArray();
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int MaximumSnapshotBytes = 16 * 1024 * 1024;

    private readonly string _root;
    private readonly byte[] _key;
    private bool _disposed;

    public EncryptedFileFleetJobStore(string root, ReadOnlySpan<byte> encryptionKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        if (encryptionKey.Length != 32)
        {
            throw new ArgumentException("Fleet store encryption key must contain exactly 32 bytes.", nameof(encryptionKey));
        }

        _root = Path.GetFullPath(root);
        Directory.CreateDirectory(_root);
        if (new DirectoryInfo(_root).LinkTarget != null)
        {
            throw new InvalidOperationException("Fleet store root cannot be a symbolic link.");
        }
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(_root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        _key = encryptionKey.ToArray();
    }

    public async Task<FleetJob> CreateOrGetAsync(FleetJob candidate, FleetPrincipal principal, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        FleetAuthorization.Demand(principal, candidate.TenantId, FleetPermission.SubmitJob);
        try
        {
            FleetJobStateMachine.ValidateNewJob(candidate);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            throw new FleetInvariantException(ex.Message);
        }
        await using var tenantLock = await AcquireTenantLockAsync(candidate.TenantId, cancellationToken).ConfigureAwait(false);
        var state = await ReadTenantAsync(candidate.TenantId, cancellationToken).ConfigureAwait(false);
        var jobs = state.Jobs;
        var existing = jobs.SingleOrDefault(job => string.Equals(job.IdempotencyKeyHash, candidate.IdempotencyKeyHash, StringComparison.Ordinal));
        if (existing != null)
        {
            if (!string.Equals(existing.RequestDigest, candidate.RequestDigest, StringComparison.Ordinal) || existing.Transport != candidate.Transport)
            {
                throw new FleetIdempotencyConflictException("The tenant idempotency key is already bound to a different request.");
            }
            return existing;
        }
        if (jobs.Any(job => string.Equals(job.JobId, candidate.JobId, StringComparison.Ordinal)))
        {
            throw new FleetIdempotencyConflictException("The tenant job identifier already exists.");
        }

        jobs.Add(candidate);
        state.Audit.Add(CreateAudit(state, principal.SubjectId, candidate.JobId, "submit", 0, candidate.Version, candidate.CreatedAt));
        await WriteTenantAsync(candidate.TenantId, state, cancellationToken).ConfigureAwait(false);
        return candidate;
    }

    public async Task<FleetJob?> GetAsync(FleetPrincipal principal, string jobId, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        FleetAuthorization.Demand(principal, principal.TenantId, FleetPermission.ReadJob);
        await using var tenantLock = await AcquireTenantLockAsync(principal.TenantId, cancellationToken).ConfigureAwait(false);
        return (await ReadTenantAsync(principal.TenantId, cancellationToken).ConfigureAwait(false)).Jobs
            .SingleOrDefault(job => string.Equals(job.JobId, jobId, StringComparison.Ordinal));
    }

    public async Task<IReadOnlyList<FleetJob>> ListEligibleAsync(
        FleetPrincipal principal,
        FleetTransport transport,
        DateTimeOffset now,
        int limit,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        FleetAuthorization.Demand(principal, principal.TenantId, FleetPermission.OperateWorker);
        if (limit is < 1 or > 1000)
        {
            throw new ArgumentOutOfRangeException(nameof(limit));
        }
        await using var tenantLock = await AcquireTenantLockAsync(principal.TenantId, cancellationToken).ConfigureAwait(false);
        return (await ReadTenantAsync(principal.TenantId, cancellationToken).ConfigureAwait(false)).Jobs
            .Where(job => job.State == FleetJobState.Queued && job.Transport == transport && job.NextAttemptAt <= now)
            .OrderBy(job => job.NextAttemptAt)
            .ThenBy(job => job.CreatedAt)
            .ThenBy(job => job.JobId, StringComparer.Ordinal)
            .Take(limit)
            .ToArray();
    }

    public async Task<IReadOnlyList<FleetJobMutationAudit>> ReadAuditAsync(FleetPrincipal principal, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        FleetAuthorization.Demand(principal, principal.TenantId, FleetPermission.ReadAudit);
        await using var tenantLock = await AcquireTenantLockAsync(principal.TenantId, cancellationToken).ConfigureAwait(false);
        return (await ReadTenantAsync(principal.TenantId, cancellationToken).ConfigureAwait(false)).Audit.ToArray();
    }

    public async Task<FleetJob> RequestCancellationAsync(
        string jobId,
        long expectedVersion,
        FleetPrincipal principal,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        FleetAuthorization.Demand(principal, principal.TenantId, FleetPermission.CancelJob);
        await using var tenantLock = await AcquireTenantLockAsync(principal.TenantId, cancellationToken).ConfigureAwait(false);
        var state = await ReadTenantAsync(principal.TenantId, cancellationToken).ConfigureAwait(false);
        var index = state.Jobs.FindIndex(job => string.Equals(job.JobId, jobId, StringComparison.Ordinal));
        if (index < 0 || state.Jobs[index].Version != expectedVersion)
        {
            throw new FleetConcurrencyException("Job version changed before cancellation could be committed.");
        }
        var current = state.Jobs[index];
        var next = FleetJobStateMachine.RequestCancellation(current, principal.TenantId, now);
        if (ReferenceEquals(next, current)) return current;
        try
        {
            FleetJobStateMachine.ValidateCancellationTransition(current, next, principal.TenantId, now);
        }
        catch (InvalidOperationException ex)
        {
            throw new FleetInvariantException(ex.Message);
        }
        state.Jobs[index] = next;
        state.Audit.Add(CreateAudit(state, principal.SubjectId, jobId, "cancel-request", current.Version, next.Version, now));
        await WriteTenantAsync(principal.TenantId, state, cancellationToken).ConfigureAwait(false);
        return next;
    }

    public async Task<FleetJob> CompareExchangeAsync(
        FleetJob next,
        long expectedVersion,
        FleetMutationContext context,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        FleetAuthorization.Demand(context.Principal, next.TenantId, FleetPermission.OperateWorker);
        if (next.Version != checked(expectedVersion + 1))
        {
            throw new FleetConcurrencyException("Replacement version must advance exactly once.");
        }
        await using var tenantLock = await AcquireTenantLockAsync(next.TenantId, cancellationToken).ConfigureAwait(false);
        var state = await ReadTenantAsync(next.TenantId, cancellationToken).ConfigureAwait(false);
        var jobs = state.Jobs;
        var index = jobs.FindIndex(job => string.Equals(job.JobId, next.JobId, StringComparison.Ordinal));
        if (index < 0 || jobs[index].Version != expectedVersion)
        {
            throw new FleetConcurrencyException("Job version changed before the update could be committed.");
        }
        try
        {
            FleetJobStateMachine.ValidatePersistedTransition(jobs[index], next, context);
        }
        catch (UnauthorizedAccessException)
        {
            throw;
        }
        catch (InvalidOperationException ex)
        {
            throw new FleetInvariantException(ex.Message);
        }

        var current = jobs[index];
        jobs[index] = next;
        state.Audit.Add(CreateAudit(
            state,
            context.Principal.SubjectId,
            next.JobId,
            $"{current.State}->{next.State}",
            current.Version,
            next.Version,
            context.Now));
        await WriteTenantAsync(next.TenantId, state, cancellationToken).ConfigureAwait(false);
        return next;
    }

    public void Dispose()
    {
        if (_disposed) return;
        CryptographicOperations.ZeroMemory(_key);
        _disposed = true;
    }

    private async Task<FleetTenantSnapshot> ReadTenantAsync(string tenantId, CancellationToken cancellationToken)
    {
        var path = GetSnapshotPath(tenantId);
        if (!File.Exists(path)) return new FleetTenantSnapshot();
        var file = new FileInfo(path);
        if (file.LinkTarget != null || file.Length > MaximumSnapshotBytes + Magic.Length + NonceSize + TagSize)
        {
            throw new InvalidDataException("Fleet snapshot must be a bounded regular file.");
        }
        var envelope = new byte[file.Length];
        await using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            var offset = 0;
            while (offset < envelope.Length)
            {
                var read = await stream.ReadAsync(envelope.AsMemory(offset), cancellationToken).ConfigureAwait(false);
                if (read == 0) throw new EndOfStreamException("Fleet snapshot ended before its declared length.");
                offset += read;
            }
            if (stream.ReadByte() != -1) throw new InvalidDataException("Fleet snapshot grew beyond its size bound while reading.");
        }
        if (new FileInfo(path).LinkTarget != null ||
            envelope.Length < Magic.Length + NonceSize + TagSize ||
            !envelope.AsSpan(0, Magic.Length).SequenceEqual(Magic))
        {
            throw new InvalidDataException("Fleet snapshot envelope is invalid or exceeds its size limit.");
        }

        var nonce = envelope[Magic.Length..(Magic.Length + NonceSize)];
        var tag = envelope[(Magic.Length + NonceSize)..(Magic.Length + NonceSize + TagSize)];
        var ciphertext = envelope[(Magic.Length + NonceSize + TagSize)..];
        var plaintext = new byte[ciphertext.Length];
        try
        {
            using var aes = new AesGcm(_key, TagSize);
            aes.Decrypt(nonce, ciphertext, tag, plaintext, GetTenantAssociatedData(tenantId));
            return JsonSerializer.Deserialize<FleetTenantSnapshot>(plaintext, JsonOptions)
                ?? throw new InvalidDataException("Fleet snapshot payload is empty.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private async Task WriteTenantAsync(string tenantId, FleetTenantSnapshot state, CancellationToken cancellationToken)
    {
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(state, JsonOptions);
        if (plaintext.Length > MaximumSnapshotBytes)
        {
            throw new InvalidOperationException("Fleet tenant snapshot exceeds its 16 MiB limit.");
        }
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var tag = new byte[TagSize];
        var ciphertext = new byte[plaintext.Length];
        try
        {
            using var aes = new AesGcm(_key, TagSize);
            aes.Encrypt(nonce, plaintext, ciphertext, tag, GetTenantAssociatedData(tenantId));
            var envelope = new byte[Magic.Length + NonceSize + TagSize + ciphertext.Length];
            Magic.CopyTo(envelope, 0);
            nonce.CopyTo(envelope, Magic.Length);
            tag.CopyTo(envelope, Magic.Length + NonceSize);
            ciphertext.CopyTo(envelope, Magic.Length + NonceSize + TagSize);

            var destination = GetSnapshotPath(tenantId);
            var temporary = Path.Combine(_root, $".{Path.GetFileName(destination)}.{Guid.NewGuid():N}.tmp");
            try
            {
                await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                {
                    if (!OperatingSystem.IsWindows())
                    {
                        File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                    }
                    await stream.WriteAsync(envelope, cancellationToken).ConfigureAwait(false);
                    stream.Flush(flushToDisk: true);
                }
                File.Move(temporary, destination, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private static FleetJobMutationAudit CreateAudit(
        FleetTenantSnapshot state,
        string subjectId,
        string jobId,
        string action,
        long fromVersion,
        long toVersion,
        DateTimeOffset timestamp) =>
        new(state.Audit.Count + 1L, subjectId, jobId, action, fromVersion, toVersion, timestamp);

    private sealed class FleetTenantSnapshot
    {
        public List<FleetJob> Jobs { get; init; } = [];
        public List<FleetJobMutationAudit> Audit { get; init; } = [];
    }

    private async Task<FileStream> AcquireTenantLockAsync(string tenantId, CancellationToken cancellationToken)
    {
        var path = Path.Combine(_root, $"{Hash(tenantId)}.lock");
        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
            }
            catch (IOException) when (DateTimeOffset.UtcNow < deadline)
            {
                await Task.Delay(10, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private string GetSnapshotPath(string tenantId) => Path.Combine(_root, $"{Hash(tenantId)}.fleet");

    private static byte[] GetTenantAssociatedData(string tenantId) => Encoding.UTF8.GetBytes($"mcpval-fleet:{Hash(tenantId)}");

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}