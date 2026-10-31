using CivicLens.Application.Paging;
using CivicLens.Core.Review;
using Microsoft.Extensions.Logging;

namespace CivicLens.Application.Review;

public sealed class ListUnmatchedApprovedLinks(IReviewQueueStore store, ILogger<ListUnmatchedApprovedLinks> logger)
{
    public async Task<ReviewQueuePage> ExecuteAsync(ReviewActor actor, IReadOnlyCollection<string> excludedRecordIds,
        int limit = PageLimit.Default, CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireOwner(actor);
        PageLimit.Validate(limit);
        var page = await store.ListUnmatchedApprovedLinksAsync(limit, excludedRecordIds, cancellationToken);
        foreach (var item in page.Items.Where(item => item.IsUnreadable))
            logger.LogWarning(item.UnreadableCause, "The review queue could not read the {Kind} record {RecordId}", item.Kind, item.RecordId);
        return page;
    }
}
