using CivicLens.Application.Review;
using CivicLens.Core.Review;

namespace CivicLens.Application.Briefs;

public sealed class GetBriefReviewCounts(IBriefReviewStore store)
{
    public Task<BriefReviewCounts> ExecuteAsync(ReviewActor actor, CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireReviewer(actor);
        return store.CountAsync(cancellationToken);
    }
}
