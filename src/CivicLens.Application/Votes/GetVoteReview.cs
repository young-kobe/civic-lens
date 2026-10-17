using CivicLens.Application.Review;
using CivicLens.Core.Review;

namespace CivicLens.Application.Votes;

public sealed class GetVoteReview(IVoteReviewStore store)
{
    public Task<VoteReviewRead?> ExecuteAsync(ReviewActor actor, string recordId, CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireReviewer(actor);
        return store.GetAsync(ReviewValidation.ValidateHash(recordId, nameof(recordId)), cancellationToken);
    }
}
