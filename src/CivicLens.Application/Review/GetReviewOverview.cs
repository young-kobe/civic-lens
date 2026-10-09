using CivicLens.Core.Review;

namespace CivicLens.Application.Review;

public sealed class GetReviewOverview(IDocumentChangeReviewStore store)
{
    public Task<ReviewOverview> ExecuteAsync(ReviewActor actor, CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireReviewer(actor);
        return store.GetOverviewAsync(cancellationToken);
    }
}
