using System.Collections.Immutable;
using System.Data;
using CivicLens.Application.Votes;
using CivicLens.Core.Votes;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Review;
using CivicLens.Infrastructure.Votes.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace CivicLens.Infrastructure.Votes;

/// <summary>Moves legacy vote revisions into columns and child rows, and checks each moved revision against its JSON.
/// The backfill takes the vote review lock, and only that lock, for each batch. It writes only rows whose columns are null.</summary>
public sealed class PostgresVoteRevisionBackfillStore(IDbContextFactory<CollectionAttemptDbContext> contextFactory) : IVoteRevisionBackfillStore
{
    private const string UnreadableJson = "UnreadableJson";
    private const string HeaderMismatch = "HeaderMismatch";
    private const string InvalidShape = "InvalidShape";
    private const string DuplicateIssueId = "DuplicateIssueId";
    private const string TrackedIssueOrder = "TrackedIssueOrder";
    private const string DatabasePrefix = "database:";

    private const string Savepoint = "vote_revision_backfill";
    private const int VerifyPageSize = 50;

    public async Task<VoteRevisionBackfillBatch> BackfillBatchAsync(VoteRevisionKey? after, int size, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await PostgresAdvisoryLock.BeginWriteAsync(db, PostgresAdvisoryLock.VoteReview, cancellationToken);
        var rows = await LegacyRows(db, after).Take(size).ToListAsync(cancellationToken);
        var skipped = ImmutableArray.CreateBuilder<SkippedVoteRevision>();
        foreach (var row in rows)
            if (await TryMoveAsync(db, transaction, row, cancellationToken) is { } reason)
                skipped.Add(new(row.RecordId, row.RevisionNumber, reason));
        await transaction.CommitAsync(cancellationToken);
        return new(rows.Count, skipped.ToImmutable(), rows.Count == 0 ? null : new(rows[^1].RecordId, rows[^1].RevisionNumber));
    }

