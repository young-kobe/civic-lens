using CivicLens.Application.Paging;

namespace CivicLens.Application.Review;

public interface IReviewQueueStore
{
    Task<ReviewQueuePage> ListAsync(PageCursor? cursor, int limit, CancellationToken cancellationToken);

    /// <summary>Finds approved records whose headline matches the words, newest revision first.</summary>
    Task<ApprovedRecordPage> SearchApprovedAsync(string text, PageCursor? cursor, int limit, CancellationToken cancellationToken);
}
