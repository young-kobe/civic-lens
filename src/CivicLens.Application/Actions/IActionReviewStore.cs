using CivicLens.Application.Paging;
using CivicLens.Application.Review;
using CivicLens.Core.Actions;
using CivicLens.Core.Review;

namespace CivicLens.Application.Actions;

public interface IActionReviewStore
{
    Task<ActionDraftRevision?> GetRevisionAsync(string recordId, int revisionNumber, CancellationToken cancellationToken);

    Task<ActionDraftRevision> SaveRevisionAsync(string actorSubject, ActionDraftRevision revision, int expectedRevisionNumber,
        string idempotencyKey, string payloadHash, CancellationToken cancellationToken);

    Task<ReviewDecision> DecideAsync(string actorSubject, ReviewDecision decision, int expectedRevisionNumber,
        int expectedReviewStateVersion, bool sponsorshipOnly, string idempotencyKey, string payloadHash, CancellationToken cancellationToken);

    Task<ActionReviewRead?> GetAsync(string recordId, CancellationToken cancellationToken);

    Task<ActionReviewPage> ListAsync(PageCursor? cursor, int limit, DraftStatusFilter filter, CancellationToken cancellationToken);

    Task<ActionReviewCounts> CountAsync(CancellationToken cancellationToken);
}
