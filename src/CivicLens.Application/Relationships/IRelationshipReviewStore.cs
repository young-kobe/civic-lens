using System.Collections.Immutable;
using CivicLens.Application.Publication;
using CivicLens.Application.Review;
using CivicLens.Core.Relationships;
using CivicLens.Core.Review;

namespace CivicLens.Application.Relationships;

public interface IRelationshipReviewStore
{
    Task<RelationshipRevision?> GetRevisionAsync(string recordId, int revisionNumber, CancellationToken cancellationToken);

    Task<RelationshipRevision> SaveRevisionAsync(string actorSubject, RelationshipRevision revision, int expectedRevisionNumber,
        ReviewDecision? approval, string idempotencyKey, string payloadHash, CancellationToken cancellationToken);

    Task<ReviewDecision> DecideAsync(string actorSubject, ReviewDecision decision, int expectedRevisionNumber,
        int expectedReviewStateVersion, string idempotencyKey, string payloadHash, CancellationToken cancellationToken);

    Task<RelationshipReviewRead?> GetAsync(string recordId, CancellationToken cancellationToken);

    Task<IReadOnlyList<UnpublishedApprovedRelationship>> ListUnpublishedApprovedAsync(int limit, IReadOnlyCollection<string> excludedRecordIds,
        CancellationToken cancellationToken);

    Task<IReadOnlySet<string>> FindCitingAsync(ImmutableArray<string> relationshipIds, ImmutableArray<PublishedRecordReference> cited,
        CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<string, PublishableRelationship>> GetForPublicationAsync(ImmutableArray<string> recordIds,
        ImmutableArray<PublishedRevisionBinding> releaseRecords, CancellationToken cancellationToken);
}
