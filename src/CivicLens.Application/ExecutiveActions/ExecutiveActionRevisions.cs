using CivicLens.Application.Collection.FederalRegister;
using CivicLens.Application.Documents;
using CivicLens.Core.ExecutiveActions;
using CivicLens.Core.Review;

namespace CivicLens.Application.ExecutiveActions;

public static class ExecutiveActionRevisions
{
    public static ExecutiveActionRevision Create(string sourceId, int revisionNumber, DateTimeOffset createdAtUtc, FederalRegisterEvidence evidence)
    {
        var document = evidence.Document;
        var recordId = ExecutiveActionIdentity.RecordId(sourceId, document.RecordDocumentNumber);
        var lines = LineCitations.ExactLines(evidence.ExtractionId, evidence.ExtractionText);
        var facts = ExecutiveActionFacts.Of(document.DocumentNumber, document.CorrectionOf, document.Kind, document.PresidentialNumber,
            document.Title, document.SignedOn, document.PublishedOn, document.Citation, document.PresidentName, document.Url);
        return new ExecutiveActionRevision(recordId, revisionNumber, ReviewAuthor.CodeSubject, createdAtUtc, sourceId, document.DocumentNumber,
            document.CorrectionOf, document.Kind, document.PresidentialNumber, document.Title, document.SignedOn, document.PublishedOn,
            document.Citation, document.PresidentName, evidence.PersonId, document.Url,
            [.. facts.Select(fact => new ExecutiveActionFact(fact.Label, fact.Value, lines[ExecutiveActionFacts.Line(fact.Label, fact.Value)]))]);
    }
}
