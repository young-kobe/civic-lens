using CivicLens.Application.Review;
using CivicLens.Core.Review;

namespace CivicLens.Application.Votes;

public sealed class RequeueBlockedVoteDrafts(IVoteDraftStore store)
{
    public Task<int> ExecuteAsync(ReviewActor actor, string errorCode, CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireOwner(actor);
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        return store.RequeueBlockedAsync(errorCode, cancellationToken);
    }
}
