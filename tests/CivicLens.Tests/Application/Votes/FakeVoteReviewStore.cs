using System.Collections.Immutable;
using CivicLens.Application.Paging;
using CivicLens.Application.Review;
using CivicLens.Application.Votes;
using CivicLens.Core.Review;
using CivicLens.Core.Votes;

namespace CivicLens.Tests.Application.Votes;

internal sealed class FakeVoteReviewStore : IVoteReviewStore
{
    public Dictionary<string, PublishableVote> Publishable { get; } = [];
    public List<UnpublishedApprovedVote> Unpublished { get; } = [];
    public Dictionary<(int Congress, string BillType, int BillNumber), ImmutableArray<string>> BillActions { get; } = [];
    public int BillLookups { get; private set; }

    public Task<IReadOnlyList<UnpublishedApprovedVote>> ListUnpublishedApprovedAsync(int limit, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<UnpublishedApprovedVote>>([.. Unpublished.Take(limit)]);

    public Task<IReadOnlyDictionary<string, PublishableVote>> GetForPublicationAsync(ImmutableArray<string> recordIds,
        CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<string, PublishableVote> found = recordIds.Where(Publishable.ContainsKey)
            .ToDictionary(id => id, id => Publishable[id]);
        return Task.FromResult(found);
    }

    public Task<IReadOnlyDictionary<string, ImmutableArray<string>>> FindBillActionRecordIdsAsync(ImmutableArray<VoteBill> bills,
        CancellationToken cancellationToken)
    {
        BillLookups++;
        IReadOnlyDictionary<string, ImmutableArray<string>> found = bills.ToDictionary(bill => bill.VoteRecordId,
            bill => BillActions.GetValueOrDefault((bill.Congress, bill.BillType, bill.BillNumber), []));
        return Task.FromResult(found);
    }

    public Task<VoteDraftRevision?> GetRevisionAsync(string recordId, int revisionNumber, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<VoteDraftRevision> SaveRevisionAsync(string actorSubject, VoteDraftRevision revision, int expectedRevisionNumber,
        string idempotencyKey, string payloadHash, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<ReviewDecision> DecideAsync(string actorSubject, ReviewDecision decision, int expectedRevisionNumber,
        int expectedReviewStateVersion, string idempotencyKey, string payloadHash, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<VoteReviewRead?> GetAsync(string recordId, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<VoteReviewPage> ListAsync(PageCursor? cursor, int limit, DraftListFilter filter, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<VoteReviewCounts> CountAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
}
