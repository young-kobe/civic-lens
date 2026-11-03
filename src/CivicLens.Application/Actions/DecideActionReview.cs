using CivicLens.Application.Review;
using CivicLens.Core.Review;

namespace CivicLens.Application.Actions;

public sealed class DecideActionReview(IActionReviewStore store)
{
    public Task<ReviewDecision> ExecuteAsync(ReviewActor actor, DecideActionReviewRequest request,
        CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireReviewer(actor);
        ArgumentNullException.ThrowIfNull(request);
        ReviewValidation.ValidateIdempotencyKey(request.IdempotencyKey);
        var recordId = ReviewValidation.ValidateHash(request.RecordId, nameof(request.RecordId));
        var decision = ReviewValidation.CreateDecision(actor, recordId, request.ExpectedRevisionNumber,
            request.ExpectedReviewStateVersion, request.Kind, request.Note, request.ResolvedDecisionIds, request.CorrectionNote);
        return store.DecideAsync(actor.Subject, decision, request.ExpectedRevisionNumber, request.ExpectedReviewStateVersion,
            request.IdempotencyKey, ReviewValidation.HashPayload(request), cancellationToken);
    }
}
