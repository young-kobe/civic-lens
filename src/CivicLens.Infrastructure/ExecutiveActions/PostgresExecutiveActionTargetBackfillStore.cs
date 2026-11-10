using System.Collections.Immutable;
using CivicLens.Application.Review;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.ExecutiveActions.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Infrastructure.ExecutiveActions;

/// <summary>
/// Writes the disposition targets of revisions that have notes and no target rows. A batch takes the executive action review lock, the lock of the revision writer.
/// A revision whose notes hold no target stays selected, so each run examines it again. Examined counts the revisions that got targets.
/// Verify compares the stored targets of each revision to the parse of its notes. A revision with notes, a target in the parse, and no stored target is Legacy.
/// </summary>
public sealed class PostgresExecutiveActionTargetBackfillStore(IDbContextFactory<CollectionAttemptDbContext> contextFactory) : IRevisionBackfillStore
{
    private const int VerifyPageSize = 50;
    private const string TargetsKey = "dispositionTargets";

    public async Task<RevisionBackfillBatch> BackfillBatchAsync(RevisionKey? after, int size, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await PostgresAdvisoryLock.BeginWriteAsync(db, PostgresAdvisoryLock.ExecutiveActionReview, cancellationToken);
        var rows = await InKeyOrderAfter(db.Set<ExecutiveActionRevisionRow>().AsNoTracking()
            .Where(row => row.DispositionNotes != null && !row.DispositionTargets.Any()), after)
            .Select(row => new NotesRow(row.RecordId, row.RevisionNumber, row.DispositionNotes!)).Take(size).ToListAsync(cancellationToken);
        var written = 0;
        foreach (var row in rows)
        {
            var targets = ExecutiveActionRevisionRows.TargetRows(row.RecordId, row.RevisionNumber, row.Notes).ToList();
            db.Set<ExecutiveActionRevisionDispositionTargetRow>().AddRange(targets);
            if (targets.Count > 0) written++;
        }
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(written, ImmutableArray<SkippedRevision>.Empty, rows.Count == 0 ? null : new(rows[^1].RecordId, rows[^1].RevisionNumber));
    }

    public async Task<RevisionVerification> VerifyAsync(int mismatchLimit, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        int checkedCount = 0, legacy = 0, mismatched = 0;
        var first = new List<RevisionMismatch>();
        RevisionKey? after = null;
        while (await ReadVerifyPageAsync(db, after, cancellationToken) is { Count: > 0 } page)
        {
            foreach (var row in page)
            {
                checkedCount++;
                var expected = ExecutiveActionRevisionRows.TargetRows(row.RecordId, row.RevisionNumber, row.Notes).Select(Describe).ToArray();
                var actual = row.Targets.OrderBy(target => target.Position).Select(Describe).ToArray();
                if (expected.SequenceEqual(actual)) continue;
                if (actual.Length == 0) { legacy++; continue; }
                mismatched++;
                if (first.Count < mismatchLimit) first.Add(new(row.RecordId, row.RevisionNumber, [TargetsKey]));
            }
            after = new(page[^1].RecordId, page[^1].RevisionNumber);
        }
        return new(checkedCount, legacy, mismatched, first);
    }

    private static Task<List<VerifyRow>> ReadVerifyPageAsync(CollectionAttemptDbContext db, RevisionKey? after, CancellationToken cancellationToken) =>
        InKeyOrderAfter(db.Set<ExecutiveActionRevisionRow>().AsNoTracking().Where(row => row.DispositionNotes != null || row.DispositionTargets.Any()), after)
            .Select(row => new VerifyRow(row.RecordId, row.RevisionNumber, row.DispositionNotes, row.DispositionTargets.ToList()))
            .Take(VerifyPageSize).ToListAsync(cancellationToken);

    private static IQueryable<ExecutiveActionRevisionRow> InKeyOrderAfter(IQueryable<ExecutiveActionRevisionRow> rows, RevisionKey? after) =>
        rows.Where(row => after == null || row.RecordId.CompareTo(after.RecordId) > 0 || row.RecordId == after.RecordId && row.RevisionNumber > after.RevisionNumber)
            .OrderBy(row => row.RecordId).ThenBy(row => row.RevisionNumber);

    private static (int, string, string, int, int) Describe(ExecutiveActionRevisionDispositionTargetRow target) =>
        (target.Position, $"{target.Relation}/{target.Kind}", target.Number, target.ReferenceStart, target.ReferenceLength);

    private sealed record NotesRow(string RecordId, int RevisionNumber, string Notes);
    private sealed record VerifyRow(string RecordId, int RevisionNumber, string? Notes, List<ExecutiveActionRevisionDispositionTargetRow> Targets);
}
