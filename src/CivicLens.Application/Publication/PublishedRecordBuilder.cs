using CivicLens.Application.Review;
using CivicLens.Core.Documents;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

internal sealed class PublishedRecordBuilder(PublishedRecordRules rules)
{
    public PublishedDocumentChange Build(PublishableDocumentChange change, PublishedRecordEntry? previous, PublishedCorrection[] corrections,
        DateTimeOffset now)
    {
        var (review, comparison, before, after) = change;
        var revision = review.CurrentRevision;
        PublishedCitation Cite(SourceSpan citation) => PublishedRecordRules.Cite(citation, ExtractionOf(citation, before, after));

        var record = new PublishedDocumentChange
        {
            RecordId = review.DraftId,
            RevisionNumber = revision.RevisionNumber,
            Headline = revision.Headline,
            Summary = revision.Summary,
            Significance = revision.Significance,
            Limits = revision.Limits,
            Institution = revision.Institution,
            ChangeDate = revision.ChangeDate,
            ChangeDateEvidence = revision.ChangeDateEvidence is null ? null : Cite(revision.ChangeDateEvidence),
            Officials = rules.MapOfficials(review.DraftId, revision.OfficialIds),
            IssueIds = [.. revision.IssueIds],
            Before = new()
            {
                ExtractionId = before.ExtractionId,
                Url = comparison.BeforeFinalUrl,
                ObservedAtUtc = comparison.BeforeObservedAtUtc,
                Text = before.Text
            },
            After = new()
            {
                ExtractionId = after.ExtractionId,
                Url = comparison.AfterFinalUrl,
                ObservedAtUtc = comparison.AfterObservedAtUtc,
                Text = after.Text
            },
            Changes = [.. comparison.Comparison.Hunks.Select(MapChange)],
            Citations = [.. revision.Citations.Select(Cite)],
            ApprovedAtUtc = PublishedRecordRules.GetApprovalTime(revision.RevisionNumber, review.Decisions),
            FirstPublishedAtUtc = previous?.FirstPublishedAtUtc ?? now,
            Corrections = corrections
        };
        record.Validate();
        return record;
    }

    private static DocumentExtraction ExtractionOf(SourceSpan citation, DocumentExtraction before, DocumentExtraction after) =>
        citation.ExtractionId == before.ExtractionId ? before
            : citation.ExtractionId == after.ExtractionId ? after
            : throw new InvalidDataException("A citation names an extraction outside its comparison.");

    private static PublishedChange MapChange(DocumentComparisonHunk hunk) => new()
    {
        BeforeStart = hunk.BeforeStart,
        BeforeLength = hunk.BeforeLength,
        AfterStart = hunk.AfterStart,
        AfterLength = hunk.AfterLength,
        BeforeContextStart = hunk.BeforeContextStart,
        BeforeContextLength = hunk.BeforeContext.Length,
        AfterContextStart = hunk.AfterContextStart,
        AfterContextLength = hunk.AfterContext.Length,
        WordEdits = [.. hunk.WordEdits.Select(edit => new PublishedWordEdit
        {
            BeforeStart = edit.BeforeStart,
            BeforeLength = edit.BeforeLength,
            AfterStart = edit.AfterStart,
            AfterLength = edit.AfterLength
        })]
    };
}
