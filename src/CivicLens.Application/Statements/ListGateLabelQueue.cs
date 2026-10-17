using CivicLens.Application.Paging;
using CivicLens.Application.Review;
using CivicLens.Core.Review;

namespace CivicLens.Application.Statements;

public sealed class ListGateLabelQueue(IGateLabelStore store)
{
    public Task<GateLabelQueue> ExecuteAsync(ReviewActor actor, int limit = PageLimit.Default, CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireOwner(actor);
        PageLimit.Validate(limit);
        return store.ListUnlabeledAsync(limit, cancellationToken);
    }
}
