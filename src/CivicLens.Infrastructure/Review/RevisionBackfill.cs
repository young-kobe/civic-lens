using System.Collections.Immutable;
using System.Data;
using CivicLens.Application.Review;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Review.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CivicLens.Infrastructure.Review;

/// <summary>Moves legacy revisions of one kind into columns and child rows, and checks each moved revision against its JSON.
/// A batch takes the review lock of the kind, and only that lock. It writes only rows whose columns are null.
/// It proves each row inside its savepoint: it reads the row back, rebuilds the revision, and compares it to the stored JSON.</summary>
internal sealed class RevisionBackfill<TRow, TRevision, TPage>(IDbContextFactory<CollectionAttemptDbContext> contextFactory,
    RevisionBackfillKind<TRow, TRevision, TPage> kind)
    where TRow : class, IReviewRevisionRow
    where TRevision : class
    where TPage : IRevisionRowPage<TRow>
{
    private const int VerifyPageSize = 50;

    private readonly Func<TRow, bool> isLegacy = kind.IsLegacy.Compile();

    public async Task<RevisionBackfillBatch> BackfillBatchAsync(RevisionKey? after, int size, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await PostgresAdvisoryLock.BeginWriteAsync(db, kind.LockKey, cancellationToken);
        var rows = await InKeyOrderAfter(db.Set<TRow>().AsNoTracking().Where(kind.IsLegacy), after).Take(size).ToListAsync(cancellationToken);
        var skipped = ImmutableArray.CreateBuilder<SkippedRevision>();
        foreach (var row in rows)
            if (await TryMoveAsync(db, transaction, row, cancellationToken) is { } reason)
                skipped.Add(new(row.RecordId, row.RevisionNumber, reason));
        await transaction.CommitAsync(cancellationToken);
        return new(rows.Count, skipped.ToImmutable(), rows.Count == 0 ? null : new(rows[^1].RecordId, rows[^1].RevisionNumber));
    }

    public async Task<RevisionVerification> VerifyAsync(int mismatchLimit, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        int checkedCount = 0, legacy = 0, mismatched = 0;
        var first = new List<RevisionMismatch>();
        RevisionKey? after = null;
        while (await ReadVerifyPageAsync(db, after, cancellationToken) is { Rows.Count: > 0 } page)
        {
            foreach (var row in page.Rows)
            {
                if (isLegacy(row)) { legacy++; continue; }
                checkedCount++;
                if (kind.DifferingFields(row, page) is not { Count: > 0 } fields) continue;
                mismatched++;
                if (first.Count < mismatchLimit) first.Add(new(row.RecordId, row.RevisionNumber, fields));
            }
            after = new(page.Rows[^1].RecordId, page.Rows[^1].RevisionNumber);
        }
        return new(checkedCount, legacy, mismatched, first);
    }

    private static IQueryable<TRow> InKeyOrderAfter(IQueryable<TRow> rows, RevisionKey? after) =>
        rows.Where(row => after == null || row.RecordId.CompareTo(after.RecordId) > 0 || row.RecordId == after.RecordId && row.RevisionNumber > after.RevisionNumber)
            .OrderBy(row => row.RecordId).ThenBy(row => row.RevisionNumber);

    private async Task<string?> TryMoveAsync(CollectionAttemptDbContext db, IDbContextTransaction transaction, TRow row, CancellationToken cancellationToken)
    {
        var (revision, refusal) = kind.ReadMovable(row);
        if (revision is null) return refusal;
        await transaction.CreateSavepointAsync(kind.Savepoint, cancellationToken);
        try
        {
            refusal = await WriteAndProveAsync(db, row, revision, cancellationToken);
        }
        catch (Exception exception) when (RevisionBackfillReasons.RefusalOf(exception) is { } refusedBy)
        {
            refusal = RevisionBackfillReasons.DatabasePrefix + refusedBy;
        }
        if (refusal is null)
        {
            await transaction.ReleaseSavepointAsync(kind.Savepoint, cancellationToken);
            return null;
        }
        await transaction.RollbackToSavepointAsync(kind.Savepoint, cancellationToken);
        db.ChangeTracker.Clear();
        return refusal;
    }

    private async Task<string?> WriteAndProveAsync(CollectionAttemptDbContext db, TRow row, TRevision revision, CancellationToken cancellationToken)
    {
        db.Attach(row);
        await kind.FillColumnsAsync(db, row, revision, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        var stored = await kind.ReadPageAsync(db, db.Set<TRow>().AsNoTracking()
            .Where(candidate => candidate.RecordId == row.RecordId && candidate.RevisionNumber == row.RevisionNumber), cancellationToken);
        return kind.DifferingFields(stored.Rows.Single(), stored).Count == 0 ? null : RevisionBackfillReasons.RebuildMismatch;
    }

    private async Task<TPage> ReadVerifyPageAsync(CollectionAttemptDbContext db, RevisionKey? after, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var page = await kind.ReadPageAsync(db, InKeyOrderAfter(db.Set<TRow>().AsNoTracking(), after).Take(VerifyPageSize), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return page;
    }
}
