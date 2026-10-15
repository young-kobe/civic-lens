using System.Data;
using CivicLens.Application.Collection;
using CivicLens.Application.Collection.Jobs;
using CivicLens.Application.Paging;
using CivicLens.Infrastructure.Collection.Jobs.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace CivicLens.Infrastructure.Collection.Jobs;

public sealed partial class PostgresCollectionJobStore
{
    public async Task<CollectionJobPage> ListPageAsync(PageCursor? cursor, int limit, CancellationToken cancellationToken)
    {
        PageLimit.Validate(limit);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var fetched = await KeysetJobs(db.Set<JobRow>().AsNoTracking(), cursor).Take(limit + 1).ToListAsync(cancellationToken);
        var slice = KeysetSlice<JobRow>.Create(fetched, limit, cursor, row => (row.CreatedAt, row.JobId));
        var records = await ToRecordsAsync(db, slice.Items, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(records, slice.NewerCursor, slice.OlderCursor);
    }

    public async Task<IReadOnlyList<CollectionJobRecord>> ListLatestBySourceAsync(
        IReadOnlyCollection<string> sourceIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sourceIds);
        if (sourceIds.Count == 0) return [];
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var rows = await db.Set<JobRow>().FromSqlRaw("""
            SELECT latest.*
              FROM unnest(@sourceIds) AS source(source_id)
             CROSS JOIN LATERAL (
                   SELECT * FROM collection_jobs j
                    WHERE j.source_id = source.source_id AND j.is_root
                    ORDER BY j.created_at DESC, j.job_id
                    LIMIT 1) AS latest
            """,
            new NpgsqlParameter("sourceIds", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = sourceIds.ToArray() })
            .AsNoTracking().ToListAsync(cancellationToken);
        var records = await ToRecordsAsync(db, rows, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return records;
    }

    public async Task<CollectionJobActivity> ListActivityAsync(int limit, CancellationToken cancellationToken)
    {
        PageLimit.Validate(limit);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var started = await db.Set<JobRow>().AsNoTracking().OrderByDescending(row => row.CreatedAt).ThenBy(row => row.JobId)
            .Take(limit).Select(row => new JobTime(row.JobId, row.DefinitionJson, row.ConfigurationRevisionId, row.CreatedAt)).ToListAsync(cancellationToken);
        var failed = await (from row in db.Set<JobRow>().AsNoTracking()
                            where row.State == CollectionJobState.Failed
                            let failedAt = db.Set<JobAttemptRow>().Where(attempt => attempt.JobId == row.JobId)
                                .Max(attempt => attempt.CompletedAt) ?? row.CreatedAt
                            orderby failedAt descending, row.JobId
                            select new JobTime(row.JobId, row.DefinitionJson, row.ConfigurationRevisionId, failedAt)).Take(limit).ToListAsync(cancellationToken);
        var revisions = await StoredJobDefinition.LoadRevisionsAsync(db,
            started.Concat(failed).Select(job => job.ConfigurationRevisionId), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(started.Select(job => ToEvent(job, revisions)).ToArray(), failed.Select(job => ToEvent(job, revisions)).ToArray());
    }

    private sealed record JobTime(string JobId, string DefinitionJson, string? ConfigurationRevisionId, long Ticks);

    private static CollectionJobEvent ToEvent(JobTime job, IReadOnlyDictionary<string, CollectionConfigurationRevision> revisions) =>
        new(job.JobId, StoredJobDefinition.Read(job.DefinitionJson, job.ConfigurationRevisionId, revisions).SourceId, FromTicks(job.Ticks)!.Value);

    private static IQueryable<JobRow> KeysetJobs(IQueryable<JobRow> rows, PageCursor? cursor)
    {
        if (cursor is null) return rows.OrderByDescending(row => row.CreatedAt).ThenBy(row => row.JobId);
        var (ticks, id) = (cursor.Ticks, cursor.Id);
        return cursor.Direction == PageDirection.Older
            ? rows.Where(row => row.CreatedAt < ticks || row.CreatedAt == ticks && string.Compare(row.JobId, id) > 0)
                .OrderByDescending(row => row.CreatedAt).ThenBy(row => row.JobId)
            : rows.Where(row => row.CreatedAt > ticks || row.CreatedAt == ticks && string.Compare(row.JobId, id) < 0)
                .OrderBy(row => row.CreatedAt).ThenByDescending(row => row.JobId);
    }

    private static async Task<CollectionJobRecord[]> ToRecordsAsync(CollectionAttemptDbContext db,
        IReadOnlyCollection<JobRow> rows, CancellationToken cancellationToken)
    {
        var jobIds = rows.Select(row => row.JobId).ToArray();
        var attempts = await db.Set<JobAttemptRow>().AsNoTracking().Where(attempt => jobIds.Contains(attempt.JobId))
            .OrderBy(attempt => attempt.Sequence).ToListAsync(cancellationToken);
        var attemptsByJob = attempts.ToLookup(attempt => attempt.JobId);
        var revisions = await StoredJobDefinition.LoadRevisionsAsync(db, rows.Select(row => row.ConfigurationRevisionId), cancellationToken);
        return rows.Select(row => ToRecord(row, StoredJobDefinition.Read(row.DefinitionJson, row.ConfigurationRevisionId, revisions), attemptsByJob[row.JobId])).ToArray();
    }
}
