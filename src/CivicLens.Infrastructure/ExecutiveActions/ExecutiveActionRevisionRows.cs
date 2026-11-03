using CivicLens.Core.ExecutiveActions;
using CivicLens.Core.Review;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.ExecutiveActions.Persistence;
using CivicLens.Infrastructure.Review;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Infrastructure.ExecutiveActions;

/// <summary>Owns the mapping between an executive action revision and its row, fact rows, and notice rows, in both directions. The row also keeps the JSON, but readers use the columns.</summary>
internal static class ExecutiveActionRevisionRows
{
    public static ExecutiveActionRevisionRow Create(ExecutiveActionRevision revision)
    {
        var row = new ExecutiveActionRevisionRow
        {
            RecordId = revision.RecordId,
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
    public static void FillColumns(ExecutiveActionRevisionRow row, ExecutiveActionRevision revision)
    {
        row.SourceId = revision.SourceId;
        row.DocumentNumber = revision.DocumentNumber;
        row.Kind = revision.Kind;
        row.PresidentialNumber = revision.PresidentialNumber;
        row.Title = revision.Title;
        row.SignedOn = revision.SignedOn;
        row.PublishedOn = revision.PublishedOn;
        row.FederalRegisterCitation = revision.FederalRegisterCitation;
        row.PresidentName = revision.PresidentName;
        row.PersonId = revision.PersonId;
        row.DocumentUrl = revision.DocumentUrl;
        row.Facts = [.. revision.Facts.Select((fact, position) => FactRow(revision, fact, position))];
        row.Notices = [.. revision.Notices.Select((notice, position) => NoticeRow(revision, notice, position))];
    }

    /// <summary>Reads the rows of a query, then their facts, notices, and notice documents, in a fixed number of queries. The child rows come
    /// by the exact keys of the parent rows. The child rows of a revision with columns never change, and neither do the documents that its notices name.</summary>
    public static async Task<ExecutiveActionRevisionPage> ReadPageAsync(CollectionAttemptDbContext db, IQueryable<ExecutiveActionRevisionRow> query,
        CancellationToken cancellationToken)
    {
        var rows = await query.ToListAsync(cancellationToken);
        var recordIds = rows.Select(row => row.RecordId).ToArray();
        var numbers = rows.Select(row => row.RevisionNumber).ToArray();
        var facts = (await db.Set<ExecutiveActionRevisionFactRow>().FromSqlRaw(ChildrenSql("executive_action_revision_facts"), recordIds, numbers)
            .AsNoTracking().ToListAsync(cancellationToken)).ToLookup(fact => (fact.RecordId, fact.RevisionNumber));
        var notices = (await db.Set<ExecutiveActionRevisionNoticeRow>().FromSqlRaw(ChildrenSql("executive_action_revision_notices"), recordIds, numbers)
            .AsNoTracking().ToListAsync(cancellationToken)).ToLookup(notice => (notice.RecordId, notice.RevisionNumber));
        var named = notices.SelectMany(group => group).ToArray();
        var documents = await db.Set<ExecutiveActionDocumentRow>().FromSqlRaw("""
                SELECT d.* FROM executive_action_documents d JOIN unnest({0}, {1}) AS k(source_id, document_number)
                ON d.source_id = k.source_id AND d.document_number = k.document_number
                """, named.Select(notice => notice.SourceId).ToArray(), named.Select(notice => notice.DocumentNumber).ToArray())
            .AsNoTracking().ToDictionaryAsync(document => (document.SourceId, document.DocumentNumber), cancellationToken);
        return new(rows, facts, notices, documents);
    }

    public static ExecutiveActionNotice NoticeOf(ExecutiveActionDocumentRow document) => new(document.DocumentNumber, document.PublishedOn, document.NoticeUrl!,
        new(document.NoticeCitationExtractionId!, document.NoticeCitationStart!.Value, document.NoticeCitationLength!.Value));

    public static void SetNotice(ExecutiveActionDocumentRow document, ExecutiveActionNotice notice)
    {
        document.NoticeUrl = notice.Url;
        document.NoticeCitationExtractionId = notice.Citation.ExtractionId;
        document.NoticeCitationStart = notice.Citation.Start;
        document.NoticeCitationLength = notice.Citation.Length;
    }

    private static string ChildrenSql(string table) =>
        $"SELECT c.* FROM {table} c JOIN unnest({{0}}, {{1}}) AS k(id, number) ON c.record_id = k.id AND c.revision_number = k.number";

    /// <summary>The failure of a reader that meets an executive action revision row with null columns. Only the backfill fixes such a row.</summary>
    public static InvalidDataException NotBackfilled(string revision) =>
        new($"{revision} has null columns. Run the executive action revision backfill.");

    /// <summary>The reader that the shared review code uses for executive actions.</summary>
    public static async Task<IReadOnlyList<StoredRevision<ExecutiveActionRevisionRow, ExecutiveActionRevision>>> ReadAsync(CollectionAttemptDbContext db,
        IQueryable<ExecutiveActionRevisionRow> query, CancellationToken cancellationToken) =>
        (await ReadPageAsync(db, query, cancellationToken)).ToStoredRevisions();

    private static ExecutiveActionRevisionFactRow FactRow(ExecutiveActionRevision revision, ExecutiveActionFact fact, int position) => new()
    {
        RecordId = revision.RecordId,
        RevisionNumber = revision.RevisionNumber,
        Position = (short)position,
        CitationExtractionId = fact.Citation.ExtractionId,
        CitationStart = fact.Citation.Start,
        CitationLength = fact.Citation.Length
    };

    private static ExecutiveActionRevisionNoticeRow NoticeRow(ExecutiveActionRevision revision, ExecutiveActionNotice notice, int position) => new()
    {
        RecordId = revision.RecordId,
        RevisionNumber = revision.RevisionNumber,
        Position = (short)position,
        SourceId = revision.SourceId,
        DocumentNumber = notice.DocumentNumber
    };
}
