using CivicLens.Application.Paging;
using CivicLens.Core.Review;

namespace CivicLens.Application.Review;

public sealed class SearchApprovedRecords(IReviewQueueStore store)
{
    public const int MaximumQueryLength = 300;

    public Task<ApprovedRecordPage> ExecuteAsync(ReviewActor actor, string? query, string? cursor, int limit = PageLimit.Default,
        CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireReviewer(actor);
        var text = query?.Trim() ?? "";
        if (text.Length is 0 or > MaximumQueryLength)
            throw new ArgumentException($"Search text must contain 1 to {MaximumQueryLength} characters.", nameof(query));
        return store.SearchApprovedAsync(text, ReviewValidation.ValidatePage(cursor, limit), limit, cancellationToken);
    }
}
