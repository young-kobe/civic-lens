using System.Collections.Immutable;
using CivicLens.Application.ExecutiveActions;
using CivicLens.Core.Review;

namespace CivicLens.Tests.Application.ExecutiveActions;

internal sealed class FakeExecutiveActionReviewStore : IExecutiveActionReviewStore
{
    public List<ReviewDecision> Decided { get; } = [];
    public Dictionary<string, PublishableExecutiveAction> Publishable { get; } = [];
    public List<UnpublishedApprovedExecutiveAction> Unpublished { get; } = [];

    public Task<ReviewDecision> DecideAsync(string actorSubject, ReviewDecision decision, int expectedRevisionNumber,
        int expectedReviewStateVersion, string idempotencyKey, string payloadHash, CancellationToken cancellationToken)
    {
        Decided.Add(decision);
        return Task.FromResult(decision);
    }

    public Task<ExecutiveActionReviewRead?> GetAsync(string recordId, CancellationToken cancellationToken) =>
        Task.FromResult(Publishable.GetValueOrDefault(recordId) is { } found ? new ExecutiveActionReviewRead(found.Review, found.Extractions) : null);

    public Task<ExecutiveActionsNeedingAction> ListNeedingActionAsync(int limit, CancellationToken cancellationToken) =>
        Task.FromResult(new ExecutiveActionsNeedingAction([], 0));

    public Task<IReadOnlyList<UnpublishedApprovedExecutiveAction>> ListUnpublishedApprovedAsync(int limit, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<UnpublishedApprovedExecutiveAction>>(Unpublished);

    public Task<IReadOnlyList<UnpublishedApprovedExecutiveAction>> ListCodeApprovedUnpublishedAsync(int limit,
        IReadOnlyCollection<string> excludedRecordIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<UnpublishedApprovedExecutiveAction>>(
            [.. Unpublished.Where(item => !excludedRecordIds.Contains(item.Action.RecordId))
                .Where(item => item.Action.Decisions.MaxBy(decision => decision.ReviewStateVersion) is { ActorSubject: ReviewAuthor.CodeSubject })]);

    public Task<IReadOnlyDictionary<string, PublishableExecutiveAction>> GetForPublicationAsync(ImmutableArray<string> recordIds,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<string, PublishableExecutiveAction>>(
            recordIds.Where(Publishable.ContainsKey).ToDictionary(id => id, id => Publishable[id], StringComparer.Ordinal));
}
