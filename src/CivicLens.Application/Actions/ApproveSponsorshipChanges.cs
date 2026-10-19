using System.Collections.Immutable;
using CivicLens.Application.Review;
using CivicLens.Core.Review;

namespace CivicLens.Application.Actions;

public sealed class ApproveSponsorshipChanges(IActionReviewStore store)
{
    public Task<ImmutableArray<BulkApprovalResult>> ExecuteAsync(ReviewActor actor, BulkApprovalRequest request,
        CancellationToken cancellationToken = default) =>
        BulkApproval.ExecuteAsync(actor, request, (target, decision, idempotencyKey, payloadHash) =>
            store.DecideAsync(actor.Subject, decision, target.ExpectedRevisionNumber, target.ExpectedReviewStateVersion,
                sponsorshipOnly: true, idempotencyKey, payloadHash, cancellationToken));
}
