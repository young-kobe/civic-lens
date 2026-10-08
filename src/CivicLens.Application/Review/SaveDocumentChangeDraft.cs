using CivicLens.Core.Review;

namespace CivicLens.Application.Review;

public sealed class SaveDocumentChangeDraft(IDocumentChangeReviewStore store, ReviewCatalog catalog)
{
    public Task<DocumentChangeDraftRevision> ExecuteAsync(ReviewActor actor,
        SaveDocumentChangeDraftRequest request, CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireReviewer(actor);
        ArgumentNullException.ThrowIfNull(request);
        ReviewValidation.ValidateIdempotencyKey(request.IdempotencyKey);
        var draftId = ReviewValidation.ValidateDraftId(request.DraftId);
        if (request.ExpectedRevisionNumber < 1) throw new ArgumentOutOfRangeException(nameof(request.ExpectedRevisionNumber));
        var officialIds = ReviewValidation.ValidateSelections(request.OfficialIds, catalog.OfficialIds, nameof(request.OfficialIds));
        var issueIds = ReviewValidation.ValidateSelections(request.IssueIds, catalog.IssueIds, nameof(request.IssueIds));
        var citations = ReviewValidation.ValidateCitations(request.Citations);
        var changeDateEvidence = ReviewValidation.ValidateChangeDateEvidence(request.ChangeDate,
            request.ChangeDateEvidence, citations);
        ReviewValidation.ValidateText(request.Headline, 256, nameof(request.Headline), allowEmpty: true);
        ReviewValidation.ValidateText(request.Summary, 8_000, nameof(request.Summary), allowEmpty: true);
        ReviewValidation.ValidateText(request.Significance, 4_000, nameof(request.Significance), allowEmpty: true);
        ReviewValidation.ValidateText(request.Limits, 4_000, nameof(request.Limits), allowEmpty: true);
        ReviewValidation.ValidateText(request.Institution, 256, nameof(request.Institution), allowEmpty: true);
        var revision = new DocumentChangeDraftRevision(draftId, request.ExpectedRevisionNumber + 1, "",
            actor.Subject, DateTimeOffset.UtcNow, request.Headline, request.Summary, request.Significance, request.Limits, request.Institution,
            request.ChangeDate, changeDateEvidence,
            officialIds, issueIds, citations);
        return store.SaveRevisionAsync(actor.Subject, revision, request.ExpectedRevisionNumber,
            request.IdempotencyKey, ReviewValidation.HashPayload(request), cancellationToken);
    }
}
