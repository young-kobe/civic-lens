using System.Collections.Immutable;
using CivicLens.Application.Paging;
using CivicLens.Application.Publication;
using CivicLens.Application.Votes;
using CivicLens.Core.Review;
using CivicLens.Core.Votes;

namespace CivicLens.Tests.Application.Votes;

internal sealed class FakeVoteReviewStore : IVoteReviewStore
{
    public List<ReviewDecision> Decided { get; } = [];
    public Dictionary<string, PublishableVote> Publishable { get; } = [];
    public List<UnpublishedApprovedVote> Unpublished { get; } = [];
    public Dictionary<(int Congress, string BillType, int BillNumber), ImmutableArray<string>> BillActions { get; } = [];
    public Dictionary<(Chamber Chamber, int Congress, int Session, int Roll), string> Rolls { get; } = [];
    public int BillLookups { get; private set; }

    public int RollLookups { get; private set; }

    public Task<string?> FindRecordIdByRollAsync(Chamber chamber, int congress, int sessionNumber, int rollCallNumber,
        CancellationToken cancellationToken)
    {
        RollLookups++;
        return Task.FromResult(Rolls.GetValueOrDefault((chamber, congress, sessionNumber, rollCallNumber)));
    }

    public Task<IReadOnlyList<UnpublishedApprovedVote>> ListCodeApprovedUnpublishedAsync(int limit, IReadOnlyCollection<string> excludedRecordIds,
        CancellationToken cancellationToken)
    {
        PageLimit.Validate(limit);
        return Task.FromResult<IReadOnlyList<UnpublishedApprovedVote>>([.. Unpublished
            .Where(item => !excludedRecordIds.Contains(item.Vote.RecordId))
            .Where(item => item.Vote.Decisions.MaxBy(decision => decision.ReviewStateVersion) is { ActorSubject: ReviewAuthor.CodeSubject })
            .Take(limit)]);
    }

    public Task<IReadOnlyList<UnpublishedApprovedVote>> ListUnpublishedApprovedAsync(int limit, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<UnpublishedApprovedVote>>([.. Unpublished.Take(limit)]);

    public Task<IReadOnlyDictionary<string, PublishableVote>> GetForPublicationAsync(ImmutableArray<string> recordIds,
        CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<string, PublishableVote> found = recordIds.Where(Publishable.ContainsKey)
            .ToDictionary(id => id, id => Publishable[id]);
        return Task.FromResult(found);
    }

    public Task<IReadOnlyList<VoteMembership>> ReadMembersAsync(ImmutableArray<PublishedRevisionBinding> revisions,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<VoteMembership> members = [.. revisions.SelectMany(item => Publishable[item.RecordId].Review.Revisions
            .Single(revision => revision.RevisionNumber == item.RevisionNumber).OfficialIds()
            .Select(personId => new VoteMembership(item.RecordId, personId)))];
        return Task.FromResult(members);
    }

    public Task<IReadOnlyDictionary<string, ImmutableArray<string>>> FindBillActionRecordIdsAsync(ImmutableArray<VoteBill> bills,
        CancellationToken cancellationToken)
    {
        BillLookups++;
        IReadOnlyDictionary<string, ImmutableArray<string>> found = bills.ToDictionary(bill => bill.VoteRecordId,
            bill => BillActions.GetValueOrDefault((bill.Congress, bill.BillType, bill.BillNumber), []));
        return Task.FromResult(found);
    }

    public Task<IReadOnlySet<string>> FindVoteRecordIdsOnBillsOfActionsAsync(ImmutableArray<string> actionRecordIds,
        CancellationToken cancellationToken)
    {
        IReadOnlySet<string> found = Publishable
            .Where(vote => VoteBill.Of(vote.Value.Review.CurrentRevision) is { } bill &&
                BillActions.GetValueOrDefault((bill.Congress, bill.BillType, bill.BillNumber), []).Any(actionRecordIds.Contains))
            .Select(vote => vote.Key).ToHashSet(StringComparer.Ordinal);
        return Task.FromResult(found);
    }

    public Task<VoteDraftRevision?> GetRevisionAsync(string recordId, int revisionNumber, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<VoteDraftRevision> SaveRevisionAsync(string actorSubject, VoteDraftRevision revision, int expectedRevisionNumber,
        string idempotencyKey, string payloadHash, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<ReviewDecision> DecideAsync(string actorSubject, ReviewDecision decision, int expectedRevisionNumber,
        int expectedReviewStateVersion, string idempotencyKey, string payloadHash, CancellationToken cancellationToken)
    {
        Decided.Add(decision);
        return Task.FromResult(decision);
    }

    public Task<VoteReviewRead?> GetAsync(string recordId, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<VotesNeedingAction> ListNeedingActionAsync(int limit, CancellationToken cancellationToken) => throw new NotSupportedException();
}
