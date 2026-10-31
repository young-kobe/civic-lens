using CivicLens.Application.Paging;

namespace CivicLens.Application.Review;

public interface IReviewQueueStore
{
    Task<ReviewQueuePage> ListAsync(PageCursor? cursor, int limit, CancellationToken cancellationToken);

    /// <summary>Finds approved links that no longer match, newest revision first, without the excluded records. Total counts all of them.</summary>
    Task<ReviewQueuePage> ListUnmatchedApprovedLinksAsync(int limit, IReadOnlyCollection<string> excludedRecordIds,
        CancellationToken cancellationToken);

    /// <summary>Finds approved records whose headline matches the words, newest revision first.</summary>
    Task<ApprovedRecordPage> SearchApprovedAsync(string text, PageCursor? cursor, int limit, CancellationToken cancellationToken);
}
