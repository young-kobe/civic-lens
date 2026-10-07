using System.Data;
using System.Text.Json;
using CivicLens.Application.Collection;
using CivicLens.Application.Collection.Jobs;
using CivicLens.Collection.Contracts;
using CivicLens.Core.Collection;
using CivicLens.Infrastructure.Collection.Jobs.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace CivicLens.Infrastructure.Collection.Jobs;

/// <summary>Atomic job ownership, collection admission, and recovery accounting using database time.</summary>
public sealed partial class PostgresCollectionJobStore(IDbContextFactory<CollectionAttemptDbContext> contextFactory) : ICollectionJobStore
{
    private const string LockName = "civic-lens-collection-jobs";

    public static PostgresCollectionJobStore FromConnectionString(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        try
        {
            var parsed = new NpgsqlConnectionStringBuilder(connectionString);
            if (string.IsNullOrWhiteSpace(parsed.Host) || string.IsNullOrWhiteSpace(parsed.Database))
                throw new ArgumentException("Host and Database are required.");
            var options = new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(parsed.ConnectionString).Options;
            return new PostgresCollectionJobStore(new PooledDbContextFactory<CollectionAttemptDbContext>(options));
        }
        catch (ArgumentException)
        {
            throw new ArgumentException("CIVIC_LENS_DATABASE must be a valid Postgres connection string with Host and Database.");
        }
    }

