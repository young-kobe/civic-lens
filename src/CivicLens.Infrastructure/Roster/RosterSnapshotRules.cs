using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Collection.Persistence;
using CivicLens.Infrastructure.Roster.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Infrastructure.Roster;

/// <summary>Owns the snapshot rules that every roster source shares. A snapshot is a header row in roster_snapshots.</summary>
internal static class RosterSnapshotRules
{
    public static async Task<bool> HasSnapshotOfCaptureAsync(CollectionAttemptDbContext db, string attemptId,
        CancellationToken cancellationToken)
    {
        var attempt = await db.Attempts.AsNoTracking().SingleOrDefaultAsync(row => row.AttemptId == attemptId, cancellationToken);
        if (attempt?.CaptureSha256 is null)
            throw new InvalidOperationException("A roster snapshot needs an imported captured attempt.");
        return await SnapshotAttempts(db, attempt.SourceId).AnyAsync(snapshot => snapshot.CaptureSha256 == attempt.CaptureSha256,
            cancellationToken);
    }

    public static void AddHeader(CollectionAttemptDbContext db, string attemptId) =>
        db.Add(new RosterSnapshotRow { AttemptId = attemptId });

    public static async Task<string?> ReadLatestSnapshotAttemptIdAsync(CollectionAttemptDbContext db, string sourceId,
        CancellationToken cancellationToken)
    {
        var snapshots = SnapshotAttempts(db, sourceId);
        var newestCapture = await db.Attempts.AsNoTracking()
            .Where(attempt => attempt.SourceId == sourceId && attempt.CaptureSha256 != null &&
                snapshots.Any(snapshot => snapshot.CaptureSha256 == attempt.CaptureSha256))
            .OrderByDescending(attempt => attempt.ObservedAtUtcTicks).ThenByDescending(attempt => attempt.AttemptId)
            .FirstOrDefaultAsync(cancellationToken);
        if (newestCapture is null) return null;
        return await snapshots.Where(candidate => candidate.CaptureSha256 == newestCapture.CaptureSha256)
            .OrderBy(candidate => candidate.ObservedAtUtcTicks).ThenBy(candidate => candidate.AttemptId)
            .Select(candidate => candidate.AttemptId).FirstAsync(cancellationToken);
    }

    private static IQueryable<AttemptRow> SnapshotAttempts(CollectionAttemptDbContext db, string sourceId) =>
        from attempt in db.Attempts.AsNoTracking()
        where attempt.SourceId == sourceId
        join snapshot in db.Set<RosterSnapshotRow>().AsNoTracking() on attempt.AttemptId equals snapshot.AttemptId
        select attempt;
}
