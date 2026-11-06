using CivicLens.Application.Review;
using CivicLens.Core.Review;

namespace CivicLens.Application.Spending;

public sealed class SaveMonthlyCap(IAiSpendingStore store)
{
    public Task ExecuteAsync(ReviewActor actor, decimal capDollars, CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireOwner(actor);
        var capMicros = AiSpendingPolicy.ToMicros(capDollars);
        return store.SaveCapAsync(actor.Subject, capMicros, cancellationToken);
    }
}
