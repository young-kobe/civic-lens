using System.Collections.Immutable;
using CivicLens.Application.Actions;
using CivicLens.Application.Paging;
using CivicLens.Core.Actions;
using CivicLens.Core.Review;

namespace CivicLens.Tests.Application.Actions;

internal sealed class FakeActionReviewStore : IActionReviewStore
{
    public Dictionary<string, PublishableAction> Publishable { get; } = [];
    public List<UnpublishedApprovedAction> Unpublished { get; } = [];
    public Dictionary<(int Congress, string BillType, int BillNumber), string> Bills { get; } = [];
    public int BillLookups { get; private set; }

    public Task<string?> FindRecordIdByBillAsync(int congress, string billType, int billNumber, CancellationToken cancellationToken)
    {
        BillLookups++;
        return Task.FromResult(Bills.GetValueOrDefault((congress, billType, billNumber)));
    }

    public int PublicationReadCount { get; private set; }
    public int UnpublishedLimit { get; private set; }

    public Task<IReadOnlyList<UnpublishedApprovedAction>> ListCodeApprovedUnpublishedAsync(int limit, IReadOnlyCollection<string> excludedRecordIds,
        CancellationToken cancellationToken)
    {
        PageLimit.Validate(limit);
        return Task.FromResult<IReadOnlyList<UnpublishedApprovedAction>>([.. Unpublished
            .Where(item => !excludedRecordIds.Contains(item.Action.RecordId))
            .Where(item => item.Action.Decisions.MaxBy(decision => decision.ReviewStateVersion) is { ActorSubject: ReviewAuthor.CodeSubject })
            .Take(limit)]);
    }

    public Task<IReadOnlyList<UnpublishedApprovedAction>> ListUnpublishedApprovedAsync(int limit, CancellationToken cancellationToken)
    {
        UnpublishedLimit = limit;
        return Task.FromResult<IReadOnlyList<UnpublishedApprovedAction>>([.. Unpublished.Take(limit)]);
    }

    public Task<IReadOnlyDictionary<string, PublishableAction>> GetForPublicationAsync(ImmutableArray<string> recordIds,
        CancellationToken cancellationToken)
    {
        PublicationReadCount++;
        IReadOnlyDictionary<string, PublishableAction> found = recordIds.Where(Publishable.ContainsKey)
            .ToDictionary(id => id, id => Publishable[id]);
        return Task.FromResult(found);
    }

    public Task<ActionDraftRevision?> GetRevisionAsync(string recordId, int revisionNumber, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<ActionDraftRevision> SaveRevisionAsync(string actorSubject, ActionDraftRevision revision, int expectedRevisionNumber,
        string idempotencyKey, string payloadHash, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<ReviewDecision> DecideAsync(string actorSubject, ReviewDecision decision, int expectedRevisionNumber,
        int expectedReviewStateVersion, string idempotencyKey, string payloadHash,
        CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<ActionReviewRead?> GetAsync(string recordId, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<ActionsNeedingAction> ListNeedingActionAsync(int limit, CancellationToken cancellationToken) => throw new NotSupportedException();
}
