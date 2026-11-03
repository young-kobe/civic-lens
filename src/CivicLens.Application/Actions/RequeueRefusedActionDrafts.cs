using CivicLens.Application.Review;
using CivicLens.Core.Review;

namespace CivicLens.Application.Actions;

public sealed class RequeueRefusedActionDrafts(IActionDraftStore store)
{
    public Task<int> ExecuteAsync(ReviewActor actor, CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireOwner(actor);
        return store.RequeueRefusedAsync(cancellationToken);
    }
}
