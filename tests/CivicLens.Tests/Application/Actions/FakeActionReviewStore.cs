using System.Collections.Immutable;
using CivicLens.Application.Actions;
using CivicLens.Application.Paging;
using CivicLens.Application.Review;
using CivicLens.Core.Actions;
using CivicLens.Core.Review;

namespace CivicLens.Tests.Application.Actions;

internal sealed class FakeActionReviewStore : IActionReviewStore
{
    public Dictionary<string, PublishableAction> Publishable { get; } = [];
    public List<UnpublishedApprovedAction> Unpublished { get; } = [];
    public int PublicationReadCount { get; private set; }
    public int UnpublishedLimit { get; private set; }

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
        int expectedReviewStateVersion, bool sponsorshipOnly, string idempotencyKey, string payloadHash,
        CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<ActionReviewRead?> GetAsync(string recordId, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<ActionReviewPage> ListAsync(PageCursor? cursor, int limit, DraftListFilter filter, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<ActionReviewCounts> CountAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
}
