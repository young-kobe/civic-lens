using CivicLens.Application.Review;
using CivicLens.Core.Review;

namespace CivicLens.Application.Votes;

public sealed class GetVoteReviewCounts(IVoteReviewStore store)
{
    public Task<VoteReviewCounts> ExecuteAsync(ReviewActor actor, CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireReviewer(actor);
        return store.CountAsync(cancellationToken);
    }
}
