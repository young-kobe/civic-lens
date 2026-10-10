using CivicLens.Application.Analysis;
using CivicLens.Infrastructure.Analysis.Persistence;
using CivicLens.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace CivicLens.Infrastructure.Analysis;

public sealed class PostgresDocumentChangeAnalysisStatusStore(IDbContextFactory<CollectionAttemptDbContext> contextFactory)
    : IDocumentChangeAnalysisStatusStore
{
    private const string ClockTicksSql = "(EXTRACT(EPOCH FROM clock_timestamp())::numeric * 10000000 + 621355968000000000)::bigint";

    public static PostgresDocumentChangeAnalysisStatusStore FromConnectionString(string connectionString)
    {
        _ = PostgresCollectionAttemptStore.FromConnectionString(connectionString);
        var options = new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options;
        return new(new PooledDbContextFactory<CollectionAttemptDbContext>(options));
    }

    public async Task<DocumentChangeAnalysisStatus?> GetAsync(CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var row = await db.Set<DocumentChangeAnalysisStatusRow>().AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Task == DocumentChangeDraftingTask.Task, cancellationToken);
        return row is null ? null : new(row.DailyTokenLimit, new DateTimeOffset(row.RecordedAtUtcTicks, TimeSpan.Zero),
            row.PausedReason, row.PausedUntilUtcTicks is { } until ? new DateTimeOffset(until, TimeSpan.Zero) : null);
    }

    public async Task RecordStartAsync(long? dailyTokenLimit, CancellationToken cancellationToken)
    {
        if (dailyTokenLimit is < 1) throw new ArgumentOutOfRangeException(nameof(dailyTokenLimit));
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        _ = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO document_change_analysis_status (task, daily_token_limit, recorded_at_utc_ticks)
            VALUES ({DocumentChangeDraftingTask.Task}, {dailyTokenLimit}, {await ReadTicksAsync(db, cancellationToken)})
            ON CONFLICT (task) DO UPDATE SET daily_token_limit = EXCLUDED.daily_token_limit,
                recorded_at_utc_ticks = EXCLUDED.recorded_at_utc_ticks, paused_reason = NULL, paused_until_utc_ticks = NULL
            """, cancellationToken);
    }

    public async Task RecordPauseAsync(string reason, TimeSpan? duration, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (reason.Length > 64 || duration < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(reason));
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        long? until = duration is { } pause ? await ReadTicksAsync(db, cancellationToken) + pause.Ticks : null;
        _ = await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE document_change_analysis_status SET paused_reason = {reason}, paused_until_utc_ticks = {until}
            WHERE task = {DocumentChangeDraftingTask.Task} AND (paused_reason IS NULL OR paused_until_utc_ticks IS NOT NULL)
            """, cancellationToken);
    }

    public async Task ClearOutagePauseAsync(CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        _ = await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE document_change_analysis_status SET paused_reason = NULL, paused_until_utc_ticks = NULL
            WHERE task = {DocumentChangeDraftingTask.Task} AND paused_until_utc_ticks IS NOT NULL
            """, cancellationToken);
    }

    private static Task<long> ReadTicksAsync(CollectionAttemptDbContext db, CancellationToken cancellationToken) =>
        db.Database.SqlQueryRaw<long>($"SELECT {ClockTicksSql} AS \"Value\"").SingleAsync(cancellationToken);
}
