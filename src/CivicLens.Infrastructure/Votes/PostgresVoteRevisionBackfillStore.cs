using System.Collections.Immutable;
using System.Data;
using System.Text.Json.Nodes;
using CivicLens.Application.Review;
using CivicLens.Core.Votes;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Review;
using CivicLens.Infrastructure.Votes.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CivicLens.Infrastructure.Votes;

/// <summary>Moves legacy vote revisions into columns and child rows, and checks each moved revision against its JSON.
/// The backfill takes the vote review lock, and only that lock, for each batch. It writes only rows whose columns are null.
/// It proves each row inside its savepoint: it reads the row back, rebuilds the revision, and compares it to the stored JSON.</summary>
public sealed class PostgresVoteRevisionBackfillStore(IDbContextFactory<CollectionAttemptDbContext> contextFactory) : IRevisionBackfillStore
{
    private const string UnprovenSource = "UnprovenSource";

    private const string Savepoint = "vote_revision_backfill";
    private const int VerifyPageSize = 50;

    public async Task<RevisionBackfillBatch> BackfillBatchAsync(RevisionKey? after, int size, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await PostgresAdvisoryLock.BeginWriteAsync(db, PostgresAdvisoryLock.VoteReview, cancellationToken);
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
                if (row.Headline is null) { legacy++; continue; }
                checkedCount++;
                if (DifferingFields(row, page) is not { Count: > 0 } fields) continue;
                mismatched++;
                if (first.Count < mismatchLimit) first.Add(new(row.RecordId, row.RevisionNumber, fields));
            }
            after = new(page.Rows[^1].RecordId, page.Rows[^1].RevisionNumber);
        }
        return new(checkedCount, legacy, mismatched, first);
    }

    private static IQueryable<VoteRevisionRow> LegacyRows(CollectionAttemptDbContext db, RevisionKey? after) =>
        InKeyOrderAfter(db.Set<VoteRevisionRow>().AsNoTracking().Where(row => row.Headline == null), after);

    private static IQueryable<VoteRevisionRow> InKeyOrderAfter(IQueryable<VoteRevisionRow> rows, RevisionKey? after) =>
        rows.Where(row => after == null || row.RecordId.CompareTo(after.RecordId) > 0 || row.RecordId == after.RecordId && row.RevisionNumber > after.RevisionNumber)
            .OrderBy(row => row.RecordId).ThenBy(row => row.RevisionNumber);

    private static async Task<string?> TryMoveAsync(CollectionAttemptDbContext db, IDbContextTransaction transaction, VoteRevisionRow row,
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

    private static async Task<string?> WriteAndProveAsync(CollectionAttemptDbContext db, VoteRevisionRow row, VoteDraftRevision revision,
        CancellationToken cancellationToken)
    {
        db.Attach(row);
        VoteRevisionRows.FillColumns(row, revision);
        await VoteMemberLists.EnsureAsync(db, revision, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        var stored = await VoteRevisionRows.ReadPageAsync(db, db.Set<VoteRevisionRow>().AsNoTracking().Where(candidate => candidate.RecordId == row.RecordId && candidate.RevisionNumber == row.RevisionNumber), cancellationToken);
        return DifferingFields(stored.Rows.Single(), stored).Count == 0 ? null : RevisionBackfillReasons.RebuildMismatch;
    }

    private static (VoteDraftRevision? Revision, string? Refusal) ReadMovable(VoteRevisionRow row)
    {
        if (RevisionJsonComparison.TryParseObject(row.RevisionJson) is not { } stored) return (null, RevisionBackfillReasons.UnreadableJson);
        VoteDraftRevision revision;
        try
        {
            revision = ReviewJson.Deserialize<VoteDraftRevision>(row.RevisionJson, "vote revision");
        }
        catch (Exception exception) when (ReviewJson.IsUnreadableStoredData(exception))
        {
            return (null, RevisionBackfillReasons.UnreadableJson);
        }
        if (!stored.ContainsKey(VoteRevisionComparison.SourceKey))
        {
            if (!VoteRevisionComparison.MayOmitSource(stored)) return (null, UnprovenSource);
            revision = revision with { Source = VoteSource.CongressGov };
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
        if (RevisionBackfillReasons.HasDuplicates(revision.TrackedIssueIds) || !revision.IssueCandidateIds.IsDefault && RevisionBackfillReasons.HasDuplicates(revision.IssueCandidateIds))
            return (null, RevisionBackfillReasons.DuplicateIssueId);
        return (revision, null);
    }

    private static bool HasNullElements(VoteDraftRevision revision) =>
        !revision.Facts.IsDefault && revision.Facts.Any(fact => fact is null) || !revision.Members.IsDefault && revision.Members.Any(member => member is null);

    private static async Task<VoteRevisionPage> ReadVerifyPageAsync(CollectionAttemptDbContext db, RevisionKey? after, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var page = await VoteRevisionRows.ReadPageAsync(db, InKeyOrderAfter(db.Set<VoteRevisionRow>().AsNoTracking(), after).Take(VerifyPageSize), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return page;
    }

    private static IReadOnlyList<string> DifferingFields(VoteRevisionRow row, VoteRevisionPage page)
    {
        var members = page.MembersOf(row);
        var fields = VoteRevisionComparison.DifferingKeys(row.RevisionJson, VoteRevisionRows.Rebuild(row, members, page.FactsOf(row), page.IssuesOf(row))).ToList();
        if (row.MemberListId != VoteMemberLists.IdOf(members)) fields.Add("memberListId");
        if (page.MemberCounts.GetValueOrDefault(row.MemberListId!) != members.Length) fields.Add("memberCount");
        return fields;
    }
}
