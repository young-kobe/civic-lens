using CivicLens.Application.Review;
using CivicLens.Core.Review;

namespace CivicLens.Application.Briefs;

public sealed class DecideBriefReview(IBriefReviewStore store)
{
    public Task<ReviewDecision> ExecuteAsync(ReviewActor actor, DecideBriefReviewRequest request,
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
