using System.Collections.Immutable;
using System.Data;
using CivicLens.Application.Review;
using CivicLens.Core.ExecutiveActions;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.ExecutiveActions.Persistence;
using CivicLens.Infrastructure.Review;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CivicLens.Infrastructure.ExecutiveActions;

public sealed class PostgresExecutiveActionRevisionBackfillStore(IDbContextFactory<CollectionAttemptDbContext> contextFactory) : IRevisionBackfillStore
{
    private const string Savepoint = "executive_action_revision_backfill";
    private const string ColumnsKey = "columns";
    private const int VerifyPageSize = 50;
    private static readonly HashSet<string> NoAllowedAbsences = [];

    public async Task<RevisionBackfillBatch> BackfillBatchAsync(RevisionKey? after, int size, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await PostgresAdvisoryLock.BeginWriteAsync(db, PostgresAdvisoryLock.ExecutiveActionReview, cancellationToken);
        var rows = await LegacyRows(db, after).Take(size).ToListAsync(cancellationToken);
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
                if (row.SourceId is null) { legacy++; continue; }
                checkedCount++;
                if (DifferingFields(row, page) is not { Count: > 0 } fields) continue;
                mismatched++;
                if (first.Count < mismatchLimit) first.Add(new(row.RecordId, row.RevisionNumber, fields));
            }
            after = new(page.Rows[^1].RecordId, page.Rows[^1].RevisionNumber);
        }
        return new(checkedCount, legacy, mismatched, first);
    }

    private static IQueryable<ExecutiveActionRevisionRow> LegacyRows(CollectionAttemptDbContext db, RevisionKey? after) =>
        InKeyOrderAfter(db.Set<ExecutiveActionRevisionRow>().AsNoTracking().Where(row => row.SourceId == null), after);

    private static IQueryable<ExecutiveActionRevisionRow> InKeyOrderAfter(IQueryable<ExecutiveActionRevisionRow> rows, RevisionKey? after) =>
        rows.Where(row => after == null || row.RecordId.CompareTo(after.RecordId) > 0 || row.RecordId == after.RecordId && row.RevisionNumber > after.RevisionNumber)
            .OrderBy(row => row.RecordId).ThenBy(row => row.RevisionNumber);

    private static async Task<string?> TryMoveAsync(CollectionAttemptDbContext db, IDbContextTransaction transaction, ExecutiveActionRevisionRow row,
        CancellationToken cancellationToken)
    {
        var (revision, refusal) = ReadMovable(row);
        if (revision is null) return refusal;
        await transaction.CreateSavepointAsync(Savepoint, cancellationToken);
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
            await transaction.ReleaseSavepointAsync(Savepoint, cancellationToken);
            return null;
        }
        await transaction.RollbackToSavepointAsync(Savepoint, cancellationToken);
        db.ChangeTracker.Clear();
        return refusal;
    }

    private static async Task<string?> WriteAndProveAsync(CollectionAttemptDbContext db, ExecutiveActionRevisionRow row, ExecutiveActionRevision revision,
        CancellationToken cancellationToken)
    {
        db.Attach(row);
        ExecutiveActionRevisionRows.FillColumns(row, revision);
        await db.SaveChangesAsync(cancellationToken);
        var stored = await ExecutiveActionRevisionRows.ReadPageAsync(db, db.Set<ExecutiveActionRevisionRow>().AsNoTracking()
            .Where(candidate => candidate.RecordId == row.RecordId && candidate.RevisionNumber == row.RevisionNumber), cancellationToken);
        return DifferingFields(stored.Rows.Single(), stored).Count == 0 ? null : RevisionBackfillReasons.RebuildMismatch;
    }

    private static (ExecutiveActionRevision? Revision, string? Refusal) ReadMovable(ExecutiveActionRevisionRow row)
    {
        ExecutiveActionRevision revision;
        try
        {
            revision = ReviewJson.Deserialize<ExecutiveActionRevision>(row.RevisionJson, "executive action revision");
        }
        catch (Exception exception) when (ReviewJson.IsUnreadableStoredData(exception))
        {
            return (null, RevisionBackfillReasons.UnreadableJson);
        }
        if (HasNullElements(revision)) return (null, RevisionBackfillReasons.InvalidShape);
        try
        {
            revision.ValidateShape();
        }
        catch (ArgumentException)
        {
            return (null, RevisionBackfillReasons.InvalidShape);
        }
        return (revision, null);
    }

    private static bool HasNullElements(ExecutiveActionRevision revision) =>
        !revision.Facts.IsDefault && revision.Facts.Any(fact => fact is null) || !revision.Notices.IsDefault && revision.Notices.Any(notice => notice is null);

    private static async Task<ExecutiveActionRevisionPage> ReadVerifyPageAsync(CollectionAttemptDbContext db, RevisionKey? after,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var page = await ExecutiveActionRevisionRows.ReadPageAsync(db, InKeyOrderAfter(db.Set<ExecutiveActionRevisionRow>().AsNoTracking(), after).Take(VerifyPageSize),
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return page;
    }

    private static IReadOnlyList<string> DifferingFields(ExecutiveActionRevisionRow row, ExecutiveActionRevisionPage page)
    {
        ExecutiveActionRevision rebuilt;
        try
        {
            rebuilt = page.Rebuild(row);
        }
        catch (InvalidDataException)
        {
            return [ColumnsKey];
        }
        if (RevisionJsonComparison.TryParseObject(row.RevisionJson) is not { } stored) return [RevisionJsonComparison.UnreadableKey];
        return RevisionJsonComparison.DifferingKeys(stored, ReviewJson.ToObject(rebuilt), NoAllowedAbsences);
    }
}
