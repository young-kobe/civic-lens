using CivicLens.Core.Review;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Review;

public sealed class DecideDocumentChangeReview(IDocumentChangeReviewStore store, ReviewCatalog catalog)
{
    public Task<ReviewDecision> ExecuteAsync(ReviewActor actor,
        DecideDocumentChangeReviewRequest request, CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireReviewer(actor);
        ArgumentNullException.ThrowIfNull(request);
        ReviewValidation.ValidateIdempotencyKey(request.IdempotencyKey);
        var draftId = ReviewValidation.ValidateDraftId(request.DraftId);
        ReviewReasons.Require(PublishedRecordKind.DocumentChange, request.ReasonCode);
        var decision = ReviewValidation.CreateDecision(actor, draftId, request.ExpectedRevisionNumber,
            request.ExpectedReviewStateVersion, request.Kind, request.Note, request.ResolvedDecisionIds, request.CorrectionNote, request.ReasonCode);
        return store.DecideAsync(actor.Subject, decision, request.ExpectedRevisionNumber,
            request.ExpectedReviewStateVersion, request.IdempotencyKey, ReviewValidation.HashPayload(request),
            catalog.OfficialIds, catalog.IssueIds, cancellationToken);
    }
}
