using System.Collections.Immutable;
using CivicLens.Application.Collection.FederalRegister;
using CivicLens.Application.Documents;
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
        return new ExecutiveActionRevision(ExecutiveActionIdentity.RecordId(sourceId, document.RecordDocumentNumber), 1, ReviewAuthor.CodeSubject,
            createdAtUtc, sourceId, document.DocumentNumber, document.Kind, document.PresidentialNumber, document.Title, document.SignedOn,
            document.PublishedOn, document.Citation, document.PresidentName, original.PersonId, document.Url,
            [.. facts.Select(fact => new ExecutiveActionFact(fact.Label, fact.Value, lines[ExecutiveActionFacts.Line(fact.Label, fact.Value)]))],
            Ordered(notices));
    }

    /// <summary>The next revision keeps the original facts and adds the notice.</summary>
    public static ExecutiveActionRevision Add(ExecutiveActionRevision current, DateTimeOffset createdAtUtc, ExecutiveActionNotice notice) =>
        current with { RevisionNumber = current.RevisionNumber + 1, CreatedAtUtc = createdAtUtc, Notices = Ordered([.. current.Notices, notice]) };

    public static ExecutiveActionNotice NoticeOf(FederalRegisterEvidence correction)
    {
        var document = correction.Document;
        var line = FederalRegisterRenderer.NoticeLine(document);
        return new(document.DocumentNumber, document.PublishedOn, document.Url,
            LineCitations.ExactLines(correction.ExtractionId, correction.ExtractionText)[line]);
    }

    private static ImmutableArray<ExecutiveActionNotice> Ordered(IEnumerable<ExecutiveActionNotice> notices) =>
        [.. notices.OrderBy(notice => notice.PublishedOn).ThenBy(notice => notice.DocumentNumber, StringComparer.Ordinal)];
}
