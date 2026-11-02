using System.Collections.Immutable;
using System.Data;
using System.Text.Json.Nodes;
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
/// The backfill takes the vote review lock, and only that lock, for each batch. It writes only rows whose columns are null.
/// It proves each row inside its savepoint: it reads the row back, rebuilds the revision, and compares it to the stored JSON.</summary>
public sealed class PostgresVoteRevisionBackfillStore(IDbContextFactory<CollectionAttemptDbContext> contextFactory) : IVoteRevisionBackfillStore
{
    private const string UnreadableJson = "UnreadableJson";
    private const string UnprovenSource = "UnprovenSource";
    private const string InvalidShape = "InvalidShape";
    private const string DuplicateIssueId = "DuplicateIssueId";
    private const string RebuildMismatch = "RebuildMismatch";
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

    private static IQueryable<VoteRevisionRow> LegacyRows(CollectionAttemptDbContext db, VoteRevisionKey? after) =>
        InKeyOrderAfter(db.Set<VoteRevisionRow>().AsNoTracking().Where(row => row.Headline == null), after);

    private static IQueryable<VoteRevisionRow> InKeyOrderAfter(IQueryable<VoteRevisionRow> rows, VoteRevisionKey? after) =>
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
        catch (Exception exception) when (RefusalOf(exception) is { } refusedBy)
        {
            refusal = DatabasePrefix + refusedBy;
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
        var stored = await ReadPageAsync(db, Rows(db).Where(candidate => candidate.RecordId == row.RecordId && candidate.RevisionNumber == row.RevisionNumber), cancellationToken);
        return DifferingFields(stored.Rows.Single(), stored).Count == 0 ? null : RebuildMismatch;
    }

    private static (VoteDraftRevision? Revision, string? Refusal) ReadMovable(VoteRevisionRow row)
    {
        JsonObject stored;
        VoteDraftRevision revision;
        try
        {
            stored = JsonNode.Parse(row.RevisionJson) as JsonObject ?? throw new InvalidDataException("A stored vote revision is no JSON object.");
            revision = ReviewJson.Deserialize<VoteDraftRevision>(row.RevisionJson, "vote revision");
        }
        catch (Exception exception) when (ReviewJson.IsUnreadableStoredData(exception))
        {
            return (null, UnreadableJson);
        }
        if (!stored.ContainsKey(VoteRevisionComparison.SourceKey))
        {
            if (!VoteRevisionComparison.MayOmitSource(stored)) return (null, UnprovenSource);
            revision = revision with { Source = VoteSource.CongressGov };
        }
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

    private static IQueryable<VoteRevisionRow> Rows(CollectionAttemptDbContext db) =>
        db.Set<VoteRevisionRow>().AsNoTracking().Include(row => row.Facts).Include(row => row.Issues);

    private static async Task<VerifyPage> ReadVerifyPageAsync(CollectionAttemptDbContext db, VoteRevisionKey? after, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var page = await ReadPageAsync(db, InKeyOrderAfter(Rows(db), after).Take(VerifyPageSize), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return page;
    }

    private static async Task<VerifyPage> ReadPageAsync(CollectionAttemptDbContext db, IQueryable<VoteRevisionRow> query, CancellationToken cancellationToken)
    {
        var rows = await query.ToListAsync(cancellationToken);
        var listIds = rows.Select(row => row.MemberListId).OfType<string>().Distinct().ToArray();
        var lists = await db.Set<VoteMemberListRow>().AsNoTracking().Where(list => listIds.Contains(list.ListId))
            .ToDictionaryAsync(list => list.ListId, list => list.MemberCount, StringComparer.Ordinal, cancellationToken);
        var entries = (await db.Set<VoteMemberListEntryRow>().AsNoTracking().Where(entry => listIds.Contains(entry.ListId))
            .OrderBy(entry => entry.ListId).ThenBy(entry => entry.Position).ToListAsync(cancellationToken)).ToLookup(entry => entry.ListId, StringComparer.Ordinal);
        return new(rows, lists, entries);
    }

    private static IReadOnlyList<string> DifferingFields(VoteRevisionRow row, VerifyPage page)
    {
        var members = page.Entries[row.MemberListId!].Select(entry => new MemberVote(entry.PersonId, entry.BioguideId, entry.VoteCast,
            new(entry.CitationExtractionId, entry.CitationStart, entry.CitationLength))).ToImmutableArray();
        var fields = VoteRevisionComparison.DifferingKeys(row.RevisionJson, VoteRevisionRows.Rebuild(row, members)).ToList();
        if (row.MemberListId != VoteMemberLists.IdOf(members)) fields.Add("memberListId");
        if (page.MemberCounts.GetValueOrDefault(row.MemberListId!) != members.Length) fields.Add("memberCount");
        return fields;
    }

    private sealed record VerifyPage(IReadOnlyList<VoteRevisionRow> Rows, IReadOnlyDictionary<string, short> MemberCounts,
        ILookup<string, VoteMemberListEntryRow> Entries);
}
