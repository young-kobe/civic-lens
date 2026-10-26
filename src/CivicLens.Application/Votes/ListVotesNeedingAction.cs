using CivicLens.Application.Paging;
using CivicLens.Application.Review;
using CivicLens.Core.Review;

namespace CivicLens.Application.Votes;

public sealed class ListVotesNeedingAction(IVoteReviewStore store)
{
    public Task<VotesNeedingAction> ExecuteAsync(ReviewActor actor, int limit = PageLimit.Default,
        CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireReviewer(actor);
        return store.ListNeedingActionAsync(limit, cancellationToken);
    }
}
