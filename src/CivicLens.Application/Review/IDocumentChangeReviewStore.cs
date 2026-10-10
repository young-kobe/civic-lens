using CivicLens.Application.Paging;
using CivicLens.Core.Review;
using System.Collections.Immutable;

namespace CivicLens.Application.Review;

public interface IDocumentChangeReviewStore
{
    Task<DocumentChangeDraftRevision> CreateAsync(string actorSubject, DocumentChangeDraftRevision revision,
        string idempotencyKey, string payloadHash, CancellationToken cancellationToken);

    Task<DocumentChangeDraftRevision> SaveRevisionAsync(string actorSubject, DocumentChangeDraftRevision revision,
        int expectedRevisionNumber, bool changeDateChecked, string idempotencyKey, string payloadHash, CancellationToken cancellationToken);

    Task<ReviewDecision> DecideAsync(string actorSubject, ReviewDecision decision,
        int expectedRevisionNumber, int expectedReviewStateVersion, string idempotencyKey,
        string payloadHash, ImmutableHashSet<string> allowedOfficialIds, ImmutableHashSet<string> allowedIssueIds,
        CancellationToken cancellationToken);

    Task<DocumentChangeReview?> GetAsync(string draftId, CancellationToken cancellationToken);
    Task<IReadOnlyDictionary<string, PublishableDocumentChange>> GetForPublicationAsync(
        IReadOnlyCollection<string> draftIds, CancellationToken cancellationToken);
    Task<DocumentChangeReviewPage> ListAsync(PageCursor? cursor, int limit, DraftStatusFilter filter,
        CancellationToken cancellationToken);
    Task<EligibleDocumentComparisonPage> ListEligibleComparisonsAsync(PageCursor? cursor, int limit,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<UnpublishedApprovedDraft>> ListUnpublishedApprovedDraftsAsync(int limit,
        CancellationToken cancellationToken);
    Task<ReviewOverview> GetOverviewAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<ReviewActivityEvent>> ListRecentActivityAsync(int limit, CancellationToken cancellationToken);
}
