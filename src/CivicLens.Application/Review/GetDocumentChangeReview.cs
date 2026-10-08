using CivicLens.Core.Review;

namespace CivicLens.Application.Review;

public sealed class GetDocumentChangeReview(IDocumentChangeReviewStore store, ReviewCatalog catalog)
{
    public Task<DocumentChangeReview?> ExecuteAsync(ReviewActor actor, string draftId,
        CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireReviewer(actor);
        _ = catalog;
        return store.GetAsync(ReviewValidation.ValidateDraftId(draftId), cancellationToken);
    }
}
