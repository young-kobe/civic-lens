using CivicLens.Application.Documents;
using CivicLens.Application.Review;
using CivicLens.Core.Documents;
using CivicLens.Core.Review;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

internal sealed class PublishedRecordBuilder(IDocumentChangeReviewStore reviews, IDocumentExtractionStore extractions,
    PublicationCatalog catalog)
{
    public static void RequirePublishable(DocumentChangeReview review)
    {
        var status = DocumentChangeReviewPolicy.GetCurrentStatus(review.CurrentRevision, review.Decisions,
            review.UnresolvedConcerns);
        if (status != DocumentChangeReviewStatus.Approved || !review.UnresolvedConcerns.IsEmpty)
            throw new ArgumentException(
                $"Draft {review.DraftId} is not approved or has unresolved concerns.", nameof(review));
    }

    public async Task<PublishedDocumentChange> BuildAsync(DocumentChangeReview review,
        PublishedRecordEntry? previous, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var revision = review.CurrentRevision;
        var comparison = await reviews.GetEligibleComparisonAsync(revision.ComparisonId, cancellationToken)
            ?? throw new InvalidDataException($"Comparison for draft {review.DraftId} is missing.");
        var before = await LoadExtractionAsync(comparison.Comparison.BeforeExtractionId, cancellationToken);
        var after = await LoadExtractionAsync(comparison.Comparison.AfterExtractionId, cancellationToken);

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
            ChangeDateEvidence = revision.ChangeDateEvidence is null ? null : Cite(revision.ChangeDateEvidence, before, after),
            Officials = MapOfficials(review),
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
            Citations = [.. revision.Citations.Select(citation => Cite(citation, before, after))],
            ApprovedAtUtc = GetApprovalTime(review),
            FirstPublishedAtUtc = previous?.FirstPublishedAtUtc ?? now
        };
        record.Validate();
        return record;
    }

    private async Task<DocumentExtraction> LoadExtractionAsync(string extractionId, CancellationToken cancellationToken)
    {
        var extraction = await extractions.GetAsync(extractionId, cancellationToken)
            ?? throw new InvalidDataException("A compared extraction is missing.");
        if (extraction.ExtractionId != extractionId)
            throw new InvalidDataException("A retained extraction does not match its comparison.");
        return extraction;
    }

    private PublishedOfficial[] MapOfficials(DocumentChangeReview review) =>
        [.. review.CurrentRevision.OfficialIds.Select(id => new PublishedOfficial
        {
            Id = id,
            Name = catalog.OfficialNames.TryGetValue(id, out var name)
                ? name
                : throw new ArgumentException($"Official {id} in draft {review.DraftId} has no configured public name.")
        })];

    private static DateTimeOffset GetApprovalTime(DocumentChangeReview review) =>
        review.Decisions
            .Where(item => item.RevisionNumber == review.CurrentRevision.RevisionNumber && item.Kind == ReviewDecisionKind.Approve)
            .MaxBy(item => item.ReviewStateVersion)!.CreatedAtUtc;

    private static PublishedCitation Cite(DocumentChangeCitation citation, DocumentExtraction before, DocumentExtraction after)
    {
        var extraction = citation.ExtractionId == before.ExtractionId ? before
            : citation.ExtractionId == after.ExtractionId ? after
            : throw new InvalidDataException("A citation names an extraction outside its comparison.");
        var span = new DocumentTextSpan(extraction, citation.Start, citation.Length);
        return new() { ExtractionId = span.ExtractionId, Start = span.Start, Length = span.Length, Quote = span.Quote };
    }

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
