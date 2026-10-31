using System.Collections.Immutable;
using CivicLens.Core.Actions;
using CivicLens.Core.Review;
using CivicLens.Publication.Contracts;

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


    Task<IReadOnlyList<UnpublishedApprovedAction>> ListUnpublishedApprovedAsync(int limit, IReadOnlyCollection<string> excludedRecordIds,
        CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<string, PublishableAction>> GetForPublicationAsync(ImmutableArray<string> recordIds,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<PublishedBillStatus>> ReadBillStatusesAsync(ImmutableArray<string> recordIds, CancellationToken cancellationToken);
}
