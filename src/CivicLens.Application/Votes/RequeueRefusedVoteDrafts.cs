using CivicLens.Application.Review;
using CivicLens.Core.Review;

namespace CivicLens.Application.Votes;

public sealed class RequeueRefusedVoteDrafts(IVoteDraftStore store)
{
    public Task<int> ExecuteAsync(ReviewActor actor, CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireOwner(actor);
        return store.RequeueRefusedAsync(cancellationToken);
    }
}
