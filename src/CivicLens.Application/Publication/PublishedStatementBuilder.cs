using CivicLens.Application.Statements;
using CivicLens.Core.Documents;
using CivicLens.Core.Statements;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

internal sealed class PublishedStatementBuilder(PublishedRecordRules rules)
{
    public PublishedStatement Build(PublishableStatement statement, PublishedRecordEntry? previous, PublishedCorrection[] corrections, DateTimeOffset now)
    {
        var (review, url, extraction) = statement;
        var revision = review.CurrentRevision!;
        var record = new PublishedStatement
        {
            RecordId = review.CandidateId,
            RevisionNumber = revision.RevisionNumber,
            Headline = revision.Headline,
            Summary = revision.Summary,
            StatementDate = revision.StatementDate,
            StatementDateEvidence = revision.StatementDateEvidence is null ? null : PublishedRecordRules.Cite(revision.StatementDateEvidence, extraction),
            Speakers = rules.MapOfficials(review.CandidateId, revision.SpeakerOfficialIds),
            OtherSpeakers = [.. revision.OtherSpeakers],
            IssueIds = [.. revision.IssueIds],
            PolicyAreaIds = [.. revision.PolicyAreaIds],
            Quotations = [.. revision.Quotations.Select(quotation => Quote(quotation, extraction))],
            BillNumbers = [.. revision.BillNumbers],
            Source = new()
            {
                ExtractionId = extraction.ExtractionId,
                Url = url,
                ObservedAtUtc = extraction.SourceAttempt.ObservedAt,
                Text = extraction.Text
            },
            ApprovedAtUtc = PublishedRecordRules.GetApprovalTime(revision.RevisionNumber, review.Decisions),
            FirstPublishedAtUtc = previous?.FirstPublishedAtUtc ?? now,
            Corrections = corrections
        };
        record.Validate();
        return record;
    }

    private static PublishedQuotation Quote(StatementQuotation quotation, DocumentExtraction extraction)
    {
        var citation = PublishedRecordRules.Cite(quotation.Span, extraction);
        return new()
        {
            ExtractionId = citation.ExtractionId,
            Start = citation.Start,
            Length = citation.Length,
            Quote = citation.Quote,
            Kind = KindOf(quotation.Kind)
        };
    }

    private static PublishedQuotationKind KindOf(StatementQuotationKind kind) => kind switch
    {
        StatementQuotationKind.DirectQuotation => PublishedQuotationKind.DirectQuotation,
        StatementQuotationKind.ReleaseText => PublishedQuotationKind.ReleaseText,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Quotation kind has no published form.")
    };
}
