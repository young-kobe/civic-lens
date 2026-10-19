using System.Collections.Immutable;
using CivicLens.Application.Review;
using CivicLens.Core.Review;

namespace CivicLens.Application.Votes;

public sealed class ApproveBulkSafeVotes(IVoteReviewStore store)
{
    public Task<ImmutableArray<BulkApprovalResult>> ExecuteAsync(ReviewActor actor, BulkApprovalRequest request,
        CancellationToken cancellationToken = default) =>
        BulkApproval.ExecuteAsync(actor, request, (target, decision, idempotencyKey, payloadHash) =>
            store.DecideAsync(actor.Subject, decision, target.ExpectedRevisionNumber, target.ExpectedReviewStateVersion,
                bulkSafe: true, idempotencyKey, payloadHash, cancellationToken));
}
