using CivicLens.Application.Review;
using CivicLens.Core.Review;

namespace CivicLens.Application.Briefs;

public sealed class GetBriefReview(IBriefReviewStore store)
{
    public Task<BriefReviewRead?> ExecuteAsync(ReviewActor actor, string recordId, CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireReviewer(actor);
        return store.GetAsync(ReviewValidation.ValidateHash(recordId, nameof(recordId)), cancellationToken);
    }
}
