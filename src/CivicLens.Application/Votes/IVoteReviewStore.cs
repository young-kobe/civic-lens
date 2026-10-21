using System.Collections.Immutable;
using CivicLens.Application.Paging;
using CivicLens.Application.Publication;
using CivicLens.Application.Review;
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

    Task<VoteReviewPage> ListAsync(PageCursor? cursor, int limit, DraftListFilter filter, CancellationToken cancellationToken);

    Task<VoteReviewCounts> CountAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<UnpublishedApprovedVote>> ListUnpublishedApprovedAsync(int limit, CancellationToken cancellationToken);

    Task<IReadOnlyList<UnpublishedApprovedVote>> ListCodeApprovedUnpublishedAsync(int limit, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<string, PublishableVote>> GetForPublicationAsync(ImmutableArray<string> recordIds,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<VoteMembership>> ReadMembersAsync(ImmutableArray<PublishedRevisionBinding> revisions,
        CancellationToken cancellationToken);

    Task<string?> FindRecordIdByRollAsync(int congress, int sessionNumber, int rollCallNumber, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<string, ImmutableArray<string>>> FindBillActionRecordIdsAsync(ImmutableArray<VoteBill> bills,
        CancellationToken cancellationToken);
}
