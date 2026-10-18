using CivicLens.Application.Review;
using CivicLens.Core.Review;

namespace CivicLens.Application.Relationships;

public sealed class RefreshRelationshipPairs(IRelationshipDraftStore store)
{
    public Task<int> ExecuteAsync(ReviewActor actor, CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireOwner(actor);
        return store.RefreshPairsAsync(cancellationToken);
    }
}
