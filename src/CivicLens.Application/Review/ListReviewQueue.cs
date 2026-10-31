using CivicLens.Application.Paging;
using CivicLens.Core.Review;
using Microsoft.Extensions.Logging;

namespace CivicLens.Application.Review;

public sealed class ListReviewQueue(IReviewQueueStore store, ILogger<ListReviewQueue> logger)
{
    public async Task<ReviewQueuePage> ExecuteAsync(ReviewActor actor, string? cursor, int limit = PageLimit.Default,
        CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireReviewer(actor);
        var page = await store.ListAsync(ReviewValidation.ValidatePage(cursor, limit), limit, cancellationToken);
        foreach (var item in page.Items.Where(item => item.IsUnreadable))
            logger.LogWarning(item.UnreadableCause, "The review queue could not read the {Kind} record {RecordId}", item.Kind, item.RecordId);
        return page;
    }
}
