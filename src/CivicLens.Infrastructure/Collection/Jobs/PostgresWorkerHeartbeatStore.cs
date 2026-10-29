using CivicLens.Application;
using CivicLens.Infrastructure.Collection.Jobs.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace CivicLens.Infrastructure.Collection.Jobs;

/// <summary>Keeps one row for each work loop. Every write and every age uses the database clock.</summary>
public sealed class PostgresWorkerHeartbeatStore(IDbContextFactory<CollectionAttemptDbContext> contextFactory) : IWorkerHeartbeatStore
{
    public static PostgresWorkerHeartbeatStore FromConnectionString(string connectionString)
    {
        _ = PostgresCollectionJobStore.FromConnectionString(connectionString);
        var options = new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options;
        return new(new PooledDbContextFactory<CollectionAttemptDbContext>(options));
    }

    public async Task BeatAsync(string loop, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(loop);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        _ = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO worker_heartbeat (loop, beat_at_utc_ticks)
            VALUES ({loop}, {(await DatabaseClock.ReadAsync(db, cancellationToken)).Ticks})
            ON CONFLICT (loop) DO UPDATE SET beat_at_utc_ticks = EXCLUDED.beat_at_utc_ticks
            """, cancellationToken);
    }

    public async Task ClearAsync(CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        _ = await db.Set<WorkerHeartbeatRow>().ExecuteDeleteAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<WorkerHeartbeat>> ListAsync(CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var now = await DatabaseClock.ReadAsync(db, cancellationToken);
        var rows = await db.Set<WorkerHeartbeatRow>().AsNoTracking().OrderBy(row => row.Loop).ToListAsync(cancellationToken);
        return [.. rows.Select(row => new WorkerHeartbeat(row.Loop, TimeSpan.FromTicks(now.Ticks - row.BeatAtUtcTicks)))];
    }
}
