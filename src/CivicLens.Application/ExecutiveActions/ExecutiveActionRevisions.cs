using System.Collections.Immutable;
using CivicLens.Application.Collection.FederalRegister;
using CivicLens.Application.Documents;
using CivicLens.Core.Documents;
using CivicLens.Core.ExecutiveActions;
using CivicLens.Core.Review;

namespace CivicLens.Application.ExecutiveActions;

public static class ExecutiveActionRevisions
{
    /// <summary>The first revision of a record. It holds the original document and every correction notice that is known.</summary>
    public static ExecutiveActionRevision Create(string sourceId, DateTimeOffset createdAtUtc, FederalRegisterEvidence original,
        IEnumerable<ExecutiveActionNotice> notices)
    {
        var document = original.Document;
        var lines = LineCitations.ExactLines(original.ExtractionId, original.ExtractionText);
        var facts = ExecutiveActionFacts.Of(document.DocumentNumber, document.Kind, document.PresidentialNumber,
            document.Title, document.SignedOn, document.PublishedOn, document.Citation, document.PresidentName, document.Url);
        var dispositionNotes = document.DispositionNotes is null ? null : CreateDispositionNotes(original, facts, document.DispositionNotes);
        return new ExecutiveActionRevision(ExecutiveActionIdentity.RecordId(sourceId, document.RecordDocumentNumber), 1, ReviewAuthor.CodeSubject,
            createdAtUtc, sourceId, document.DocumentNumber, document.Kind, document.PresidentialNumber, document.Title, document.SignedOn,
            document.PublishedOn, document.Citation, document.PresidentName, original.PersonId, document.Url,
            [.. facts.Select(fact => new ExecutiveActionFact(fact.Label, fact.Value, lines[ExecutiveActionFacts.Line(fact.Label, fact.Value)]))],
            Ordered(notices))
        { DispositionNotes = dispositionNotes };
    }

    /// <summary>The next revision keeps the original facts and the summary, and adds the notices. Code writes it, so it has the code author.</summary>
    public static ExecutiveActionRevision Add(ExecutiveActionRevision current, DateTimeOffset createdAtUtc, IEnumerable<ExecutiveActionNotice> notices) =>
        current with
        {
            RevisionNumber = current.RevisionNumber + 1,
            AuthorSubject = ReviewAuthor.CodeSubject,
            CreatedAtUtc = createdAtUtc,
            Notices = Ordered([.. current.Notices, .. notices])
        };

    /// <summary>The next revision after the summary. The analysis worker writes it, and code approves it after the checks.</summary>
    public static ExecutiveActionRevision AddSummary(ExecutiveActionRevision current, DateTimeOffset createdAtUtc, ExecutiveActionSummary summary) =>
        current with { RevisionNumber = current.RevisionNumber + 1, AuthorSubject = ReviewAuthor.AnalysisSubject, CreatedAtUtc = createdAtUtc, Summary = summary };

    public static ExecutiveActionNotice NoticeOf(FederalRegisterEvidence correction)
    {
        var document = correction.Document;
        var line = FederalRegisterRenderer.NoticeLine(document);
        return new(document.DocumentNumber, document.PublishedOn, document.Url,
            LineCitations.ExactLines(correction.ExtractionId, correction.ExtractionText)[line]);
    }

    private static ImmutableArray<ExecutiveActionNotice> Ordered(IEnumerable<ExecutiveActionNotice> notices) =>
        [.. notices.OrderBy(notice => notice.PublishedOn).ThenBy(notice => notice.DocumentNumber, StringComparer.Ordinal)];

    private static ExecutiveActionFact CreateDispositionNotes(FederalRegisterEvidence original,
        IReadOnlyList<(string Label, string Value)> facts, string notes)
    {
        var renderedFacts = string.Join('\n', facts.Select(fact => ExecutiveActionFacts.Line(fact.Label, fact.Value)));
        var renderedNotes = ExecutiveActionFacts.Line(ExecutiveActionFacts.DispositionNotes, notes);
        if (notes.Length > ExecutiveActionRevision.MaximumDispositionNotesLength)
            throw new ArgumentException("Disposition notes exceed the maximum extracted text length.", nameof(notes));
        var start = renderedFacts.Length + 1;
        if (!string.Equals(original.ExtractionText, $"{renderedFacts}\n{renderedNotes}", StringComparison.Ordinal))
            throw new ArgumentException("Disposition notes must match the rendered original document extraction.", nameof(original));
        return new ExecutiveActionFact(ExecutiveActionFacts.DispositionNotes, notes,
            new SourceSpan(original.ExtractionId, start, renderedNotes.Length));
    }
}
