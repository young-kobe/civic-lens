using CivicLens.Core.Review;

namespace CivicLens.Application.Review;

public sealed class DecideDocumentChangeReview(IDocumentChangeReviewStore store, ReviewCatalog catalog)
{
    public Task<ReviewDecision> ExecuteAsync(ReviewActor actor,
        DecideDocumentChangeReviewRequest request, CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireReviewer(actor);
        ArgumentNullException.ThrowIfNull(request);
        ReviewValidation.ValidateIdempotencyKey(request.IdempotencyKey);
        var draftId = ReviewValidation.ValidateDraftId(request.DraftId);
        var decision = ReviewValidation.CreateDecision(actor, draftId, request.ExpectedRevisionNumber,
            request.ExpectedReviewStateVersion, request.Kind, request.Note, request.ResolvedDecisionIds);
        return store.DecideAsync(actor.Subject, decision, request.ExpectedRevisionNumber,
            request.ExpectedReviewStateVersion, request.IdempotencyKey, ReviewValidation.HashPayload(request),
            catalog.OfficialIds, catalog.IssueIds, cancellationToken);
    }
}
