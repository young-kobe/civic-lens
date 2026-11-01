using System.Collections.Immutable;
using CivicLens.Application.Publication;
using CivicLens.Application.Review;
using CivicLens.Core.Briefs;
using CivicLens.Core.Review;

namespace CivicLens.Application.Briefs;

public interface IBriefReviewStore
{
    Task<BriefRevision?> GetRevisionAsync(string recordId, int revisionNumber, CancellationToken cancellationToken);

    Task<BriefRevision> SaveRevisionAsync(string actorSubject, BriefRevision revision, int expectedRevisionNumber,
        ReviewDecision? approval, string idempotencyKey, string payloadHash, CancellationToken cancellationToken);

    Task<ReviewDecision> DecideAsync(string actorSubject, ReviewDecision decision, int expectedRevisionNumber,
        int expectedReviewStateVersion, string idempotencyKey, string payloadHash, CancellationToken cancellationToken);

    Task<BriefReviewRead?> GetAsync(string recordId, CancellationToken cancellationToken);

    Task<IReadOnlyList<UnpublishedApprovedBrief>> ListUnpublishedApprovedAsync(int limit, IReadOnlyCollection<string> excludedRecordIds,
        CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<string, PublishableBrief>> GetForPublicationAsync(ImmutableArray<string> recordIds,
        ImmutableArray<PublishedRevisionBinding> releaseRecords, CancellationToken cancellationToken);
}
