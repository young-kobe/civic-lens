using CivicLens.Application.Paging;
using CivicLens.Application.Review;
using CivicLens.Core.Review;

namespace CivicLens.Application.Actions;

public sealed class ListActionReviews(IActionReviewStore store)
{
    public Task<ActionReviewPage> ExecuteAsync(ReviewActor actor, string? cursor, int limit = PageLimit.Default,
        DraftStatusFilter filter = DraftStatusFilter.All, CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireReviewer(actor);
        if (!Enum.IsDefined(filter)) throw new ArgumentOutOfRangeException(nameof(filter));
        return store.ListAsync(ReviewValidation.ValidatePage(cursor, limit), limit, filter, cancellationToken);
    }
}
