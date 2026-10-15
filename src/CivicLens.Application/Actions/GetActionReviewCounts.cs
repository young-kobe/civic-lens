using CivicLens.Application.Review;
using CivicLens.Core.Review;

namespace CivicLens.Application.Actions;

public sealed class GetActionReviewCounts(IActionReviewStore store)
{
    public Task<ActionReviewCounts> ExecuteAsync(ReviewActor actor, CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireReviewer(actor);
        return store.CountAsync(cancellationToken);
    }
}
