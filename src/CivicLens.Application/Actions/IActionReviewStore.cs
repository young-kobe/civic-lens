using System.Collections.Immutable;
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
        int expectedReviewStateVersion, string idempotencyKey, string payloadHash, CancellationToken cancellationToken);

    Task<string?> FindRecordIdByBillAsync(int congress, string billType, int billNumber, CancellationToken cancellationToken);

    Task<ActionReviewRead?> GetAsync(string recordId, CancellationToken cancellationToken);

    Task<ActionReviewPage> ListAsync(PageCursor? cursor, int limit, DraftListFilter filter, CancellationToken cancellationToken);

    Task<ActionReviewCounts> CountAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<UnpublishedApprovedAction>> ListUnpublishedApprovedAsync(int limit, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<string, PublishableAction>> GetForPublicationAsync(ImmutableArray<string> recordIds,
        CancellationToken cancellationToken);
}