    public Task<CollectionJobRecord> EnqueueAsync(CollectionJobDefinition definition, string idempotencyKey,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definition);
        definition.Validate();
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 256)
            throw new ArgumentException("Job idempotency key must contain 1 to 256 characters.", nameof(idempotencyKey));
        return TransactionAsync(async (db, now) =>
        {
            var existing = await db.Set<JobRow>().SingleOrDefaultAsync(row => row.IdempotencyKey == idempotencyKey, cancellationToken);
            if (existing is not null)
            {
                if (Read<CollectionJobDefinition>(existing.DefinitionJson) != definition)
                    throw new ArgumentException("The idempotency key belongs to a different job definition.");
                return await ToRecordAsync(db, existing, cancellationToken);
            }
            var row = new JobRow
            {
                JobId = Guid.NewGuid().ToString("N"),
                IdempotencyKey = idempotencyKey,
                DefinitionJson = Write(definition),
                State = CollectionJobState.Pending,
                CreatedAt = Ticks(now)
            };
            db.Add(row);
            return await ToRecordAsync(db, row, cancellationToken);
        }, cancellationToken);
    }

    public Task<CollectionJobRecord?> GetAsync(string jobId, CancellationToken cancellationToken) =>
        TransactionAsync(async (db, _) =>
        {
            var row = await db.Set<JobRow>().SingleOrDefaultAsync(row => row.JobId == jobId, cancellationToken);
            return row is null ? null : await ToRecordAsync(db, row, cancellationToken);
        }, cancellationToken);

    public async Task<IReadOnlyList<CollectionJobRecord>> ListAsync(int limit, CancellationToken cancellationToken)
    {
        if (limit is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(limit), "Job list limit must be 1 to 100.");
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var rows = await db.Set<JobRow>().AsNoTracking().OrderByDescending(row => row.CreatedAt).ThenBy(row => row.JobId)
            .Take(limit).ToListAsync(cancellationToken);
        var jobIds = rows.Select(row => row.JobId).ToArray();
        var attempts = await db.Set<JobAttemptRow>().AsNoTracking().Where(attempt => jobIds.Contains(attempt.JobId))
            .OrderBy(attempt => attempt.Sequence).ToListAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        var attemptsByJob = attempts.ToLookup(attempt => attempt.JobId);
        return rows.Select(row => ToRecord(row, attemptsByJob[row.JobId])).ToArray();
    }

    public Task<CollectionJobRecord?> CancelAsync(string jobId, CancellationToken cancellationToken) =>
        TransactionAsync(async (db, _) =>
        {
            var row = await db.Set<JobRow>().SingleOrDefaultAsync(row => row.JobId == jobId, cancellationToken);
            if (row is null) return null;
            if (row.State is CollectionJobState.Succeeded or CollectionJobState.Failed or CollectionJobState.Cancelled)
                return await ToRecordAsync(db, row, cancellationToken);
            row.CancellationRequested = true;
            if (!await db.Set<JobAttemptRow>().AnyAsync(attempt => attempt.JobId == jobId && attempt.ResolutionJson == null, cancellationToken))
            {
                row.State = CollectionJobState.Cancelled;
                row.RetryAt = null;
            }
            return await ToRecordAsync(db, row, cancellationToken);
        }, cancellationToken);

    public Task<CollectionJobClaim?> TryClaimAsync(string jobId, TimeSpan leaseDuration, CancellationToken cancellationToken)
    {
        ValidateLeaseDuration(leaseDuration);
        return TransactionAsync<CollectionJobClaim?>(async (db, now) =>
        {
            var row = await db.Set<JobRow>().SingleOrDefaultAsync(row => row.JobId == jobId, cancellationToken);
            if (row is null || row.LeaseExpiresAt > Ticks(now)) return null;
            row.LeaseToken = Guid.NewGuid().ToString("N");
            row.LeaseFence = checked(row.LeaseFence + 1);
            row.LeaseExpiresAt = Ticks(now + leaseDuration);
            return new CollectionJobClaim(Lease(row), await ToRecordAsync(db, row, cancellationToken));
        }, cancellationToken);
    }

    public Task<CollectionJobRenewal> RenewAsync(CollectionJobLease jobLease, CollectionCollectorLease? collectorLease,
        TimeSpan leaseDuration, CancellationToken cancellationToken)
    {
        ValidateLeaseDuration(leaseDuration);
        return TransactionAsync(async (db, now) =>
        {
            var row = await OwnedJobAsync(db, jobLease, now, cancellationToken);
            if (row is null) return new CollectionJobRenewal(false, false, null, null);
            var slot = collectorLease is null ? null : await db.Set<CollectorSlotRow>().SingleOrDefaultAsync(cancellationToken);
            if (collectorLease is not null && !OwnsSlot(slot, jobLease, collectorLease, now))
                return new CollectionJobRenewal(false, row.CancellationRequested, null, null);
            row.LeaseExpiresAt = Ticks(now + leaseDuration);
            if (slot is not null) slot.ExpiresAt = row.LeaseExpiresAt;
            return new CollectionJobRenewal(true, row.CancellationRequested, Lease(row), slot is null ? null : CollectorLease(slot));
        }, cancellationToken);
    }

    public Task<CollectionJobStartResult> TryStartAttemptAsync(CollectionJobLease jobLease, string artifactDirectory,
        CancellationToken cancellationToken) => TransactionAsync(async (db, now) =>
    {
        var row = await OwnedJobAsync(db, jobLease, now, cancellationToken);
        if (row is null) return new CollectionJobStartResult(CollectionJobStartStatus.LostOwnership);
        if (row.CancellationRequested || row.State == CollectionJobState.Cancelled)
            return new CollectionJobStartResult(CollectionJobStartStatus.Cancelled);
        if (row.State is CollectionJobState.Succeeded or CollectionJobState.Failed)
            return new CollectionJobStartResult(CollectionJobStartStatus.Exhausted);
        if (await db.Set<JobAttemptRow>().AnyAsync(attempt => attempt.JobId == row.JobId && attempt.ResolutionJson == null, cancellationToken))
            return Blocked(CollectionJobBlockReason.AttemptUnresolved);
        if (row.RetryAt > Ticks(now)) return Blocked(CollectionJobBlockReason.RetryNotDue, FromTicks(row.RetryAt));

        var definition = Read<CollectionJobDefinition>(row.DefinitionJson);
        var request = definition.CreateRequest(Guid.NewGuid().ToString("N"), Path.GetFullPath(artifactDirectory));
        request.Validate();
        var count = await db.Set<JobAttemptRow>().CountAsync(attempt => attempt.JobId == row.JobId, cancellationToken);
        if (!CollectionJobLifecycle.CanReserve(definition, request, row.State, count, row.ChargedRequests, row.ChargedBytes, row.ChargedSeconds))
        {
            row.State = CollectionJobState.Failed;
            row.RetryAt = null;
            return new CollectionJobStartResult(CollectionJobStartStatus.Exhausted);
        }
        var originKey = NormalizeOrigin(definition.AllowedOrigin);
        var origin = await db.Set<JobOriginRow>().SingleOrDefaultAsync(item => item.Origin == originKey, cancellationToken);
        if (origin?.UnresolvedAttemptId is not null) return Blocked(CollectionJobBlockReason.AttemptUnresolved);
        if (origin?.NotBefore > Ticks(now)) return Blocked(CollectionJobBlockReason.OriginBackoff, FromTicks(origin.NotBefore));
        var slot = await db.Set<CollectorSlotRow>().SingleOrDefaultAsync(cancellationToken);
        if (slot?.ExpiresAt > Ticks(now)) return Blocked(CollectionJobBlockReason.CollectorBusy, FromTicks(slot.ExpiresAt));

        var attempt = new JobAttemptRow
        {
            AttemptId = Guid.NewGuid().ToString("N"),
            JobId = row.JobId,
            Sequence = count + 1,
            RequestJson = Write(request),
            StartedAt = Ticks(now)
        };
        db.Add(attempt);
        if (origin is null) { origin = new JobOriginRow { Origin = originKey }; db.Add(origin); }
        origin.UnresolvedAttemptId = attempt.AttemptId;
        if (slot is null) { slot = new CollectorSlotRow(); db.Add(slot); }
        slot.JobId = row.JobId;
        slot.AttemptId = attempt.AttemptId;
        slot.Token = Guid.NewGuid().ToString("N");
        slot.Fence = checked(slot.Fence + 1);
        slot.ExpiresAt = row.LeaseExpiresAt;
        row.ChargedRequests += request.MaxRequests;
        row.ChargedBytes += request.MaxBytes;
        row.ChargedSeconds += request.TimeoutSeconds;
        row.State = CollectionJobState.Running;
        row.RetryAt = null;
        return new CollectionJobStartResult(CollectionJobStartStatus.Started, ToAttempt(attempt), CollectorLease(slot));
    }, cancellationToken);

    public Task<bool> ReleaseCollectorAsync(CollectionJobLease jobLease, CollectionCollectorLease collectorLease,
        CancellationToken cancellationToken) => TransactionAsync(async (db, now) =>
    {
        if (await OwnedJobAsync(db, jobLease, now, cancellationToken) is null) return false;
        var slot = await db.Set<CollectorSlotRow>().SingleOrDefaultAsync(cancellationToken);
        if (!OwnsSlot(slot, jobLease, collectorLease, now)) return false;
        slot!.JobId = null;
        slot.AttemptId = null;
        slot.Token = null;
        slot.ExpiresAt = null;
        return true;
    }, cancellationToken);

    public Task<bool> SettleAttemptAsync(CollectionJobLease jobLease, string attemptId,
        CollectionAttemptResolution resolution, CancellationToken cancellationToken) => TransactionAsync(async (db, now) =>
    {
        var job = await OwnedJobAsync(db, jobLease, now, cancellationToken);
        if (job is null) return false;
        var attempt = await db.Set<JobAttemptRow>().SingleAsync(item => item.AttemptId == attemptId && item.JobId == job.JobId, cancellationToken);
        var request = Read<CollectionRequest>(attempt.RequestJson);
        var definition = Read<CollectionJobDefinition>(job.DefinitionJson);
        ValidateResolution(resolution, request);
        var resolutionJson = Write(resolution);
        if (attempt.ResolutionJson is not null)
        {
            if (attempt.ResolutionJson != resolutionJson) throw new InvalidOperationException("Conflicting job attempt settlement.");
            return true;
        }
        var evidence = await PostgresCollectionAttemptStore.GetAsync(db, attemptId, cancellationToken);
        if (resolution.Outcome == CollectionJobAttemptOutcome.Interrupted)
        {
            if (evidence is not null) throw new InvalidOperationException("Imported evidence must be reconciled before retrying.");
        }
        else
        {
            if (evidence is null) throw new InvalidOperationException("Evidence import is not confirmed.");
            var expected = CollectionJobLifecycle.Resolve(evidence);
            if (expected.Outcome != resolution.Outcome || expected.RetryDelay != resolution.RetryDelay ||
                evidence.AttemptResult.SourceId != request.SourceId || evidence.AttemptResult.RequestedUrl != request.Url)
                throw new InvalidOperationException("Job settlement does not match imported evidence.");
        }
        if (resolution.Receipt is { } receipt)
        {
            var imported = CollectionAttemptImporter.CreateImport(attemptId, request, receipt);
            _ = imported.Decide(evidence, evidence!.PriorCapturedAttempt);
            job.ChargedRequests -= request.MaxRequests - receipt.RequestCount;
            job.ChargedBytes -= request.MaxBytes - receipt.BytesReceived;
        }
        var prospective = resolution.Outcome is CollectionJobAttemptOutcome.RetryableFailure or CollectionJobAttemptOutcome.Interrupted
            ? CollectionJobState.WaitingToRetry : CollectionJobState.Running;
        var canRetry = CollectionJobLifecycle.CanReserve(definition, request, prospective, attempt.Sequence,
            job.ChargedRequests, job.ChargedBytes, job.ChargedSeconds);
        job.State = CollectionJobLifecycle.Transition(job.State, resolution.Outcome, job.CancellationRequested, !canRetry);
        job.RetryAt = job.State == CollectionJobState.WaitingToRetry
            ? Ticks(CollectionJobLifecycle.RetryAt(now, definition.Policy, attempt.Sequence, resolution.RetryDelay)) : null;
        attempt.ResolutionJson = resolutionJson;
        attempt.CompletedAt = Ticks(now);
        var origin = await db.Set<JobOriginRow>().SingleAsync(item => item.Origin == NormalizeOrigin(definition.AllowedOrigin), cancellationToken);
        if (origin.UnresolvedAttemptId != attemptId) throw new InvalidOperationException("Origin ownership does not match the unresolved attempt.");
        var nextOrigin = AddDelay(now, TimeSpan.FromMilliseconds(request.MinDelayMilliseconds));
        if (evidence?.AttemptResult is DeferredAttemptResult)
        {
            var backoff = CollectionJobLifecycle.RetryAt(now, definition.Policy, attempt.Sequence, resolution.RetryDelay);
            if (backoff > nextOrigin) nextOrigin = backoff;
        }
        origin.NotBefore = Math.Max(origin.NotBefore, Ticks(nextOrigin));
        origin.UnresolvedAttemptId = null;
        return true;
    }, cancellationToken);

    public Task<bool> ReleaseClaimAsync(CollectionJobLease jobLease, CancellationToken cancellationToken) =>
        TransactionAsync(async (db, now) =>
        {
            var row = await OwnedJobAsync(db, jobLease, now, cancellationToken);
            if (row is null) return false;
            row.LeaseToken = null;
            row.LeaseExpiresAt = null;
            return true;
        }, cancellationToken);

    private static void ValidateResolution(CollectionAttemptResolution resolution, CollectionRequest request)
    {
        ArgumentNullException.ThrowIfNull(resolution);
        if (!Enum.IsDefined(resolution.Outcome) || resolution.RetryDelay < TimeSpan.Zero)
            throw new ArgumentException("Invalid job attempt resolution.");
        if (resolution.Outcome == CollectionJobAttemptOutcome.Interrupted && resolution.Receipt is not null)
            throw new ArgumentException("Interrupted execution cannot replace a completed receipt.");
        if (resolution.Receipt is not { } receipt) return;
        receipt.ValidateAgainst(request);
        var expected = CollectionJobLifecycle.Resolve(receipt);
        if (expected.Outcome != resolution.Outcome || expected.ErrorCode != resolution.ErrorCode || expected.RetryDelay != resolution.RetryDelay)
            throw new ArgumentException("Job resolution does not match its receipt.");
    }

    private async Task<T> TransactionAsync<T>(Func<CollectionAttemptDbContext, DateTimeOffset, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText = "SELECT pg_advisory_xact_lock(hashtextextended(@name, 0))";
        command.Parameters.Add(new NpgsqlParameter("name", LockName));
        await command.ExecuteNonQueryAsync(cancellationToken);
        command.Parameters.Clear();
        command.CommandText = "SELECT clock_timestamp()";
        var now = new DateTimeOffset((DateTime)(await command.ExecuteScalarAsync(cancellationToken))!);
        var result = await operation(db, now);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    private static async Task<JobRow?> OwnedJobAsync(CollectionAttemptDbContext db, CollectionJobLease lease,
        DateTimeOffset now, CancellationToken cancellationToken) =>
        await db.Set<JobRow>().SingleOrDefaultAsync(row => row.JobId == lease.JobId && row.LeaseToken == lease.Token &&
            row.LeaseFence == lease.Fence && row.LeaseExpiresAt > Ticks(now), cancellationToken);

    private static bool OwnsSlot(CollectorSlotRow? slot, CollectionJobLease job, CollectionCollectorLease lease, DateTimeOffset now) =>
        slot is not null && slot.JobId == job.JobId && slot.Token == lease.Token && slot.Fence == lease.Fence && slot.ExpiresAt > Ticks(now);

    private static CollectionJobLease Lease(JobRow row) => new(row.JobId, row.LeaseToken!, row.LeaseFence, FromTicks(row.LeaseExpiresAt)!.Value);
    private static CollectionCollectorLease CollectorLease(CollectorSlotRow row) => new(row.Token!, row.Fence, FromTicks(row.ExpiresAt)!.Value);
    private static CollectionJobStartResult Blocked(CollectionJobBlockReason reason, DateTimeOffset? retryAt = null) =>
        new(CollectionJobStartStatus.Blocked, BlockReason: reason, RetryAt: retryAt);
    private static long Ticks(DateTimeOffset value) => value.UtcDateTime.Ticks;
    private static DateTimeOffset? FromTicks(long? value) => value is { } ticks ? new DateTimeOffset(ticks, TimeSpan.Zero) : null;
    private static string NormalizeOrigin(string value)
    {
        var uri = new Uri(value);
        return $"{uri.Scheme.ToLowerInvariant()}://{uri.IdnHost.ToLowerInvariant()}:{uri.Port}";
    }
    private static string Write<T>(T value) => JsonSerializer.Serialize(value, CollectionProtocol.JsonOptions);
    private static T Read<T>(string value) => JsonSerializer.Deserialize<T>(value, CollectionProtocol.JsonOptions) ?? throw new InvalidDataException("Missing job data.");
    private static DateTimeOffset AddDelay(DateTimeOffset now, TimeSpan delay) => delay > DateTimeOffset.MaxValue - now ? DateTimeOffset.MaxValue : now + delay;
    private static void ValidateLeaseDuration(TimeSpan duration)
    {
        if (duration < TimeSpan.FromSeconds(3) || duration > TimeSpan.FromMinutes(10))
            throw new ArgumentOutOfRangeException(nameof(duration), "Job leases must be 3 to 600 seconds.");
    }

    private static CollectionJobAttempt ToAttempt(JobAttemptRow row) => new(row.AttemptId, Read<CollectionRequest>(row.RequestJson),
        row.ResolutionJson is null ? null : Read<CollectionAttemptResolution>(row.ResolutionJson),
        FromTicks(row.StartedAt)!.Value, FromTicks(row.CompletedAt));

    private static async Task<CollectionJobRecord> ToRecordAsync(CollectionAttemptDbContext db, JobRow row, CancellationToken cancellationToken)
    {
        var attempts = await db.Set<JobAttemptRow>().Where(attempt => attempt.JobId == row.JobId).OrderBy(attempt => attempt.Sequence).ToListAsync(cancellationToken);
        return ToRecord(row, attempts);
    }

    private static CollectionJobRecord ToRecord(JobRow row, IEnumerable<JobAttemptRow> attempts) =>
        new(row.JobId, Read<CollectionJobDefinition>(row.DefinitionJson), row.IdempotencyKey,
            row.State, FromTicks(row.CreatedAt)!.Value, FromTicks(row.RetryAt), row.CancellationRequested,
            row.ChargedRequests, row.ChargedBytes, row.ChargedSeconds, attempts.Select(ToAttempt).ToArray());
}
