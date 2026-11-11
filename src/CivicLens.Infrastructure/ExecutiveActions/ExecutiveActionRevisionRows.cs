using CivicLens.Application;
using CivicLens.Core.ExecutiveActions;
using CivicLens.Core.Review;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.ExecutiveActions.Persistence;
using CivicLens.Infrastructure.Review;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Infrastructure.ExecutiveActions;

internal static class ExecutiveActionRevisionRows
{
    public static ExecutiveActionRevisionRow Create(ExecutiveActionRevision revision, CodeVersion codeVersion)
    {
        var row = new ExecutiveActionRevisionRow
        {
            RecordId = revision.RecordId,
            RevisionNumber = revision.RevisionNumber,
            AuthorSubject = revision.AuthorSubject,
            AuthorKind = ReviewAuthor.KindOf(revision.AuthorSubject),
            CodeVersion = codeVersion.Sha,
            RevisionJson = ReviewJson.Serialize(revision),
            CreatedAtUtcTicks = revision.CreatedAtUtc.UtcTicks
        };
        FillColumns(row, revision);
        return row;
    }

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
        row.DispositionNotes = revision.DispositionNotes?.Value;
        row.DispositionNotesExtractionId = revision.DispositionNotes?.Citation.ExtractionId;
        row.DispositionNotesStart = revision.DispositionNotes?.Citation.Start;
        row.DispositionNotesLength = revision.DispositionNotes?.Citation.Length;
        row.Facts = [.. revision.Facts.Select((fact, position) => FactRow(revision, fact, position))];
        row.Notices = [.. revision.Notices.Select((notice, position) => NoticeRow(revision, notice, position))];
        row.DispositionTargets = [.. TargetRows(revision)];
    }

    public static async Task<ExecutiveActionRevisionPage> ReadPageAsync(CollectionAttemptDbContext db, IQueryable<ExecutiveActionRevisionRow> query,
        CancellationToken cancellationToken)
    {
        var rows = await query.ToListAsync(cancellationToken);
        var recordIds = rows.Select(row => row.RecordId).ToArray();
        var numbers = rows.Select(row => row.RevisionNumber).ToArray();
        var facts = (await db.Set<ExecutiveActionRevisionFactRow>().FromSqlRaw(ReviewQueries.RevisionChildren("executive_action_revision_facts"), recordIds, numbers)
            .AsNoTracking().ToListAsync(cancellationToken)).ToLookup(fact => (fact.RecordId, fact.RevisionNumber));
        var notices = (await db.Set<ExecutiveActionRevisionNoticeRow>().FromSqlRaw(ReviewQueries.RevisionChildren("executive_action_revision_notices"), recordIds, numbers)
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

    public static InvalidDataException NotBackfilled(string revision) =>
        new($"{revision} has null columns. Run the executive action revision backfill.");

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

    /// <summary>The store parses the verbatim notes once, in the transaction that writes the revision. A correction revision keeps the original notes, so it keeps their targets.</summary>
    public static IEnumerable<ExecutiveActionRevisionDispositionTargetRow> TargetRows(ExecutiveActionRevision revision) =>
        TargetRows(revision.RecordId, revision.RevisionNumber, revision.DispositionNotes?.Value);

    public static IEnumerable<ExecutiveActionRevisionDispositionTargetRow> TargetRows(string recordId, int revisionNumber, string? notes) =>
        notes is null ? [] : ExecutiveActionDispositionTargets.Parse(notes).Select((target, position) => new ExecutiveActionRevisionDispositionTargetRow
        {
            RecordId = recordId,
            RevisionNumber = revisionNumber,
            Position = position,
            Relation = target.Relation,
            Kind = target.Kind,
            Number = target.Number,
            ReferenceStart = target.Start,
            ReferenceLength = target.Length
        });

    private static ExecutiveActionRevisionNoticeRow NoticeRow(ExecutiveActionRevision revision, ExecutiveActionNotice notice, int position) => new()
    {
        RecordId = revision.RecordId,
        RevisionNumber = revision.RevisionNumber,
        Position = (short)position,
        SourceId = revision.SourceId,
        DocumentNumber = notice.DocumentNumber
    };
}
