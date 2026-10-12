using CivicLens.Application.Analysis;
using CivicLens.Infrastructure.Analysis.Persistence;
using CivicLens.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace CivicLens.Infrastructure.Analysis;

public sealed class PostgresDocumentChangeAnalysisStatusStore(IDbContextFactory<CollectionAttemptDbContext> contextFactory,
    string task = DocumentChangeDraftingTask.Task) : IDocumentChangeAnalysisStatusStore
{
    public static PostgresDocumentChangeAnalysisStatusStore FromConnectionString(string connectionString,
        string task = DocumentChangeDraftingTask.Task)
    {
        _ = PostgresCollectionAttemptStore.FromConnectionString(connectionString);
        var options = new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options;
        return new(new PooledDbContextFactory<CollectionAttemptDbContext>(options), task);
    }

    public async Task<DocumentChangeAnalysisStatus?> GetAsync(CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var row = await db.Set<DocumentChangeAnalysisStatusRow>().AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Task == task, cancellationToken);
        return row is null ? null : ToStatus(row);
    }

    public async Task RecordStartAsync(long? dailyTokenLimit, CancellationToken cancellationToken)
    {
        if (dailyTokenLimit is < 1) throw new ArgumentOutOfRangeException(nameof(dailyTokenLimit));
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        _ = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO document_change_analysis_status (task, daily_token_limit, recorded_at_utc_ticks)
            VALUES ({task}, {dailyTokenLimit}, {(await DatabaseClock.ReadAsync(db, cancellationToken)).Ticks})
            ON CONFLICT (task) DO UPDATE SET daily_token_limit = EXCLUDED.daily_token_limit,
                recorded_at_utc_ticks = EXCLUDED.recorded_at_utc_ticks, paused_reason = NULL, paused_until_utc_ticks = NULL
            """, cancellationToken);
    }

    public async Task RecordPauseAsync(string reason, TimeSpan? duration, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (reason.Length > 64 || duration < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(reason));
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var row = await LockAsync(db, cancellationToken);
        if (row is null || ToStatus(row).IsHardPaused) return;
        var now = await DatabaseClock.ReadAsync(db, cancellationToken);
        row.PausedReason = reason;
        row.PausedUntilUtcTicks = duration is { } pause ? now.Ticks + pause.Ticks : null;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task ClearOutagePauseAsync(CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var row = await LockAsync(db, cancellationToken);
        if (row is null || !ToStatus(row).IsTimedPaused) return;
        row.PausedReason = null;
        row.PausedUntilUtcTicks = null;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    // The row lock keeps the pause rule and its write in one step. A restart or a second pause cannot slip between them.
    private Task<DocumentChangeAnalysisStatusRow?> LockAsync(CollectionAttemptDbContext db,
        CancellationToken cancellationToken) => db.Set<DocumentChangeAnalysisStatusRow>().FromSqlInterpolated($"""
            SELECT * FROM document_change_analysis_status WHERE task = {task} FOR UPDATE
            """).SingleOrDefaultAsync(cancellationToken);

    private static DocumentChangeAnalysisStatus ToStatus(DocumentChangeAnalysisStatusRow row) =>
        new(row.DailyTokenLimit, new DateTimeOffset(row.RecordedAtUtcTicks, TimeSpan.Zero), row.PausedReason,
            row.PausedUntilUtcTicks is { } until ? new DateTimeOffset(until, TimeSpan.Zero) : null);
}
