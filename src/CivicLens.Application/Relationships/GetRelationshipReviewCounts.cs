using CivicLens.Application.Review;
using CivicLens.Core.Review;

namespace CivicLens.Application.Relationships;

public sealed class GetRelationshipReviewCounts(IRelationshipReviewStore store)
{
    public Task<RelationshipReviewCounts> ExecuteAsync(ReviewActor actor, CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireReviewer(actor);
        return store.CountAsync(cancellationToken);
    }
}
