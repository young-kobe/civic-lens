using CivicLens.Application.ExecutiveActions;
using CivicLens.Core.Documents;
using CivicLens.Core.ExecutiveActions;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

internal sealed class PublishedExecutiveActionBuilder(PublishedRecordRules rules)
{
    public PublishedExecutiveAction Build(PublishableExecutiveAction action, DateTimeOffset? firstPublishedAtUtc, PublishedCorrection[] corrections,
        DateTimeOffset now)
    {
        var (review, extractions) = action;
        var revision = review.CurrentRevision;

        var record = new PublishedExecutiveAction
        {
            RecordId = review.RecordId,
            RevisionNumber = revision.RevisionNumber,
            Headline = revision.Title,
            Kind = KindOf(revision.Kind),
            DocumentNumber = revision.DocumentNumber,
            CorrectionOf = revision.CorrectionOf,
            SignedOn = revision.SignedOn,
            PublishedOn = revision.PublishedOn,
            FederalRegisterCitation = revision.FederalRegisterCitation,
            DocumentUrl = revision.DocumentUrl,
            President = rules.MapOfficial(review.RecordId, revision.PersonId),
            Facts = [.. revision.Facts.Select(fact => new PublishedExecutiveActionFact
            {
                Label = fact.Label,
                Value = fact.Value,
                Citation = PublishedRecordRules.Cite(fact.Citation, extractions[fact.Citation.ExtractionId])
            })],
            Sources = PublishedRecordRules.Sources(revision.Citations, extractions),
            ApprovedAtUtc = PublishedRecordRules.GetApprovalTime(revision.RevisionNumber, review.Decisions),
            FirstPublishedAtUtc = firstPublishedAtUtc ?? now,
            Corrections = corrections
        };
        record.Validate();
        return record;
    }

    private static PublishedExecutiveActionKind KindOf(ExecutiveActionKind kind) => kind switch
    {
        ExecutiveActionKind.ExecutiveOrder => PublishedExecutiveActionKind.ExecutiveOrder,
        ExecutiveActionKind.Proclamation => PublishedExecutiveActionKind.Proclamation,
        ExecutiveActionKind.Notice => PublishedExecutiveActionKind.Notice,
        ExecutiveActionKind.Memorandum => PublishedExecutiveActionKind.Memorandum,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Executive action kind has no published form.")
    };
}
