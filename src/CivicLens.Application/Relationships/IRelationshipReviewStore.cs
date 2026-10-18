using System.Collections.Immutable;
using CivicLens.Application.Paging;
using CivicLens.Application.Publication;
using CivicLens.Application.Review;
using CivicLens.Core.Relationships;
using CivicLens.Core.Review;

namespace CivicLens.Application.Relationships;

public interface IRelationshipReviewStore
{
    Task<RelationshipRevision?> GetRevisionAsync(string recordId, int revisionNumber, CancellationToken cancellationToken);

    Task<RelationshipRevision> SaveRevisionAsync(string actorSubject, RelationshipRevision revision, int expectedRevisionNumber,
        string idempotencyKey, string payloadHash, CancellationToken cancellationToken);

    Task<ReviewDecision> DecideAsync(string actorSubject, ReviewDecision decision, int expectedRevisionNumber,
        int expectedReviewStateVersion, string idempotencyKey, string payloadHash, CancellationToken cancellationToken);

    Task<RelationshipReviewRead?> GetAsync(string recordId, CancellationToken cancellationToken);

    Task<RelationshipReviewPage> ListAsync(PageCursor? cursor, int limit, DraftStatusFilter filter, CancellationToken cancellationToken);

    Task<RelationshipReviewCounts> CountAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<UnpublishedApprovedRelationship>> ListUnpublishedApprovedAsync(int limit, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<string, PublishableRelationship>> GetForPublicationAsync(ImmutableArray<string> recordIds,
        ImmutableArray<PublishedRevisionBinding> releaseRecords, CancellationToken cancellationToken);
}
