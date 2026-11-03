using System.Collections.Immutable;
using CivicLens.Core.Documents;
using CivicLens.Core.ExecutiveActions;
using CivicLens.Infrastructure.ExecutiveActions.Persistence;
using CivicLens.Infrastructure.Review;

namespace CivicLens.Infrastructure.ExecutiveActions;

internal sealed record ExecutiveActionRevisionPage(IReadOnlyList<ExecutiveActionRevisionRow> Rows,
    ILookup<(string RecordId, int RevisionNumber), ExecutiveActionRevisionFactRow> Facts,
    ILookup<(string RecordId, int RevisionNumber), ExecutiveActionRevisionNoticeRow> Notices,
    IReadOnlyDictionary<(string SourceId, string DocumentNumber), ExecutiveActionDocumentRow> Documents)
{
    public ExecutiveActionRevision Rebuild(ExecutiveActionRevisionRow row)
    {
        if (row.SourceId is null)
            throw ExecutiveActionRevisionRows.NotBackfilled($"Executive action revision {row.RecordId} {row.RevisionNumber}");
        return new(row.RecordId, row.RevisionNumber, row.AuthorSubject, new DateTimeOffset(row.CreatedAtUtcTicks, TimeSpan.Zero),
            row.SourceId, row.DocumentNumber!, row.Kind!.Value, row.PresidentialNumber, row.Title!, row.SignedOn!.Value, row.PublishedOn!.Value,
            row.FederalRegisterCitation!, row.PresidentName!, row.PersonId!, row.DocumentUrl!, FactsOf(row), NoticesOf(row));
    }

    public IReadOnlyList<StoredRevision<ExecutiveActionRevisionRow, ExecutiveActionRevision>> ToStoredRevisions() =>
        [.. Rows.Select(row => new StoredRevision<ExecutiveActionRevisionRow, ExecutiveActionRevision>(row, () => Rebuild(row)))];

    private ImmutableArray<ExecutiveActionFact> FactsOf(ExecutiveActionRevisionRow row)
    {
        var lines = ExecutiveActionFacts.Of(row.DocumentNumber!, row.Kind!.Value, row.PresidentialNumber, row.Title!, row.SignedOn!.Value,
            row.PublishedOn!.Value, row.FederalRegisterCitation!, row.PresidentName!, row.DocumentUrl!);
        var spans = Facts[(row.RecordId, row.RevisionNumber)].OrderBy(fact => fact.Position).ToArray();
        if (spans.Length != lines.Count)
            throw new InvalidDataException($"Executive action revision {row.RecordId} {row.RevisionNumber} has {spans.Length} fact rows for {lines.Count} fact lines.");
        return [.. lines.Zip(spans, (line, span) => new ExecutiveActionFact(line.Label, line.Value,
            new SourceSpan(span.CitationExtractionId, span.CitationStart, span.CitationLength)))];
    }

    private ImmutableArray<ExecutiveActionNotice> NoticesOf(ExecutiveActionRevisionRow row) =>
        [.. Notices[(row.RecordId, row.RevisionNumber)].OrderBy(notice => notice.Position).Select(notice => NoticeOf(row, Documents[(notice.SourceId, notice.DocumentNumber)]))];

    private static ExecutiveActionNotice NoticeOf(ExecutiveActionRevisionRow row, ExecutiveActionDocumentRow document)
    {
        if (document.RecordId != row.RecordId || document.NoticeUrl is null)
            throw new InvalidDataException($"Executive action revision {row.RecordId} {row.RevisionNumber} names document {document.DocumentNumber}, which is no notice of its record.");
        return ExecutiveActionRevisionRows.NoticeOf(document);
    }
}
