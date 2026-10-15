using System.Collections.Immutable;
using CivicLens.Application.Review;
using CivicLens.Core.Review;

namespace CivicLens.Application.Actions;

/// <summary>Approves each record on its own, so one stale or rejected record never blocks the others.</summary>
public sealed class ApproveSponsorshipChanges(IActionReviewStore store)
{
    public const int MaximumRecords = 64;
    private const int MaximumKeyLength = ReviewValidation.MaximumIdempotencyKeyLength - 1 - 64;

    public async Task<ImmutableArray<ActionApprovalResult>> ExecuteAsync(ReviewActor actor, ApproveSponsorshipChangesRequest request,
        CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireReviewer(actor);
        ArgumentNullException.ThrowIfNull(request);
        ReviewValidation.ValidateIdempotencyKey(request.IdempotencyKey, MaximumKeyLength);
        if (request.Targets.IsDefaultOrEmpty || request.Targets.Length > MaximumRecords ||
            request.Targets.DistinctBy(target => target.RecordId, StringComparer.Ordinal).Count() != request.Targets.Length)
            throw new ArgumentException($"Choose 1 to {MaximumRecords} distinct records.", nameof(request));
        var results = ImmutableArray.CreateBuilder<ActionApprovalResult>(request.Targets.Length);
        foreach (var target in request.Targets)
            results.Add(new(target.RecordId, await ApproveAsync(actor, target, request.IdempotencyKey, cancellationToken)));
        return results.MoveToImmutable();
    }

    private async Task<ActionApprovalOutcome> ApproveAsync(ReviewActor actor, ActionApprovalTarget target, string idempotencyKey,
        CancellationToken cancellationToken)
    {
        try
        {
            var recordId = ReviewValidation.ValidateHash(target.RecordId, nameof(target.RecordId));
            var decision = ReviewValidation.CreateDecision(actor, recordId, target.ExpectedRevisionNumber,
                target.ExpectedReviewStateVersion, ReviewDecisionKind.Approve, null, []);
            await store.DecideAsync(actor.Subject, decision, target.ExpectedRevisionNumber, target.ExpectedReviewStateVersion,
                sponsorshipOnly: true, $"{idempotencyKey}:{recordId}", ReviewValidation.HashPayload(target), cancellationToken);
            return ActionApprovalOutcome.Approved;
        }
        catch (ReviewConflictException) { return ActionApprovalOutcome.Conflict; }
        catch (KeyNotFoundException) { return ActionApprovalOutcome.NotFound; }
        catch (ArgumentException) { return ActionApprovalOutcome.Rejected; }
    }
}
