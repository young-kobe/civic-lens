using CivicLens.Application.Review;
using CivicLens.Core.Review;

namespace CivicLens.Application.Actions;

public sealed class RequeueBlockedActionDrafts(IActionDraftStore store)
{
    public Task<int> ExecuteAsync(ReviewActor actor, string errorCode, CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireOwner(actor);
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        return store.RequeueBlockedAsync(errorCode, cancellationToken);
    }
}
