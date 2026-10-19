using System.Collections.Immutable;
using CivicLens.Core.Review;

namespace CivicLens.Application.Review;

/// <summary>Approves each target on its own, so one stale or rejected record never blocks the others.</summary>
public static class BulkApproval
{
    public const int MaximumRecords = 64;
    private const int MaximumKeyLength = ReviewValidation.MaximumIdempotencyKeyLength - 1 - 64;

    internal delegate Task DecideTarget(BulkApprovalTarget target, ReviewDecision decision, string idempotencyKey, string payloadHash);

    internal static async Task<ImmutableArray<BulkApprovalResult>> ExecuteAsync(ReviewActor actor, BulkApprovalRequest request,
        DecideTarget decide)
    {
        ReviewAuthorization.RequireReviewer(actor);
        ArgumentNullException.ThrowIfNull(request);
        ReviewValidation.ValidateIdempotencyKey(request.IdempotencyKey, MaximumKeyLength);
        if (request.Targets.IsDefaultOrEmpty || request.Targets.Length > MaximumRecords ||
            request.Targets.DistinctBy(target => target.RecordId, StringComparer.Ordinal).Count() != request.Targets.Length)
            throw new ArgumentException($"Choose 1 to {MaximumRecords} distinct records.", nameof(request));
        var results = ImmutableArray.CreateBuilder<BulkApprovalResult>(request.Targets.Length);
        foreach (var target in request.Targets)
            results.Add(new(target.RecordId, await ApproveAsync(actor, target, request.IdempotencyKey, decide)));
        return results.MoveToImmutable();
    }

    private static async Task<BulkApprovalOutcome> ApproveAsync(ReviewActor actor, BulkApprovalTarget target, string idempotencyKey,
        DecideTarget decide)
    {
        try
        {
            var recordId = ReviewValidation.ValidateHash(target.RecordId, nameof(target.RecordId));
            var decision = ReviewValidation.CreateDecision(actor, recordId, target.ExpectedRevisionNumber,
                target.ExpectedReviewStateVersion, ReviewDecisionKind.Approve, null, []);
            await decide(target, decision, $"{idempotencyKey}:{recordId}", ReviewValidation.HashPayload(target));
            return BulkApprovalOutcome.Approved;
        }
        catch (ReviewConflictException) { return BulkApprovalOutcome.Conflict; }
        catch (KeyNotFoundException) { return BulkApprovalOutcome.NotFound; }
        catch (ArgumentException) { return BulkApprovalOutcome.Rejected; }
    }
}
