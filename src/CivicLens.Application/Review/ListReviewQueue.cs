using CivicLens.Application.Paging;
using CivicLens.Core.Review;

namespace CivicLens.Application.Review;

public sealed class ListReviewQueue(IReviewQueueStore store)
{
    public Task<ReviewQueuePage> ExecuteAsync(ReviewActor actor, string? cursor, int limit = PageLimit.Default,
        CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireReviewer(actor);
        return store.ListAsync(ReviewValidation.ValidatePage(cursor, limit), limit, cancellationToken);
    }
}
