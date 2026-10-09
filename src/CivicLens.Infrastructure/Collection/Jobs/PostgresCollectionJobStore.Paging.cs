using System.Data;
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
        IReadOnlyCollection<SourceCheckTarget> targets, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(targets);
        if (targets.Count == 0) return [];
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var rows = await db.Set<JobRow>().FromSqlRaw("""
            SELECT DISTINCT ON (j.definition_json::jsonb ->> 'sourceId') j.*
              FROM collection_jobs j
              JOIN unnest(@sourceIds, @urls) AS t(source_id, url)
                ON j.definition_json::jsonb ->> 'sourceId' = t.source_id AND j.definition_json::jsonb ->> 'url' = t.url
             ORDER BY j.definition_json::jsonb ->> 'sourceId', j.created_at DESC, j.job_id
            """,
            new NpgsqlParameter("sourceIds", NpgsqlDbType.Array | NpgsqlDbType.Text)
            { Value = targets.Select(target => target.SourceId).ToArray() },
            new NpgsqlParameter("urls", NpgsqlDbType.Array | NpgsqlDbType.Text)
            { Value = targets.Select(target => target.Url).ToArray() })
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
            .Take(limit).Select(row => new JobTime(row.JobId, row.DefinitionJson, row.CreatedAt)).ToListAsync(cancellationToken);
        var failed = await (from row in db.Set<JobRow>().AsNoTracking()
                            where row.State == CollectionJobState.Failed
                            let failedAt = db.Set<JobAttemptRow>().Where(attempt => attempt.JobId == row.JobId)
                                .Max(attempt => attempt.CompletedAt) ?? row.CreatedAt
                            orderby failedAt descending, row.JobId
                            select new JobTime(row.JobId, row.DefinitionJson, failedAt)).Take(limit).ToListAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(started.Select(ToEvent).ToArray(), failed.Select(ToEvent).ToArray());
    }

    private sealed record JobTime(string JobId, string DefinitionJson, long Ticks);

    private static CollectionJobEvent ToEvent(JobTime job) =>
        new(job.JobId, ReadDefinition(job.DefinitionJson).SourceId, FromTicks(job.Ticks)!.Value);

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
        return rows.Select(row => ToRecord(row, attemptsByJob[row.JobId])).ToArray();
    }
}
