using System.Collections.Immutable;
using CivicLens.Application.Publication;
using CivicLens.Core.Documents;
using CivicLens.Core.Review;
using CivicLens.Core.Votes;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Review;
using CivicLens.Infrastructure.Votes.Persistence;
using CivicLens.Publication.Contracts;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Infrastructure.Votes;

/// <summary>Owns the mapping between a vote revision and its row, fact rows, and issue rows, in both directions. The row also keeps the JSON, but readers use the columns.</summary>
internal static class VoteRevisionRows
{
    public static VoteRevisionRow Create(VoteDraftRevision revision, string? runId)
    {
        var row = new VoteRevisionRow
        {
            RecordId = revision.RecordId,
            RevisionNumber = revision.RevisionNumber,
            AuthorSubject = revision.AuthorSubject,
            AuthorKind = ReviewAuthor.KindOf(revision.AuthorSubject),
            RunId = runId,
            RevisionJson = ReviewJson.Serialize(revision),
            CreatedAtUtcTicks = revision.CreatedAtUtc.UtcTicks
        };
        FillColumns(row, revision);
        return row;
    }

    /// <summary>Sets every column and child row of the revision. It leaves the key, the author, the run, and the JSON as they are.</summary>
    public static void FillColumns(VoteRevisionRow row, VoteDraftRevision revision)
    {
        row.Headline = revision.Headline;
        row.Summary = revision.Summary;
        row.TextOrigin = revision.TextOrigin;
        row.SuggestionHeadline = revision.Suggestion?.Headline;
        row.SuggestionSummary = revision.Suggestion?.Summary;
        row.SuggestionRunId = revision.Suggestion?.RunId;
        row.SuggestionFingerprint = revision.Suggestion?.Fingerprint;
        row.SourceId = revision.SourceId;
        row.VoteSource = revision.Source;
        row.Chamber = revision.Chamber;
        row.Congress = revision.Congress;
        row.SessionNumber = revision.SessionNumber;
        row.RollCallNumber = revision.RollCallNumber;
        row.BillType = revision.BillType;
        row.BillNumber = revision.BillNumber;
        row.CandidateId = revision.CandidateId;
        row.CandidateUpdateUtcTicks = revision.CandidateUpdateDate.UtcTicks;
        row.VotedOn = revision.VotedOn;
        row.VotedOnCitationExtractionId = revision.VotedOnCitation.ExtractionId;
        row.VotedOnCitationStart = revision.VotedOnCitation.Start;
        row.VotedOnCitationLength = revision.VotedOnCitation.Length;
        row.Kind = revision.Kind;
        row.KindCitationExtractionId = revision.KindCitation?.ExtractionId;
        row.KindCitationStart = revision.KindCitation?.Start;
        row.KindCitationLength = revision.KindCitation?.Length;
        row.MemberListId = VoteMemberLists.IdOf(revision.Members);
        row.IssueRevisionId = revision.IssueRevisionId;
        row.SubjectsHash = revision.SubjectsHash;
        row.IssueCandidatesRecorded = !revision.IssueCandidateIds.IsDefault;
        row.Facts = [.. revision.Facts.Select((fact, position) => FactRow(revision, fact, position))];
        row.Issues = [.. IssueIds(revision).Select((issueId, position) => IssueRow(revision, issueId, position))];
    }

    /// <summary>Reads the rows of a query, then their facts, issues, and member lists, in a fixed number of queries. The child rows come
    /// by the exact keys of the parent rows. The child rows of a revision with columns never change, so a write that commits between the queries cannot drop its children.</summary>
    public static async Task<VoteRevisionPage> ReadPageAsync(CollectionAttemptDbContext db, IQueryable<VoteRevisionRow> query,
        CancellationToken cancellationToken)
    {
        var rows = await query.ToListAsync(cancellationToken);
        var recordIds = rows.Select(row => row.RecordId).ToArray();
        var numbers = rows.Select(row => row.RevisionNumber).ToArray();
        var facts = (await db.Set<VoteRevisionFactRow>().FromSqlRaw(ChildrenSql("vote_revision_facts"), recordIds, numbers).AsNoTracking()
            .ToListAsync(cancellationToken)).ToLookup(fact => (fact.RecordId, fact.RevisionNumber));
        var issues = (await db.Set<VoteRevisionIssueRow>().FromSqlRaw(ChildrenSql("vote_revision_issues"), recordIds, numbers).AsNoTracking()
            .ToListAsync(cancellationToken)).ToLookup(issue => (issue.RecordId, issue.RevisionNumber));
        var listIds = rows.Select(row => row.MemberListId).OfType<string>().Distinct().ToArray();
        var lists = await db.Set<VoteMemberListRow>().AsNoTracking().Where(list => listIds.Contains(list.ListId))
            .ToDictionaryAsync(list => list.ListId, list => list.MemberCount, StringComparer.Ordinal, cancellationToken);
        var entries = (await db.Set<VoteMemberListEntryRow>().AsNoTracking().Where(entry => listIds.Contains(entry.ListId))
            .OrderBy(entry => entry.ListId).ThenBy(entry => entry.Position).ToListAsync(cancellationToken)).ToLookup(entry => entry.ListId, StringComparer.Ordinal);
        return new(rows, facts, issues, lists, entries);
    }

    private static string ChildrenSql(string table) =>
        $"SELECT c.* FROM {table} c JOIN unnest({{0}}, {{1}}) AS k(id, number) ON c.record_id = k.id AND c.revision_number = k.number";

