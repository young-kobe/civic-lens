using CivicLens.Application.Review;
using CivicLens.Core.Review;

namespace CivicLens.Application.Actions;

public sealed class GetActionReview(IActionReviewStore store)
{
    public Task<ActionReviewRead?> ExecuteAsync(ReviewActor actor, string recordId, CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireReviewer(actor);
        return store.GetAsync(ReviewValidation.ValidateHash(recordId, nameof(recordId)), cancellationToken);
    }
}
