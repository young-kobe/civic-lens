using CivicLens.Application.Paging;

namespace CivicLens.Application.Review;

public interface IReviewQueueStore
{
    Task<ReviewQueuePage> ListAsync(PageCursor? cursor, int limit, CancellationToken cancellationToken);
}
