namespace Mcp.Benchmark.Fleet.Jobs;

public static class FleetJobStateMachine
{
    public static void ValidateNewJob(FleetJob job)
    {
        ValidateIdentifier(job.JobId, nameof(job.JobId));
        ValidateIdentifier(job.TenantId, nameof(job.TenantId));
        ValidateDigest(job.IdempotencyKeyHash, nameof(job.IdempotencyKeyHash));
        ValidateDigest(job.RequestDigest, nameof(job.RequestDigest));
        if (!Enum.IsDefined(job.Transport) || !Enum.IsDefined(job.State) || job.CreatedAt == default || job.UpdatedAt == default)
        {
            throw new InvalidOperationException("A new job contains an invalid transport, state, or timestamp.");
        }
        if (job.State != FleetJobState.Queued || job.Version != 1 || job.Attempt != 0 || job.LeaseOwner != null ||
            job.LeaseExpiresAt != null || job.ResultDigest != null || job.FailureCode != null ||
            job.CreatedAt != job.UpdatedAt || job.NextAttemptAt < job.CreatedAt || job.MaxAttempts is < 1 or > 10)
        {
            throw new InvalidOperationException("A new job must be an unleased version-one queued record with no execution outcome.");
        }
    }

    public static void ValidatePersistedTransition(FleetJob current, FleetJob next, FleetMutationContext context)
    {
        if (!string.Equals(current.TenantId, context.Principal.TenantId, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException("Job mutation is tenant scoped.");
        }
        if (current.JobId != next.JobId || current.TenantId != next.TenantId ||
            current.IdempotencyKeyHash != next.IdempotencyKeyHash || current.RequestDigest != next.RequestDigest ||
            current.Transport != next.Transport || current.CreatedAt != next.CreatedAt || current.MaxAttempts != next.MaxAttempts)
        {
            throw new InvalidOperationException("Immutable job identity and policy fields cannot change.");
        }
        if (next.Version != checked(current.Version + 1) || next.UpdatedAt < current.UpdatedAt)
        {
            throw new InvalidOperationException("Job version or time progression is invalid.");
        }
        var acquiringLease = current.State == FleetJobState.Queued && next.State == FleetJobState.Leased;
        if (next.Attempt != current.Attempt + (acquiringLease ? 1 : 0))
        {
            throw new InvalidOperationException("Attempt count must increment exactly once during lease acquisition only.");
        }

        var allowed = (current.State, next.State) switch
        {
            (FleetJobState.Queued, FleetJobState.Leased or FleetJobState.Cancelled) => true,
            (FleetJobState.Leased, FleetJobState.Running or FleetJobState.CancelRequested or FleetJobState.Queued or FleetJobState.DeadLettered) => true,
            (FleetJobState.Running, FleetJobState.Succeeded or FleetJobState.CancelRequested or FleetJobState.Queued or FleetJobState.DeadLettered) => true,
            (FleetJobState.CancelRequested, FleetJobState.Cancelled) => true,
            _ => false
        };
        if (!allowed) throw new InvalidOperationException("Job state transition is not allowed.");

        var requiresLease = next.State is FleetJobState.Leased or FleetJobState.Running or FleetJobState.CancelRequested;
        if (requiresLease != (next.LeaseOwner != null && next.LeaseExpiresAt != null))
        {
            throw new InvalidOperationException("Job lease shape does not match its state.");
        }
        if (requiresLease && next.LeaseExpiresAt <= next.UpdatedAt)
        {
            throw new InvalidOperationException("An active lease must expire after the transition time.");
        }
        if (requiresLease && next.LeaseExpiresAt - next.UpdatedAt > TimeSpan.FromMinutes(15))
        {
            throw new InvalidOperationException("Persisted leases cannot exceed 15 minutes.");
        }
        if (current.State is FleetJobState.Leased or FleetJobState.Running or FleetJobState.CancelRequested && requiresLease &&
            (!string.Equals(current.LeaseOwner, next.LeaseOwner, StringComparison.Ordinal) || current.LeaseExpiresAt != next.LeaseExpiresAt))
        {
            throw new InvalidOperationException("Lease ownership and expiry cannot change during a state transition.");
        }
        if (current.State is FleetJobState.Leased or FleetJobState.Running or FleetJobState.CancelRequested)
        {
            var ownsActiveLease = current.LeaseExpiresAt > context.Now &&
                string.Equals(current.LeaseOwner, context.LeaseOwner, StringComparison.Ordinal);
            var isExpiredRecovery = current.LeaseExpiresAt <= context.Now && context.LeaseOwner == null &&
                next.State is FleetJobState.Queued or FleetJobState.DeadLettered or FleetJobState.Cancelled &&
                (next.State == FleetJobState.Cancelled || string.Equals(next.FailureCode, "lease-expired", StringComparison.Ordinal));
            if (!ownsActiveLease && !isExpiredRecovery)
            {
                throw new UnauthorizedAccessException("The mutation does not own the active lease and is not an expired-lease recovery.");
            }
        }
        if (next.State == FleetJobState.Succeeded && string.IsNullOrWhiteSpace(next.ResultDigest))
        {
            throw new InvalidOperationException("A succeeded job requires a result digest.");
        }
        if (next.State == FleetJobState.Succeeded)
        {
            ValidateDigest(next.ResultDigest!, nameof(next.ResultDigest));
            if (next.FailureCode != null)
            {
                throw new InvalidOperationException("A succeeded job cannot carry a failure code.");
            }
        }
        else if (next.ResultDigest != null)
        {
            throw new InvalidOperationException("Only a succeeded job can carry a result digest.");
        }
        if (current.State is FleetJobState.Leased or FleetJobState.Running &&
            next.State is FleetJobState.Queued or FleetJobState.DeadLettered &&
            string.IsNullOrWhiteSpace(next.FailureCode))
        {
            throw new InvalidOperationException("Retry and dead-letter transitions require a failure code.");
        }
    }

    public static void ValidateCancellationTransition(FleetJob current, FleetJob next, string tenantId, DateTimeOffset now)
    {
        RequireTenant(current, tenantId);
        if (now < current.UpdatedAt || next.Version != checked(current.Version + 1) || next.UpdatedAt != now ||
            next.Attempt != current.Attempt || next.ResultDigest != current.ResultDigest ||
            next.JobId != current.JobId || next.IdempotencyKeyHash != current.IdempotencyKeyHash ||
            next.RequestDigest != current.RequestDigest || next.Transport != current.Transport ||
            next.CreatedAt != current.CreatedAt || next.MaxAttempts != current.MaxAttempts)
        {
            throw new InvalidOperationException("Cancellation transition changed immutable state or moved time/version backward.");
        }

        var valid = current.State switch
        {
            FleetJobState.Queued => next.State == FleetJobState.Cancelled && next.LeaseOwner == null && next.LeaseExpiresAt == null,
            FleetJobState.Leased or FleetJobState.Running when current.LeaseExpiresAt > now =>
                next.State == FleetJobState.CancelRequested && next.LeaseOwner == current.LeaseOwner && next.LeaseExpiresAt == current.LeaseExpiresAt,
            FleetJobState.Leased or FleetJobState.Running =>
                next.State == FleetJobState.Cancelled && next.LeaseOwner == null && next.LeaseExpiresAt == null,
            _ => false
        };
        if (!valid) throw new InvalidOperationException("Cancellation transition is invalid for the current lease state.");
    }

    public static FleetJob Create(
        string jobId,
        string tenantId,
        string idempotencyKeyHash,
        string requestDigest,
        FleetTransport transport,
        DateTimeOffset now,
        int maxAttempts = 3)
    {
        ValidateIdentifier(jobId, nameof(jobId));
        ValidateIdentifier(tenantId, nameof(tenantId));
        ValidateDigest(idempotencyKeyHash, nameof(idempotencyKeyHash));
        ValidateDigest(requestDigest, nameof(requestDigest));
        if (maxAttempts is < 1 or > 10)
        {
            throw new ArgumentOutOfRangeException(nameof(maxAttempts));
        }

        return new FleetJob
        {
            JobId = jobId,
            TenantId = tenantId,
            IdempotencyKeyHash = idempotencyKeyHash,
            RequestDigest = requestDigest,
            Transport = transport,
            CreatedAt = now,
            UpdatedAt = now,
            NextAttemptAt = now,
            MaxAttempts = maxAttempts
        };
    }

    public static FleetJob Acquire(FleetJob job, string tenantId, string workerId, DateTimeOffset now, TimeSpan leaseDuration)
    {
        RequireTenant(job, tenantId);
        ValidateIdentifier(workerId, nameof(workerId));
        if (leaseDuration <= TimeSpan.Zero || leaseDuration > TimeSpan.FromMinutes(15))
        {
            throw new ArgumentOutOfRangeException(nameof(leaseDuration));
        }
        if (job.State != FleetJobState.Queued || job.NextAttemptAt > now)
        {
            throw new InvalidOperationException("Only an eligible queued job can be leased.");
        }

        return Next(job, now) with
        {
            State = FleetJobState.Leased,
            LeaseOwner = workerId,
            LeaseExpiresAt = now.Add(leaseDuration),
            Attempt = job.Attempt + 1
        };
    }

    public static FleetJob Start(FleetJob job, string tenantId, string workerId, DateTimeOffset now)
    {
        RequireActiveLease(job, tenantId, workerId, now, FleetJobState.Leased);
        return Next(job, now) with { State = FleetJobState.Running };
    }

    public static FleetJob Complete(FleetJob job, string tenantId, string workerId, string resultDigest, DateTimeOffset now)
    {
        RequireActiveLease(job, tenantId, workerId, now, FleetJobState.Running);
        ValidateDigest(resultDigest, nameof(resultDigest));
        return ClearLease(Next(job, now)) with { State = FleetJobState.Succeeded, ResultDigest = resultDigest, FailureCode = null };
    }

    public static FleetJob RequestCancellation(FleetJob job, string tenantId, DateTimeOffset now)
    {
        RequireTenant(job, tenantId);
        if (now < job.UpdatedAt) throw new InvalidOperationException("Cancellation time cannot move backward.");
        return job.State switch
        {
            FleetJobState.Queued => ClearLease(Next(job, now)) with { State = FleetJobState.Cancelled },
            FleetJobState.Leased or FleetJobState.Running when job.LeaseExpiresAt > now => Next(job, now) with { State = FleetJobState.CancelRequested },
            FleetJobState.Leased or FleetJobState.Running => ClearLease(Next(job, now)) with { State = FleetJobState.Cancelled },
            FleetJobState.CancelRequested or FleetJobState.Cancelled => job,
            _ => throw new InvalidOperationException("A terminal job cannot be cancelled.")
        };
    }

    public static FleetJob AcknowledgeCancellation(FleetJob job, string tenantId, string workerId, DateTimeOffset now)
    {
        RequireActiveLease(job, tenantId, workerId, now, FleetJobState.CancelRequested);
        return ClearLease(Next(job, now)) with { State = FleetJobState.Cancelled };
    }

    public static FleetJob Fail(FleetJob job, string tenantId, string workerId, string failureCode, DateTimeOffset now, TimeSpan retryDelay)
    {
        RequireActiveLease(job, tenantId, workerId, now, FleetJobState.Running);
        ValidateIdentifier(failureCode, nameof(failureCode));
        if (retryDelay < TimeSpan.Zero || retryDelay > TimeSpan.FromHours(1))
        {
            throw new ArgumentOutOfRangeException(nameof(retryDelay));
        }

        var terminal = job.Attempt >= job.MaxAttempts;
        return ClearLease(Next(job, now)) with
        {
            State = terminal ? FleetJobState.DeadLettered : FleetJobState.Queued,
            NextAttemptAt = terminal ? job.NextAttemptAt : now.Add(retryDelay),
            FailureCode = failureCode
        };
    }

    public static FleetJob RecoverExpiredLease(FleetJob job, DateTimeOffset now)
    {
        if (job.State is not (FleetJobState.Leased or FleetJobState.Running or FleetJobState.CancelRequested) ||
            job.LeaseExpiresAt is null || job.LeaseExpiresAt > now)
        {
            throw new InvalidOperationException("Job does not have an expired recoverable lease.");
        }

        if (job.State == FleetJobState.CancelRequested)
        {
            return ClearLease(Next(job, now)) with { State = FleetJobState.Cancelled };
        }

        var terminal = job.Attempt >= job.MaxAttempts;
        return ClearLease(Next(job, now)) with
        {
            State = terminal ? FleetJobState.DeadLettered : FleetJobState.Queued,
            NextAttemptAt = now,
            FailureCode = "lease-expired"
        };
    }

    private static FleetJob Next(FleetJob job, DateTimeOffset now) => job with { Version = checked(job.Version + 1), UpdatedAt = now };

    private static FleetJob ClearLease(FleetJob job) => job with { LeaseOwner = null, LeaseExpiresAt = null };

    private static void RequireActiveLease(FleetJob job, string tenantId, string workerId, DateTimeOffset now, FleetJobState state)
    {
        RequireTenant(job, tenantId);
        if (job.State != state || !string.Equals(job.LeaseOwner, workerId, StringComparison.Ordinal) || job.LeaseExpiresAt <= now)
        {
            throw new InvalidOperationException("The caller does not own an active lease in the required state.");
        }
    }

    private static void RequireTenant(FleetJob job, string tenantId)
    {
        if (!string.Equals(job.TenantId, tenantId, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException("Job access is tenant scoped.");
        }
    }

    private static void ValidateIdentifier(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128 || value.Any(character => character is < '!' or > '~'))
        {
            throw new ArgumentException("Value must be 1-128 printable ASCII characters.", parameterName);
        }
    }

    private static void ValidateDigest(string value, string parameterName)
    {
        if (value.Length != 64 || value.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new ArgumentException("Value must be a 64-character hexadecimal SHA-256 digest.", parameterName);
        }
    }
}