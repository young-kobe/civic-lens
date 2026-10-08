using CivicLens.Core.Review;
using System.Collections.Immutable;

namespace CivicLens.Application.Review;

public interface IDocumentChangeReviewStore
{
    Task<DocumentChangeDraftRevision> CreateAsync(string actorSubject, DocumentChangeDraftRevision revision,
        string idempotencyKey, string payloadHash, CancellationToken cancellationToken);

    Task<DocumentChangeDraftRevision> SaveRevisionAsync(string actorSubject, DocumentChangeDraftRevision revision,
        int expectedRevisionNumber, string idempotencyKey, string payloadHash, CancellationToken cancellationToken);

    Task<ReviewDecision> DecideAsync(string actorSubject, ReviewDecision decision,
        int expectedRevisionNumber, int expectedReviewStateVersion, string idempotencyKey,
        string payloadHash, ImmutableHashSet<string> allowedOfficialIds, ImmutableHashSet<string> allowedIssueIds,
        CancellationToken cancellationToken);

    Task<DocumentChangeReview?> GetAsync(string draftId, CancellationToken cancellationToken);
    Task<EligibleDocumentComparison?> GetEligibleComparisonAsync(string comparisonId, CancellationToken cancellationToken);
    Task<DocumentChangeReviewPage> ListAsync(string? cursor, int limit, CancellationToken cancellationToken);
    Task<EligibleDocumentComparisonPage> ListEligibleComparisonsAsync(string? cursor, int limit,
        CancellationToken cancellationToken);
}