    public async Task<VoteRevisionVerification> VerifyAsync(int mismatchLimit, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        int checkedCount = 0, legacy = 0, mismatched = 0;
        var first = new List<VoteRevisionMismatch>();
        VoteRevisionKey? after = null;
        while (await ReadPageAsync(db, after, cancellationToken) is { Rows.Count: > 0 } page)
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

    private static IQueryable<VoteRevisionRow> LegacyRows(CollectionAttemptDbContext db, VoteRevisionKey? after)
    {
        var rows = db.Set<VoteRevisionRow>().AsNoTracking().Where(row => row.Headline == null);
        if (after is not null)
            rows = rows.Where(row => row.RecordId.CompareTo(after.RecordId) > 0 || row.RecordId == after.RecordId && row.RevisionNumber > after.RevisionNumber);
        return rows.OrderBy(row => row.RecordId).ThenBy(row => row.RevisionNumber);
    }

    private static async Task<string?> TryMoveAsync(CollectionAttemptDbContext db, IDbContextTransaction transaction, VoteRevisionRow row,
        CancellationToken cancellationToken)
    {
        var (revision, refusal) = ReadMovable(row);
        if (revision is null) return refusal;
        await transaction.CreateSavepointAsync(Savepoint, cancellationToken);
        try
        {
            db.Attach(row);
            VoteRevisionRows.FillColumns(row, revision);
            await VoteMemberLists.EnsureAsync(db, revision, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.ReleaseSavepointAsync(Savepoint, cancellationToken);
            return null;
        }
        catch (Exception exception) when (RefusalOf(exception) is { } refusedBy)
        {
            await transaction.RollbackToSavepointAsync(Savepoint, cancellationToken);
            db.ChangeTracker.Clear();
            return DatabasePrefix + refusedBy;
        }
    }

    private static (VoteDraftRevision? Revision, string? Refusal) ReadMovable(VoteRevisionRow row)
    {
        VoteDraftRevision revision;
        try
        {
            revision = ReviewJson.Deserialize<VoteDraftRevision>(row.RevisionJson, "vote revision");
        }
        catch (Exception exception) when (ReviewJson.IsUnreadableStoredData(exception))
        {
            return (null, UnreadableJson);
        }
        if (revision.RecordId != row.RecordId || revision.RevisionNumber != row.RevisionNumber || revision.AuthorSubject != row.AuthorSubject ||
            revision.CreatedAtUtc.UtcTicks != row.CreatedAtUtcTicks)
            return (null, HeaderMismatch);
        try
        {
            revision.ValidateShape();
        }
        catch (ArgumentException)
        {
            return (null, InvalidShape);
        }
        if (HasDuplicates(revision.TrackedIssueIds) || !revision.IssueCandidateIds.IsDefault && HasDuplicates(revision.IssueCandidateIds))
            return (null, DuplicateIssueId);
        if (!revision.IssueCandidateIds.IsDefault && !revision.TrackedIssueIds.SequenceEqual(revision.TrackedIssueIds.Order(StringComparer.Ordinal)))
            return (null, TrackedIssueOrder);
        return (revision, null);
    }

    private static bool HasDuplicates(ImmutableArray<string> ids) => ids.Distinct(StringComparer.Ordinal).Count() != ids.Length;

    private static string? RefusalOf(Exception exception)
    {
        var fault = exception as PostgresException ?? (exception as DbUpdateException)?.InnerException as PostgresException;
        return fault is { SqlState: { } state } && (state.StartsWith("23", StringComparison.Ordinal) || state == PostgresErrorCodes.StringDataRightTruncation)
            ? fault.ConstraintName ?? state
            : null;
    }

    private static async Task<VerifyPage> ReadPageAsync(CollectionAttemptDbContext db, VoteRevisionKey? after, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var rows = await db.Set<VoteRevisionRow>().AsNoTracking().Include(row => row.Facts).Include(row => row.Issues)
            .Where(row => after == null || row.RecordId.CompareTo(after.RecordId) > 0 || row.RecordId == after.RecordId && row.RevisionNumber > after.RevisionNumber)
            .OrderBy(row => row.RecordId).ThenBy(row => row.RevisionNumber).Take(VerifyPageSize).ToListAsync(cancellationToken);
        var listIds = rows.Select(row => row.MemberListId).OfType<string>().Distinct().ToArray();
        var lists = await db.Set<VoteMemberListRow>().AsNoTracking().Where(list => listIds.Contains(list.ListId))
            .ToDictionaryAsync(list => list.ListId, list => list.MemberCount, StringComparer.Ordinal, cancellationToken);
        var entries = (await db.Set<VoteMemberListEntryRow>().AsNoTracking().Where(entry => listIds.Contains(entry.ListId))
            .OrderBy(entry => entry.ListId).ThenBy(entry => entry.Position).ToListAsync(cancellationToken)).ToLookup(entry => entry.ListId, StringComparer.Ordinal);
        await transaction.CommitAsync(cancellationToken);
        return new(rows, lists, entries);
    }

    private static IReadOnlyList<string> DifferingFields(VoteRevisionRow row, VerifyPage page)
    {
        var entries = page.Entries[row.MemberListId!];
        var members = entries.Select(entry => new MemberVote(entry.PersonId, entry.BioguideId, entry.VoteCast,
            new(entry.CitationExtractionId, entry.CitationStart, entry.CitationLength))).ToImmutableArray();
        try
        {
            var stored = ReviewJson.Deserialize<VoteDraftRevision>(row.RevisionJson, "vote revision");
            var fields = VoteRevisionComparison.DifferingFields(stored, VoteRevisionRows.Rebuild(row, members)).ToList();
            if (row.MemberListId != VoteMemberLists.IdOf(members)) fields.Add("MemberListId");
            if (page.MemberCounts.GetValueOrDefault(row.MemberListId!) != members.Length) fields.Add("MemberCount");
            return fields;
        }
        catch (Exception exception) when (ReviewJson.IsUnreadableStoredData(exception))
        {
            return [nameof(VoteRevisionRow.RevisionJson)];
        }
    }

    private sealed record VerifyPage(IReadOnlyList<VoteRevisionRow> Rows, IReadOnlyDictionary<string, short> MemberCounts,
        ILookup<string, VoteMemberListEntryRow> Entries);
}
