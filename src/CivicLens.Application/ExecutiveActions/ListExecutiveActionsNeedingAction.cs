using CivicLens.Core.Review;
using CivicLens.Application.Paging;
using CivicLens.Application.Review;

namespace CivicLens.Application.ExecutiveActions;

public sealed class ListExecutiveActionsNeedingAction(IExecutiveActionReviewStore store)
{
    public Task<ExecutiveActionsNeedingAction> ExecuteAsync(ReviewActor actor, int limit = PageLimit.Default,
        CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireReviewer(actor);
        return store.ListNeedingActionAsync(limit, cancellationToken);
    }
}
