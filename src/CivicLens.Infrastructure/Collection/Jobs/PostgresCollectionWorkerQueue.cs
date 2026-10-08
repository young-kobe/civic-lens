using System.Collections.Immutable;
using CivicLens.Application.Collection.Jobs;
using CivicLens.Infrastructure.Collection.Jobs.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace CivicLens.Infrastructure.Collection.Jobs;

/// <summary>Keyset reads due jobs using database time and the durable job creation index.</summary>
public sealed class PostgresCollectionWorkerQueue(IDbContextFactory<CollectionAttemptDbContext> contextFactory)
    : ICollectionWorkerQueue
{
    private const int MaximumPageSize = 100;

    public static PostgresCollectionWorkerQueue FromConnectionString(string connectionString)
    {
        _ = PostgresCollectionJobStore.FromConnectionString(connectionString);
        var options = new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options;
        return new(new PooledDbContextFactory<CollectionAttemptDbContext>(options));
    }

    public async Task<CollectionWorkerQueuePage> GetEligibleJobIdsAsync(CollectionWorkerCursor? cursor,
        int limit, CancellationToken cancellationToken)
    {
        if (limit is < 1 or > MaximumPageSize) throw new ArgumentOutOfRangeException(nameof(limit));
        ValidateCursor(cursor);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await db.Database.OpenConnectionAsync(cancellationToken);
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT clock_timestamp()";
        var databaseNow = (DateTime)(await command.ExecuteScalarAsync(cancellationToken)
            ?? throw new InvalidOperationException("Postgres did not return database time."));
        var nowTicks = DateTime.SpecifyKind(databaseNow, DateTimeKind.Utc).Ticks;

        var query = db.Set<JobRow>().AsNoTracking().Where(row =>
            (row.LeaseExpiresAt == null || row.LeaseExpiresAt <= nowTicks) &&
            (row.State == CollectionJobState.Pending || row.State == CollectionJobState.Running ||
             row.State == CollectionJobState.WaitingToRetry && (row.RetryAt == null || row.RetryAt <= nowTicks)));
        if (cursor is not null)
            query = query.Where(row => row.CreatedAt > cursor.CreatedAtUtcTicks ||
                row.CreatedAt == cursor.CreatedAtUtcTicks && string.Compare(row.JobId, cursor.JobId) > 0);

        var rows = await query.OrderBy(row => row.CreatedAt).ThenBy(row => row.JobId)
            .Take(limit + 1).Select(row => new { row.JobId, row.CreatedAt }).ToArrayAsync(cancellationToken);
        var count = Math.Min(rows.Length, limit);
        var ids = rows.Take(count).Select(row => row.JobId).ToImmutableArray();
        var next = rows.Length > limit
            ? new CollectionWorkerCursor(rows[count - 1].CreatedAt, rows[count - 1].JobId)
            : null;
        await db.Database.CloseConnectionAsync();
        return new(ids, next);
    }

    private static void ValidateCursor(CollectionWorkerCursor? cursor)
    {
        if (cursor is null) return;
        if (cursor.CreatedAtUtcTicks is < 0 or > 3155378975999999999 ||
            string.IsNullOrWhiteSpace(cursor.JobId) || cursor.JobId.Length > 128)
            throw new ArgumentException("Worker queue cursor is invalid.", nameof(cursor));
    }
}
