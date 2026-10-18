using CivicLens.Application.Review;
using CivicLens.Core.Review;

namespace CivicLens.Application.Relationships;

public sealed class GetRelationshipReview(IRelationshipReviewStore store)
{
    public Task<RelationshipReviewRead?> ExecuteAsync(ReviewActor actor, string recordId, CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireReviewer(actor);
        return store.GetAsync(ReviewValidation.ValidateHash(recordId, nameof(recordId)), cancellationToken);
    }
}
