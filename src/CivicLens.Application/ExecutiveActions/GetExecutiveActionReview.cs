using CivicLens.Core.Review;
using CivicLens.Application.Review;

namespace CivicLens.Application.ExecutiveActions;

public sealed class GetExecutiveActionReview(IExecutiveActionReviewStore store)
{
    public Task<ExecutiveActionReviewRead?> ExecuteAsync(ReviewActor actor, string recordId, CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireReviewer(actor);
        return store.GetAsync(ReviewValidation.ValidateHash(recordId, nameof(recordId)), cancellationToken);
    }
}
