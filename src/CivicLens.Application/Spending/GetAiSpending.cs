using CivicLens.Application.Review;
using CivicLens.Core.Review;

namespace CivicLens.Application.Spending;

public sealed class GetAiSpending(IAiSpendingStore store)
{
    public const int ChangeLimit = 10;

    public Task<AiSpendingView> ExecuteAsync(ReviewActor actor, CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireOwner(actor);
        return store.GetAsync(ChangeLimit, cancellationToken);
    }
}
