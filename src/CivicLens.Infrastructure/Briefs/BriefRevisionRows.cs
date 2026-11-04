using CivicLens.Core.Briefs;
using CivicLens.Core.Review;
using CivicLens.Infrastructure.Briefs.Persistence;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Review;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Infrastructure.Briefs;

/// <summary>Owns the mapping between a brief revision and its row, citation rows, paragraph rows, and paragraph citation rows, in both directions.
/// The row also keeps the JSON, but readers use the columns.</summary>
internal static class BriefRevisionRows
{
    public static BriefRevisionRow Create(BriefRevision revision, string? runId)
    {
        var row = new BriefRevisionRow
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

    /// <summary>Sets every column and child row of the revision. It leaves the key, the author, the run, and the JSON as they are.
    /// The coverage count of cited records is not stored, because it equals the number of citation rows of the text.</summary>
    public static void FillColumns(BriefRevisionRow row, BriefRevision revision)
    {
        row.WeekStart = revision.Window.WeekStart;
        row.WindowStartUtcTicks = revision.Window.StartUtcTicks;
        row.WindowEndUtcTicks = revision.Window.EndUtcTicks;
        row.FirstReleaseNumber = revision.Window.FirstReleaseNumber;
        row.CutoffReleaseNumber = revision.Window.CutoffReleaseNumber;
        row.Fingerprint = revision.Fingerprint;
        row.Headline = revision.Headline;
        row.TextOrigin = revision.TextOrigin;
        row.SuggestionHeadline = revision.Suggestion?.Headline;
        row.SuggestionRunId = revision.Suggestion?.RunId;
        row.SuggestionFingerprint = revision.Suggestion?.Fingerprint;
        row.CoverageConsidered = revision.Coverage.Considered;
        row.CoverageSentToModel = revision.Coverage.SentToModel;
        row.Citations = CitationRows(revision, BriefRevisionPart.Text, revision.Citations);
        row.Paragraphs = ParagraphRows(revision, BriefRevisionPart.Text, revision.Paragraphs);
        if (revision.Suggestion is not { } suggestion) return;
        row.Citations.AddRange(CitationRows(revision, BriefRevisionPart.Suggestion, suggestion.Citations));
        row.Paragraphs.AddRange(ParagraphRows(revision, BriefRevisionPart.Suggestion, suggestion.Paragraphs));
    }

    /// <summary>Reads the rows of a query, then their citations and paragraphs, in a fixed number of queries. The child rows come by
    /// the exact keys of the parent rows. The child rows of a revision with columns never change, so a write that commits between the queries cannot drop its children.</summary>
    public static async Task<BriefRevisionPage> ReadPageAsync(CollectionAttemptDbContext db, IQueryable<BriefRevisionRow> query,
        CancellationToken cancellationToken)
    {
        var rows = await query.ToListAsync(cancellationToken);
        var recordIds = rows.Select(row => row.RecordId).ToArray();
        var numbers = rows.Select(row => row.RevisionNumber).ToArray();
        var citations = (await db.Set<BriefRevisionCitationRow>().FromSqlRaw(ReviewQueries.RevisionChildren("brief_revision_citations"), recordIds, numbers)
            .AsNoTracking().ToListAsync(cancellationToken)).ToLookup(citation => (citation.RecordId, citation.RevisionNumber));
        var paragraphs = (await db.Set<BriefRevisionParagraphRow>().FromSqlRaw(ReviewQueries.RevisionChildren("brief_revision_paragraphs"), recordIds, numbers)
            .AsNoTracking().ToListAsync(cancellationToken)).ToLookup(paragraph => (paragraph.RecordId, paragraph.RevisionNumber));
        var paragraphCitations = (await db.Set<BriefRevisionParagraphCitationRow>().FromSqlRaw(
                ReviewQueries.RevisionChildren("brief_revision_paragraph_citations"), recordIds, numbers)
            .AsNoTracking().ToListAsync(cancellationToken)).ToLookup(link => (link.RecordId, link.RevisionNumber));
        return new(rows, citations, paragraphs, paragraphCitations);
    }

    /// <summary>The failure of a reader that meets a brief revision row with null columns. Only the backfill fixes such a row.</summary>
    public static InvalidDataException NotBackfilled(string revision) =>
        new($"{revision} has null columns. Run the brief revision backfill.");

    /// <summary>The reader that the shared review code uses for briefs.</summary>
    public static async Task<IReadOnlyList<StoredRevision<BriefRevisionRow, BriefRevision>>> ReadAsync(CollectionAttemptDbContext db,
        IQueryable<BriefRevisionRow> query, CancellationToken cancellationToken) =>
        (await ReadPageAsync(db, query, cancellationToken)).ToStoredRevisions();

    private static List<BriefRevisionCitationRow> CitationRows(BriefRevision revision, BriefRevisionPart part, IEnumerable<BriefCitation> citations) =>
        [.. citations.Select((citation, position) => CitationRow(revision, part, citation, position))];

    private static BriefRevisionCitationRow CitationRow(BriefRevision revision, BriefRevisionPart part, BriefCitation citation, int position)
    {
        var row = new BriefRevisionCitationRow
        {
            RecordId = revision.RecordId,
            RevisionNumber = revision.RevisionNumber,
            Part = part,
            CitationId = citation.Id,
            Position = (short)position,
            Kind = citation.Kind
        };
        switch (citation.Kind)
        {
            case BriefCitationKind.Statement:
                row.StatementCandidateId = citation.RecordId;
                row.StatementRevisionNumber = citation.RevisionNumber;
                break;
            case BriefCitationKind.Action:
                row.ActionRecordId = citation.RecordId;
                row.ActionRevisionNumber = citation.RevisionNumber;
                break;
            default:
                row.DocumentChangeDraftId = citation.RecordId;
                row.DocumentChangeRevisionNumber = citation.RevisionNumber;
                break;
        }
        return row;
    }

    private static List<BriefRevisionParagraphRow> ParagraphRows(BriefRevision revision, BriefRevisionPart part, IEnumerable<BriefParagraph> paragraphs) =>
        [.. paragraphs.Select((paragraph, position) => new BriefRevisionParagraphRow
        {
            RecordId = revision.RecordId,
            RevisionNumber = revision.RevisionNumber,
            Part = part,
            Position = (short)position,
            ParagraphText = paragraph.Text,
            Citations = [.. paragraph.CitationIds.Select((citationId, citationPosition) => new BriefRevisionParagraphCitationRow
            {
                RecordId = revision.RecordId,
                RevisionNumber = revision.RevisionNumber,
                Part = part,
                ParagraphPosition = (short)position,
                Position = (short)citationPosition,
                CitationId = citationId
            })]
        })];
}
