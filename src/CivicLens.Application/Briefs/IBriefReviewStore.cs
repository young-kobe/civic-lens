using CivicLens.Application.Paging;
using CivicLens.Application.Review;
using CivicLens.Core.Briefs;
using CivicLens.Core.Review;

namespace CivicLens.Application.Briefs;

public interface IBriefReviewStore
{
    Task<BriefRevision?> GetRevisionAsync(string recordId, int revisionNumber, CancellationToken cancellationToken);

    Task<BriefRevision> SaveRevisionAsync(string actorSubject, BriefRevision revision, int expectedRevisionNumber,
        string idempotencyKey, string payloadHash, CancellationToken cancellationToken);

    Task<ReviewDecision> DecideAsync(string actorSubject, ReviewDecision decision, int expectedRevisionNumber,
        int expectedReviewStateVersion, string idempotencyKey, string payloadHash, CancellationToken cancellationToken);

    Task<BriefReviewRead?> GetAsync(string recordId, CancellationToken cancellationToken);

    Task<BriefReviewPage> ListAsync(PageCursor? cursor, int limit, DraftStatusFilter filter, CancellationToken cancellationToken);

    Task<BriefReviewCounts> CountAsync(CancellationToken cancellationToken);
}