    /// <summary>The failure of a reader that meets a vote revision row with null columns. Only the backfill fixes such a row.</summary>
    public static InvalidDataException NotBackfilled(string revision) =>
        new($"{revision} has null columns. Run the vote revision backfill.");

    /// <summary>The reader that the shared review code uses for votes.</summary>
    public static async Task<IReadOnlyList<StoredRevision<VoteRevisionRow, VoteDraftRevision>>> ReadAsync(CollectionAttemptDbContext db,
        IQueryable<VoteRevisionRow> query, CancellationToken cancellationToken) =>
        (await ReadPageAsync(db, query, cancellationToken)).ToStoredRevisions();

    public static async Task<ImmutableArray<VoteDraftRevision>> ReadDraftRevisionsAsync(CollectionAttemptDbContext db,
        IQueryable<VoteRevisionRow> query, CancellationToken cancellationToken) =>
        [.. (await ReadAsync(db, query, cancellationToken)).Select(stored => stored.Read())];

    /// <summary>Reads the named revisions. It fails when one is not stored.</summary>
    public static async Task<ImmutableArray<VoteDraftRevision>> ReadNamedAsync(CollectionAttemptDbContext db,
        IReadOnlyCollection<PublishedRevisionBinding> named, CancellationToken cancellationToken)
    {
        var requested = named.Distinct().ToArray();
        if (requested.Length == 0) return [];
        CitedRevisions.RequireKind(PublishedRecordKind.Vote, requested, nameof(named));
        var revisions = await ReadDraftRevisionsAsync(db, db.Set<VoteRevisionRow>().FromSqlRaw("""
                SELECT r.* FROM vote_revisions r JOIN unnest({0}, {1}) AS k(id, number) ON r.record_id = k.id AND r.revision_number = k.number
                """, requested.Select(binding => binding.RecordId).ToArray(), requested.Select(binding => binding.RevisionNumber).ToArray()).AsNoTracking(),
            cancellationToken);
        return revisions.Length == requested.Length ? revisions : throw CitedRevisions.NotStored(PublishedRecordKind.Vote);
    }

    /// <summary>Builds the revision back from a row with columns and its child rows. The tracked issues come back in ordinal order, because the drafting code stores them that way.</summary>
    public static VoteDraftRevision Rebuild(VoteRevisionRow row, ImmutableArray<MemberVote> members, IEnumerable<VoteRevisionFactRow> factRows,
        IEnumerable<VoteRevisionIssueRow> issueRows)
    {
        var issues = issueRows.OrderBy(issue => issue.Position).ToArray();
        var recorded = row.IssueCandidatesRecorded!.Value;
        var tracked = issues.Where(issue => issue.IsTracked).Select(issue => issue.IssueId);
        return new(row.RecordId, row.RevisionNumber, row.AuthorSubject, new DateTimeOffset(row.CreatedAtUtcTicks, TimeSpan.Zero),
            row.Headline!, row.Summary!, row.TextOrigin!.Value, SuggestionOf(row),
            row.SourceId!, row.VoteSource!.Value, row.Chamber!.Value, row.Congress!.Value, row.SessionNumber!.Value, row.RollCallNumber!.Value,
            row.BillType, row.BillNumber, row.CandidateId!, new DateTimeOffset(row.CandidateUpdateUtcTicks!.Value, TimeSpan.Zero),
            [.. factRows.OrderBy(fact => fact.Position).Select(fact => new VoteFact(fact.FactText, new(fact.CitationExtractionId, fact.CitationStart, fact.CitationLength)))],
            row.VotedOn!.Value, new(row.VotedOnCitationExtractionId!, row.VotedOnCitationStart!.Value, row.VotedOnCitationLength!.Value), row.Kind!.Value,
            row.KindCitationExtractionId is null ? null : new(row.KindCitationExtractionId, row.KindCitationStart!.Value, row.KindCitationLength!.Value), members,
            [.. recorded ? tracked.Order(StringComparer.Ordinal) : tracked],
            recorded ? [.. issues.Select(issue => issue.IssueId)] : default, row.IssueRevisionId!, row.SubjectsHash);
    }

    private static ReviewTextSuggestion? SuggestionOf(VoteRevisionRow row) => row.SuggestionHeadline is null
        ? null
        : new(row.SuggestionHeadline, row.SuggestionSummary!, row.SuggestionRunId!, row.SuggestionFingerprint!);

    private static VoteRevisionFactRow FactRow(VoteDraftRevision revision, VoteFact fact, int position) => new()
    {
        RecordId = revision.RecordId,
        RevisionNumber = revision.RevisionNumber,
        Position = (short)position,
        FactText = fact.Text,
        CitationExtractionId = fact.Citation.ExtractionId,
        CitationStart = fact.Citation.Start,
        CitationLength = fact.Citation.Length
    };

    private static IEnumerable<string> IssueIds(VoteDraftRevision revision) =>
        revision.IssueCandidateIds.IsDefault ? revision.TrackedIssueIds : revision.IssueCandidateIds;

    private static VoteRevisionIssueRow IssueRow(VoteDraftRevision revision, string issueId, int position) => new()
    {
        RecordId = revision.RecordId,
        RevisionNumber = revision.RevisionNumber,
        IssueId = issueId,
        Position = (short)position,
        IsTracked = revision.TrackedIssueIds.Contains(issueId)
    };
}
