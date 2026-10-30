using System.Collections.Immutable;
using CivicLens.Core.Review;

namespace CivicLens.Application.ExecutiveActions;

public interface IExecutiveActionReviewStore
{
    Task<ReviewDecision> DecideAsync(string actorSubject, ReviewDecision decision, int expectedRevisionNumber,
        int expectedReviewStateVersion, string idempotencyKey, string payloadHash, CancellationToken cancellationToken);

    Task<ExecutiveActionReviewRead?> GetAsync(string recordId, CancellationToken cancellationToken);

    Task<ExecutiveActionsNeedingAction> ListNeedingActionAsync(int limit, CancellationToken cancellationToken);

    Task<IReadOnlyList<UnpublishedApprovedExecutiveAction>> ListUnpublishedApprovedAsync(int limit,
        IReadOnlyCollection<string> excludedRecordIds, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<string, PublishableExecutiveAction>> GetForPublicationAsync(ImmutableArray<string> recordIds,
        CancellationToken cancellationToken);
}
