using System.Collections.Immutable;
using CivicLens.Application.Publication;
using CivicLens.Core.Review;
using CivicLens.Core.Votes;

namespace CivicLens.Application.Votes;

public interface IVoteReviewStore
{
    Task<VoteDraftRevision?> GetRevisionAsync(string recordId, int revisionNumber, CancellationToken cancellationToken);

    Task<VoteDraftRevision> SaveRevisionAsync(string actorSubject, VoteDraftRevision revision, int expectedRevisionNumber,
        string idempotencyKey, string payloadHash, CancellationToken cancellationToken);

    Task<ReviewDecision> DecideAsync(string actorSubject, ReviewDecision decision, int expectedRevisionNumber,
        int expectedReviewStateVersion, string idempotencyKey, string payloadHash, CancellationToken cancellationToken);

    Task<VoteReviewRead?> GetAsync(string recordId, CancellationToken cancellationToken);


    Task<IReadOnlyList<UnpublishedApprovedVote>> ListUnpublishedApprovedAsync(int limit, IReadOnlyCollection<string> excludedRecordIds,
        CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<string, PublishableVote>> GetForPublicationAsync(ImmutableArray<string> recordIds,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<VoteMembership>> ReadMembersAsync(ImmutableArray<PublishedRevisionBinding> revisions,
        CancellationToken cancellationToken);

    Task<string?> FindRecordIdByRollAsync(Chamber chamber, int congress, int sessionNumber, int rollCallNumber,
        CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<string, ImmutableArray<string>>> FindBillActionRecordIdsAsync(ImmutableArray<VoteBill> bills,
        CancellationToken cancellationToken);

    /// <summary>The inverse of <see cref="FindBillActionRecordIdsAsync"/>: the vote records on the bill of each named action record.</summary>
    Task<IReadOnlySet<string>> FindVoteRecordIdsOnBillsOfActionsAsync(ImmutableArray<string> actionRecordIds,
        CancellationToken cancellationToken);
}
