using System.Collections.Immutable;
using CivicLens.Core.Documents;
using CivicLens.Core.ExecutiveActions;
using CivicLens.Infrastructure.ExecutiveActions.Persistence;
using CivicLens.Infrastructure.Review;

namespace CivicLens.Infrastructure.ExecutiveActions;

internal sealed record ExecutiveActionRevisionPage(IReadOnlyList<ExecutiveActionRevisionRow> Rows,
    ILookup<(string RecordId, int RevisionNumber), ExecutiveActionRevisionFactRow> Facts,
    ILookup<(string RecordId, int RevisionNumber), ExecutiveActionRevisionNoticeRow> Notices,
    IReadOnlyDictionary<(string SourceId, string DocumentNumber), ExecutiveActionDocumentRow> Documents,
    ILookup<(string RecordId, int RevisionNumber), ExecutiveActionRevisionSummarySentenceRow> SummarySentences) : IRevisionRowPage<ExecutiveActionRevisionRow>
{
    public ExecutiveActionRevision Rebuild(ExecutiveActionRevisionRow row)
    {
        if (row.SourceId is null)
            throw ExecutiveActionRevisionRows.NotBackfilled($"Executive action revision {row.RecordId} {row.RevisionNumber}");
        return new(row.RecordId, row.RevisionNumber, row.AuthorSubject, new DateTimeOffset(row.CreatedAtUtcTicks, TimeSpan.Zero),
            row.SourceId, row.DocumentNumber!, row.Kind!.Value, row.PresidentialNumber, row.Title!, row.SignedOn!.Value, row.PublishedOn!.Value,
            row.FederalRegisterCitation!, row.PresidentName!, row.PersonId!, row.DocumentUrl!, FactsOf(row), NoticesOf(row))
        { DispositionNotes = DispositionNotesOf(row), Summary = SummaryOf(row) };
    }

    public IReadOnlyList<StoredRevision<ExecutiveActionRevisionRow, ExecutiveActionRevision>> ToStoredRevisions() =>
        [.. Rows.Select(row => new StoredRevision<ExecutiveActionRevisionRow, ExecutiveActionRevision>(row, () => Rebuild(row)))];

    private ImmutableArray<ExecutiveActionFact> FactsOf(ExecutiveActionRevisionRow row)
    {
        var lines = ExecutiveActionFacts.Of(row.DocumentNumber!, row.Kind!.Value, row.PresidentialNumber, row.Title!, row.SignedOn!.Value,
            row.PublishedOn!.Value, row.FederalRegisterCitation!, row.PresidentName!, row.DocumentUrl!);
        var spans = Facts[(row.RecordId, row.RevisionNumber)].OrderBy(fact => fact.Position).ToArray();
        var expected = lines.Count;
        if (spans.Length != expected || !spans.Select(span => (int)span.Position).SequenceEqual(Enumerable.Range(0, expected)))
            throw new InvalidDataException($"Executive action revision {row.RecordId} {row.RevisionNumber} has incomplete fact rows.");
        return [.. lines.Zip(spans, (line, span) => new ExecutiveActionFact(line.Label, line.Value,
            new SourceSpan(span.CitationExtractionId, span.CitationStart, span.CitationLength)))];
    }

    private ExecutiveActionSummary? SummaryOf(ExecutiveActionRevisionRow row)
    {
        var sentences = SummarySentences[(row.RecordId, row.RevisionNumber)].OrderBy(sentence => sentence.Position).ToArray();
        if (row.SummaryTextOrigin is null || row.SummaryCut is null)
        {
            if (sentences.Length > 0 || row.SummaryTextOrigin is not null || row.SummaryCut is not null)
                throw new InvalidDataException($"Executive action revision {row.RecordId} {row.RevisionNumber} has incomplete summary columns.");
            return null;
        }
        if (!sentences.Select(sentence => (int)sentence.Position).SequenceEqual(Enumerable.Range(0, sentences.Length)))
            throw new InvalidDataException($"Executive action revision {row.RecordId} {row.RevisionNumber} has incomplete summary rows.");
        return new(row.SummaryTextOrigin.Value, row.SummaryCut.Value, [.. sentences.Select(sentence => new ExecutiveActionSummarySentence(sentence.Sentence,
            new SourceSpan(sentence.QuoteExtractionId, sentence.QuoteStart, sentence.QuoteLength)))]);
    }

    private static ExecutiveActionFact? DispositionNotesOf(ExecutiveActionRevisionRow row)
    {
        if (row.DispositionNotes is null)
        {
            if (row.DispositionNotesExtractionId is not null || row.DispositionNotesStart is not null || row.DispositionNotesLength is not null)
                throw new InvalidDataException("Executive action disposition note columns are incomplete.");
            return null;
        }
        if (row.DispositionNotesExtractionId is null || row.DispositionNotesStart is null || row.DispositionNotesLength is null)
            throw new InvalidDataException("Executive action disposition note citation is incomplete.");
        return new(ExecutiveActionFacts.DispositionNotes, row.DispositionNotes,
            new SourceSpan(row.DispositionNotesExtractionId, row.DispositionNotesStart.Value, row.DispositionNotesLength.Value));
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
