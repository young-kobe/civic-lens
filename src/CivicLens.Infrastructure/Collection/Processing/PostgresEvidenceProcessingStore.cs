using System.Data;
using System.Text.Json;
using CivicLens.Collection.Contracts;
using CivicLens.Application.Collection.Processing;
using CivicLens.Core.Documents;
using CivicLens.Infrastructure.Collection.Jobs.Persistence;
using CivicLens.Infrastructure.Collection.Jobs;
using CivicLens.Infrastructure.Collection.Persistence;
using CivicLens.Infrastructure.Documents.Persistence;
using CivicLens.Infrastructure.Documents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace CivicLens.Infrastructure.Collection.Processing;

/// <summary>Fenced durable checkpoints for collection evidence processing.</summary>
public sealed class PostgresEvidenceProcessingStore(
    IDbContextFactory<CollectionAttemptDbContext> contextFactory) : IEvidenceProcessingStore
{
    public static PostgresEvidenceProcessingStore FromConnectionString(string connectionString)
    {
        _ = PostgresCollectionJobStore.FromConnectionString(connectionString);
        var options = new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options;
        return new(new PooledDbContextFactory<CollectionAttemptDbContext>(options));
    }

    public async Task<IReadOnlyList<EvidenceProcessingRecord>> GetByJobIdsAsync(IReadOnlyList<string> jobIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(jobIds);
        if (jobIds.Count > 100) throw new ArgumentOutOfRangeException(nameof(jobIds));
        if (jobIds.Count == 0) return [];
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var rows = await db.Set<PersistedEvidenceProcessingRow>().AsNoTracking()
            .Where(row => jobIds.Contains(row.JobId)).OrderBy(row => row.JobId).ThenBy(row => row.AttemptId)
            .ToArrayAsync(cancellationToken);
        return rows.Select(ToRecord).ToArray();
    }

    public async Task<IReadOnlyList<EvidenceProcessingRecord>> GetEligibleAsync(int limit,
        CancellationToken cancellationToken)
    {
        if (limit is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(limit));
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var now = (await ReadDatabaseNowAsync(db, cancellationToken)).Ticks;
        var rows = await db.Set<PersistedEvidenceProcessingRow>().AsNoTracking()
            .Where(row => row.Status == EvidenceProcessingStatus.Pending ||
                 row.Status == EvidenceProcessingStatus.RetryWaiting && (row.RetryAt == null || row.RetryAt <= now) ||
                 row.Status == EvidenceProcessingStatus.Running && row.LeaseExpiresAt <= now)
            .OrderBy(row => row.ObservedAtUtcTicks).ThenBy(row => row.AttemptId).ThenBy(row => row.JobId).Take(limit)
            .ToArrayAsync(cancellationToken);
        return rows.Select(ToRecord).ToArray();
    }

    public async Task<EvidenceProcessingRecord?> GetByAttemptIdAsync(string attemptId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(attemptId);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var row = await db.Set<PersistedEvidenceProcessingRow>().AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.AttemptId == attemptId, cancellationToken);
        return row is null ? null : ToRecord(row);
    }

    public async Task<bool> IsAwaitingPreparationAsync(string attemptId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(attemptId);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await IsAwaitingPreparationAsync(db, attemptId, cancellationToken);
    }

    private static async Task<bool> IsAwaitingPreparationAsync(CollectionAttemptDbContext db, string attemptId,
        CancellationToken cancellationToken)
    {
        var owner = await (from attempt in db.Set<JobAttemptRow>().AsNoTracking()
                           join job in db.Set<JobRow>().AsNoTracking() on attempt.JobId equals job.JobId
                           where attempt.AttemptId == attemptId
                           select new { job.JobId, job.State }).SingleOrDefaultAsync(cancellationToken);
        if (owner is null) return false;
        if (owner.State != Application.Collection.Jobs.CollectionJobState.Succeeded)
            return owner.State is Application.Collection.Jobs.CollectionJobState.Pending or
                Application.Collection.Jobs.CollectionJobState.Running or
                Application.Collection.Jobs.CollectionJobState.WaitingToRetry;
        var preparation = await db.Set<PersistedEvidenceProcessingRow>().AsNoTracking()
            .SingleOrDefaultAsync(row => row.JobId == owner.JobId && row.AttemptId == "prepare", cancellationToken);
        return preparation is null || preparation.Stage == EvidenceProcessingStage.Preparation &&
            preparation.Status is EvidenceProcessingStatus.Pending or EvidenceProcessingStatus.Running or EvidenceProcessingStatus.RetryWaiting;
    }

    public async Task<EvidenceProcessingRecord> EnsureAsync(string jobId, string attemptId, string sourceId,
        string requestedUrl, CancellationToken cancellationToken)
    {
        ValidateIdentity(jobId, attemptId, sourceId, requestedUrl);
        if (attemptId == "prepare") throw new ArgumentException("Attempt ID is reserved for job preparation.", nameof(attemptId));
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var belongsToJob = await db.Set<JobAttemptRow>().AnyAsync(row => row.JobId == jobId &&
            row.AttemptId == attemptId, cancellationToken);
        var jobSucceeded = await db.Set<JobRow>().AnyAsync(row => row.JobId == jobId &&
            row.State == Application.Collection.Jobs.CollectionJobState.Succeeded, cancellationToken);
        var captured = await db.Attempts.AsNoTracking().SingleOrDefaultAsync(row => row.AttemptId == attemptId,
            cancellationToken);
        if (!belongsToJob || !jobSucceeded || captured is null || captured.Outcome != "captured" ||
            captured.SourceId != sourceId || captured.RequestedUrl != requestedUrl)
            throw new InvalidDataException("Processing requires a captured attempt owned by the specified collection job.");
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        _ = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO evidence_processing (job_id, attempt_id, source_id, requested_url, stage, status, fence,
                observed_at_utc_ticks, attempts, admitted_count, deferred_count, duplicate_count)
            VALUES ({jobId}, {attemptId}, {sourceId}, {requestedUrl}, 'Extraction', 'Pending', 0, {captured.ObservedAtUtcTicks}, 0, 0, 0, 0)
            ON CONFLICT (job_id, attempt_id) DO NOTHING
            """, cancellationToken);
        var row = await db.Set<PersistedEvidenceProcessingRow>().SingleOrDefaultAsync(
            candidate => candidate.JobId == jobId && candidate.AttemptId == attemptId, cancellationToken);
        if (row is null) throw new InvalidOperationException("Processing record insert did not return a row.");
        else if (row.SourceId != sourceId || row.RequestedUrl != requestedUrl)
            throw new InvalidDataException("Processing identity was replayed with different source evidence.");
        await transaction.CommitAsync(cancellationToken);
        return ToRecord(row);
    }

    public async Task<EvidenceProcessingClaim?> TryClaimAsync(string jobId, string attemptId, TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        EvidenceProcessingPolicy.ValidateLeaseDuration(leaseDuration);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var token = Guid.NewGuid().ToString("N");
        var changed = await db.Database.ExecuteSqlInterpolatedAsync($$"""
            UPDATE evidence_processing SET lease_token = {{token}}, lease_expires_at =
                ((EXTRACT(EPOCH FROM clock_timestamp())::numeric * 10000000 + 621355968000000000)::bigint) + {{leaseDuration.Ticks}},
                fence = fence + 1, attempts = attempts + 1, status = 'Running', retry_at = NULL
            WHERE job_id = {{jobId}} AND attempt_id = {{attemptId}} AND
                (status = 'Pending' OR status = 'RetryWaiting' AND (retry_at IS NULL OR retry_at <= ((EXTRACT(EPOCH FROM clock_timestamp())::numeric * 10000000 + 621355968000000000)::bigint)) OR
                 status = 'Running' AND lease_expires_at <= ((EXTRACT(EPOCH FROM clock_timestamp())::numeric * 10000000 + 621355968000000000)::bigint))
            """, cancellationToken);
        if (changed != 1) return null;
        var row = await db.Set<PersistedEvidenceProcessingRow>().AsNoTracking().SingleAsync(
            candidate => candidate.JobId == jobId && candidate.AttemptId == attemptId && candidate.LeaseToken == token,
            cancellationToken);
        return new(ToRecord(row), token, row.Fence);
    }

    public async Task<bool> CheckpointAsync(EvidenceProcessingCheckpoint checkpoint, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        if (checkpoint.AdmittedCount < 0 || checkpoint.DeferredCount < 0 || checkpoint.DuplicateCount < 0)
            throw new ArgumentOutOfRangeException(nameof(checkpoint), "Preparation counts cannot be negative.");
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var row = await db.Set<PersistedEvidenceProcessingRow>().AsNoTracking().SingleOrDefaultAsync(
            candidate => candidate.JobId == checkpoint.JobId && candidate.AttemptId == checkpoint.AttemptId,
            cancellationToken);
        if (row is null ||
            !EvidenceProcessingLifecycle.CanTransition(ToRecord(row), checkpoint)) return false;
        var effectiveExtractionId = checkpoint.ExtractionId ?? row.ExtractionId;
        var effectiveComparisonId = checkpoint.ComparisonId ?? row.ComparisonId;
        if (effectiveExtractionId is not null)
        {
            var extractionAttempt = await db.Set<DocumentExtractionRow>().AsNoTracking()
                .Where(candidate => candidate.ExtractionId == effectiveExtractionId)
                .Select(candidate => candidate.AttemptId).SingleOrDefaultAsync(cancellationToken);
            if (extractionAttempt != row.AttemptId) return false;
        }
        if (effectiveComparisonId is not null)
        {
            var comparisonRow = await db.Set<DocumentComparisonRow>().AsNoTracking()
                .SingleOrDefaultAsync(candidate => candidate.ComparisonId == effectiveComparisonId, cancellationToken);
            if (comparisonRow is null || comparisonRow.AfterExtractionId != effectiveExtractionId) return false;
            var before = await PostgresDocumentExtractionStore.ReadAsync(db, comparisonRow.BeforeExtractionId, cancellationToken);
            var after = await PostgresDocumentExtractionStore.ReadAsync(db, comparisonRow.AfterExtractionId, cancellationToken);
            if (before is null || after is null) return false;
            var verified = DocumentComparison.Create(before, after, cancellationToken);
            if (verified.Status != DocumentComparisonStatus.Complete ||
                verified.ComparisonId != effectiveComparisonId || verified.AlgorithmVersion != comparisonRow.AlgorithmVersion ||
                verified.SettingsVersion != comparisonRow.SettingsVersion ||
                JsonSerializer.Serialize(verified, CollectionProtocol.JsonOptions) != comparisonRow.ResultJson ||
                checkpoint.Outcome == EvidenceProcessingOutcome.Changed && verified.Hunks.IsEmpty ||
                checkpoint.Outcome == EvidenceProcessingOutcome.Unchanged && !verified.Hunks.IsEmpty ||
                checkpoint.Outcome is not (EvidenceProcessingOutcome.Changed or EvidenceProcessingOutcome.Unchanged))
                return false;
        }
        var changed = await db.Database.ExecuteSqlInterpolatedAsync($$"""
            UPDATE evidence_processing SET
                stage = {{checkpoint.Stage.ToString()}}, status = {{checkpoint.Status.ToString()}},
                attempts = CASE WHEN {{checkpoint.Stage != checkpoint.ExpectedStage}} THEN 0 ELSE attempts END,
                extraction_id = {{effectiveExtractionId}}, comparison_id = {{effectiveComparisonId}},
                outcome = {{checkpoint.Outcome?.ToString()}}, error_code = {{checkpoint.ErrorCode}},
                retry_at = CASE WHEN {{checkpoint.Status == EvidenceProcessingStatus.RetryWaiting}}
                    THEN ((EXTRACT(EPOCH FROM clock_timestamp())::numeric * 10000000 + 621355968000000000)::bigint) + {{checkpoint.RetryDelay?.Ticks ?? 0}}
                    ELSE NULL END,
                admitted_count = {{checkpoint.AdmittedCount}}, deferred_count = {{checkpoint.DeferredCount}},
                duplicate_count = {{checkpoint.DuplicateCount}}, lease_token = NULL, lease_expires_at = NULL
            WHERE job_id = {{checkpoint.JobId}} AND attempt_id = {{checkpoint.AttemptId}} AND
                stage = {{checkpoint.ExpectedStage.ToString()}} AND status = {{checkpoint.ExpectedStatus.ToString()}} AND
                lease_token = {{checkpoint.LeaseToken}} AND fence = {{checkpoint.Fence}} AND
                lease_expires_at > ((EXTRACT(EPOCH FROM clock_timestamp())::numeric * 10000000 + 621355968000000000)::bigint)
            """, cancellationToken);
        if (changed != 1) return false;
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<bool> RenewAsync(EvidenceProcessingClaim claim, TimeSpan leaseDuration, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(claim);
        EvidenceProcessingPolicy.ValidateLeaseDuration(leaseDuration);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Database.ExecuteSqlInterpolatedAsync($$"""
            UPDATE evidence_processing SET lease_expires_at =
                ((EXTRACT(EPOCH FROM clock_timestamp())::numeric * 10000000 + 621355968000000000)::bigint) + {{leaseDuration.Ticks}}
            WHERE job_id = {{claim.Record.JobId}} AND attempt_id = {{claim.Record.AttemptId}} AND status = 'Running' AND
                lease_token = {{claim.LeaseToken}} AND fence = {{claim.Fence}} AND lease_expires_at >
                ((EXTRACT(EPOCH FROM clock_timestamp())::numeric * 10000000 + 621355968000000000)::bigint)
            """, cancellationToken) == 1;
    }

    public async Task<bool> WaitForPredecessorAsync(EvidenceProcessingClaim claim, string predecessorAttemptId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(claim);
        ArgumentException.ThrowIfNullOrWhiteSpace(predecessorAttemptId);
        if (predecessorAttemptId.Length > 128) throw new ArgumentException("Attempt ID exceeds its storage bound.", nameof(predecessorAttemptId));
        if (claim.Record.Stage != EvidenceProcessingStage.Comparison)
            throw new ArgumentException("Only comparison work can wait for a predecessor.", nameof(claim));

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(9911, hashtext({predecessorAttemptId}))", cancellationToken);

        var predecessor = await db.Set<PersistedEvidenceProcessingRow>().AsNoTracking()
            .SingleOrDefaultAsync(row => row.AttemptId == predecessorAttemptId, cancellationToken);
        var waiting = predecessor is null
            ? await IsAwaitingPreparationAsync(db, predecessorAttemptId, cancellationToken)
            : predecessor.ExtractionId is null && predecessor.Status is EvidenceProcessingStatus.Pending or
                EvidenceProcessingStatus.Running or EvidenceProcessingStatus.RetryWaiting or
                EvidenceProcessingStatus.WaitingForPredecessor;
        var changed = await ParkOrRequeueAsync(db, claim, predecessorAttemptId, waiting, cancellationToken);
        if (changed) await transaction.CommitAsync(cancellationToken);
        return changed;
    }

    private static async Task<bool> ParkOrRequeueAsync(CollectionAttemptDbContext db, EvidenceProcessingClaim claim,
        string predecessorAttemptId, bool waiting, CancellationToken cancellationToken) =>
        await db.Database.ExecuteSqlInterpolatedAsync($$"""
            UPDATE evidence_processing SET status = {{(waiting ? "WaitingForPredecessor" : "Pending")}},
                predecessor_attempt_id = {{(waiting ? predecessorAttemptId : null)}}, attempts = GREATEST(attempts - 1, 0),
                error_code = NULL, retry_at = NULL, lease_token = NULL, lease_expires_at = NULL
            WHERE job_id = {{claim.Record.JobId}} AND attempt_id = {{claim.Record.AttemptId}} AND
                stage = 'Comparison' AND status = 'Running' AND lease_token = {{claim.LeaseToken}} AND
                fence = {{claim.Fence}} AND lease_expires_at >
                ((EXTRACT(EPOCH FROM clock_timestamp())::numeric * 10000000 + 621355968000000000)::bigint)
            """, cancellationToken) == 1;

    public async Task<bool> ReleaseAsync(EvidenceProcessingClaim claim, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(claim);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Database.ExecuteSqlInterpolatedAsync($$"""
            UPDATE evidence_processing SET lease_token = NULL, lease_expires_at = NULL, status = 'Pending'
            WHERE job_id = {{claim.Record.JobId}} AND attempt_id = {{claim.Record.AttemptId}} AND status = 'Running' AND
                lease_token = {{claim.LeaseToken}} AND fence = {{claim.Fence}} AND lease_expires_at >
                ((EXTRACT(EPOCH FROM clock_timestamp())::numeric * 10000000 + 621355968000000000)::bigint)
            """, cancellationToken) == 1;
    }

    private static EvidenceProcessingRecord ToRecord(PersistedEvidenceProcessingRow row) => new(row.JobId,
        row.AttemptId, row.SourceId, row.RequestedUrl, row.Stage, row.Status, row.LeaseToken, row.Fence,
        row.Attempts, row.RetryAt is { } retry ? new DateTimeOffset(retry, TimeSpan.Zero) : null,
        row.ExtractionId, row.ComparisonId, row.Outcome, row.ErrorCode, row.AdmittedCount, row.DeferredCount,
        row.DuplicateCount, row.PredecessorAttemptId);

    private static async Task<DateTime> ReadDatabaseNowAsync(CollectionAttemptDbContext db, CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        var alreadyOpen = connection.State == ConnectionState.Open;
        if (!alreadyOpen) await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT clock_timestamp()";
            var value = await command.ExecuteScalarAsync(cancellationToken)
                ?? throw new InvalidOperationException("Postgres did not return database time.");
            return DateTime.SpecifyKind((DateTime)value, DateTimeKind.Utc);
        }
        finally { if (!alreadyOpen) await db.Database.CloseConnectionAsync(); }
    }

    private static void ValidateIdentity(string jobId, string attemptId, string sourceId, string requestedUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);
        ArgumentException.ThrowIfNullOrWhiteSpace(attemptId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestedUrl);
        if (jobId.Length > 128 || attemptId.Length > 128 || sourceId.Length > 128 || requestedUrl.Length > 4096)
            throw new ArgumentException("Processing identity exceeds its storage bound.");
    }
}
