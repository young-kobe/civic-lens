using CivicLens.Application.Review;
using CivicLens.Core.Briefs;
using CivicLens.Core.Review;

namespace CivicLens.Application.Briefs;

public sealed class RedraftBrief(IBriefDraftStore store)
{
    public Task<int> ExecuteAsync(ReviewActor actor, DateOnly weekStart, CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireOwner(actor);
        BriefWeek.RequireMonday(weekStart);
        return store.RedraftAsync(weekStart, cancellationToken);
    }
}
