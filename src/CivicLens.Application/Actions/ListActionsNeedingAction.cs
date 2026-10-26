using CivicLens.Application.Paging;
using CivicLens.Application.Review;
using CivicLens.Core.Review;

namespace CivicLens.Application.Actions;

public sealed class ListActionsNeedingAction(IActionReviewStore store)
{
    public Task<ActionsNeedingAction> ExecuteAsync(ReviewActor actor, int limit = PageLimit.Default,
        CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireReviewer(actor);
        return store.ListNeedingActionAsync(limit, cancellationToken);
    }
}
