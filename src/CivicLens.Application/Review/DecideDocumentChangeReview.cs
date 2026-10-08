using CivicLens.Core.Review;

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
        if (request.ExpectedRevisionNumber < 1) throw new ArgumentOutOfRangeException(nameof(request.ExpectedRevisionNumber));
        if (request.ExpectedReviewStateVersion < 0) throw new ArgumentOutOfRangeException(nameof(request.ExpectedReviewStateVersion));
        if (!Enum.IsDefined(request.Kind)) throw new ArgumentOutOfRangeException(nameof(request.Kind));
        var resolutions = ReviewValidation.ValidateDecisionIds(request.ResolvedDecisionIds);
        ReviewValidation.ValidateText(request.Note, 4_000, nameof(request.Note), allowEmpty: true);
        var decision = new ReviewDecision(Guid.NewGuid().ToString("N"), draftId, request.ExpectedRevisionNumber,
            request.ExpectedReviewStateVersion + 1, request.Kind, actor.Subject, request.Note,
            resolutions, DateTimeOffset.UtcNow);
        return store.DecideAsync(actor.Subject, decision, request.ExpectedRevisionNumber,
            request.ExpectedReviewStateVersion, request.IdempotencyKey, ReviewValidation.HashPayload(request),
            catalog.OfficialIds, catalog.IssueIds, cancellationToken);
    }
}
