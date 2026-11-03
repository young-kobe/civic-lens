using CivicLens.Application.Paging;
using CivicLens.Core.Review;
using Microsoft.Extensions.Logging;

namespace CivicLens.Application.Review;

public sealed class ListReviewQueue(IReviewQueueStore store, ILogger<ListReviewQueue> logger)
{
    public Task<ReviewQueuePage> ExecuteAsync(ReviewActor actor, string? cursor, int limit = PageLimit.Default,
        CancellationToken cancellationToken = default) => ReadAsync(actor, cursor, limit, store.ListAsync, cancellationToken);

    public Task<ReviewQueuePage> ExecuteRejectedAsync(ReviewActor actor, string? cursor, int limit = PageLimit.Default,
        CancellationToken cancellationToken = default) => ReadAsync(actor, cursor, limit, store.ListRejectedAsync, cancellationToken);

    private async Task<ReviewQueuePage> ReadAsync(ReviewActor actor, string? cursor, int limit,
        Func<PageCursor?, int, CancellationToken, Task<ReviewQueuePage>> list, CancellationToken cancellationToken)
    {
        ReviewAuthorization.RequireReviewer(actor);
        var page = await list(ReviewValidation.ValidatePage(cursor, limit), limit, cancellationToken);
        foreach (var item in page.Items.Where(item => item.IsUnreadable))
            logger.LogWarning(item.UnreadableCause, "The review queue could not read the {Kind} record {RecordId}", item.Kind, item.RecordId);
        return page;
    }
}
