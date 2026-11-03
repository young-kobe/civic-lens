using CivicLens.Application.Publication;
using CivicLens.Core.Documents;
using CivicLens.Core.Review;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Review.Persistence;
using CivicLens.Publication.Contracts;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Infrastructure.Review;

/// <summary>Owns the mapping between a document change revision and its row, official rows, issue rows, and citation rows, in both directions.
/// The row also keeps the JSON, but readers use the columns.</summary>
internal static class DocumentChangeRevisionRows
{
    private const string OfficialsTable = "document_change_revision_officials";
    private const string IssuesTable = "document_change_revision_issues";
    private const string CitationsTable = "document_change_revision_citations";
    private const string IdColumn = "draft_id";

    public static DocumentChangeRevisionRow Create(DocumentChangeDraftRevision revision)
    {
        var row = new DocumentChangeRevisionRow
        {
            DraftId = revision.DraftId,
            RevisionNumber = revision.RevisionNumber,
            AuthorSubject = revision.AuthorSubject,
            AuthorKind = ReviewAuthor.KindOf(revision.AuthorSubject),
            RevisionJson = ReviewJson.Serialize(revision),
            CreatedAtUtcTicks = revision.CreatedAtUtc.UtcTicks
        };
        FillColumns(row, revision);
        return row;
    }

    /// <summary>Sets every column and child row of the revision. It leaves the key, the author, and the JSON as they are.</summary>
    public static void FillColumns(DocumentChangeRevisionRow row, DocumentChangeDraftRevision revision)
    {
        row.ComparisonId = revision.ComparisonId;
        row.Headline = revision.Headline;
        row.Summary = revision.Summary;
        row.Significance = revision.Significance;
        row.Limits = revision.Limits;
        row.Institution = revision.Institution;
        row.ChangeDate = revision.ChangeDate;
        row.ChangeDateEvidenceExtractionId = revision.ChangeDateEvidence?.ExtractionId;
        row.ChangeDateEvidenceStart = revision.ChangeDateEvidence?.Start;
        row.ChangeDateEvidenceLength = revision.ChangeDateEvidence?.Length;
        row.Officials = [.. revision.OfficialIds.Select((officialId, position) => OfficialRow(revision, officialId, position))];
        row.Issues = [.. revision.IssueIds.Select((issueId, position) => IssueRow(revision, issueId, position))];
        row.Citations = [.. revision.Citations.Select((citation, position) => CitationRow(revision, citation, position))];
    }

    /// <summary>Reads the rows of a query, then their official, issue, and citation rows, in a fixed number of queries. The child rows come by
    /// the exact keys of the parent rows. The child rows of a revision with columns never change, so a write that commits between the queries
    /// cannot drop its children.</summary>
    public static async Task<DocumentChangeRevisionPage> ReadPageAsync(CollectionAttemptDbContext db, IQueryable<DocumentChangeRevisionRow> query,
        CancellationToken cancellationToken)
    {
        var rows = await query.ToListAsync(cancellationToken);
        var draftIds = rows.Select(row => row.DraftId).ToArray();
        var numbers = rows.Select(row => row.RevisionNumber).ToArray();
        var officials = (await db.Set<DocumentChangeRevisionOfficialRow>().FromSqlRaw(ReviewQueries.RevisionChildren(OfficialsTable, IdColumn), draftIds, numbers)
            .AsNoTracking().ToListAsync(cancellationToken)).ToLookup(official => (official.DraftId, official.RevisionNumber));
        var issues = (await db.Set<DocumentChangeRevisionIssueRow>().FromSqlRaw(ReviewQueries.RevisionChildren(IssuesTable, IdColumn), draftIds, numbers)
            .AsNoTracking().ToListAsync(cancellationToken)).ToLookup(issue => (issue.DraftId, issue.RevisionNumber));
        var citations = (await db.Set<DocumentChangeRevisionCitationRow>().FromSqlRaw(ReviewQueries.RevisionChildren(CitationsTable, IdColumn), draftIds, numbers)
            .AsNoTracking().ToListAsync(cancellationToken)).ToLookup(citation => (citation.DraftId, citation.RevisionNumber));
        return new(rows, officials, issues, citations);
    }

    /// <summary>The failure of a reader that meets a document change revision row with null columns. Only the backfill fixes such a row.</summary>
    public static InvalidDataException NotBackfilled(string revision) =>
        new($"{revision} has null columns. Run the document change revision backfill.");

    /// <summary>The reader that the shared review code uses for document changes.</summary>
    public static async Task<IReadOnlyList<StoredRevision<DocumentChangeRevisionRow, DocumentChangeDraftRevision>>> ReadAsync(
        CollectionAttemptDbContext db, IQueryable<DocumentChangeRevisionRow> query, CancellationToken cancellationToken) =>
        (await ReadPageAsync(db, query, cancellationToken)).ToStoredRevisions();

    /// <summary>Reads the named revisions. It fails when one is not stored.</summary>
    public static async Task<IReadOnlyList<DocumentChangeDraftRevision>> ReadNamedAsync(CollectionAttemptDbContext db,
        IReadOnlyCollection<PublishedRevisionBinding> named, CancellationToken cancellationToken)
    {
        var requested = named.Distinct().ToArray();
        if (requested.Length == 0) return [];
        CitedRevisions.RequireKind(PublishedRecordKind.DocumentChange, requested, nameof(named));
        var revisions = (await ReadAsync(db, db.Set<DocumentChangeRevisionRow>().FromSqlRaw("""
                SELECT r.* FROM document_change_revisions r JOIN unnest({0}, {1}) AS k(id, number) ON r.draft_id = k.id AND r.revision_number = k.number
                """, requested.Select(binding => binding.RecordId).ToArray(), requested.Select(binding => binding.RevisionNumber).ToArray()).AsNoTracking(),
            cancellationToken)).Select(stored => stored.Read()).ToArray();
        return revisions.Length == requested.Length ? revisions : throw CitedRevisions.NotStored(PublishedRecordKind.DocumentChange);
    }

    private static DocumentChangeRevisionOfficialRow OfficialRow(DocumentChangeDraftRevision revision, string officialId, int position) => new()
    {
        DraftId = revision.DraftId,
        RevisionNumber = revision.RevisionNumber,
        OfficialId = officialId,
        Position = (short)position
    };

    private static DocumentChangeRevisionIssueRow IssueRow(DocumentChangeDraftRevision revision, string issueId, int position) => new()
    {
        DraftId = revision.DraftId,
        RevisionNumber = revision.RevisionNumber,
        IssueId = issueId,
        Position = (short)position
    };

    private static DocumentChangeRevisionCitationRow CitationRow(DocumentChangeDraftRevision revision, SourceSpan citation, int position) => new()
    {
        DraftId = revision.DraftId,
        RevisionNumber = revision.RevisionNumber,
        Position = (short)position,
        CitationExtractionId = citation.ExtractionId,
        CitationStart = citation.Start,
        CitationLength = citation.Length
    };
}
