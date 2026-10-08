using CivicLens.Core.Review;

namespace CivicLens.Application.Review;

public sealed class CreateDocumentChangeDraft(IDocumentChangeReviewStore store)
{
    public async Task<DocumentChangeDraftRevision> ExecuteAsync(ReviewActor actor,
        CreateDocumentChangeDraftRequest request, CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireReviewer(actor);
        ArgumentNullException.ThrowIfNull(request);
        ReviewValidation.ValidateIdempotencyKey(request.IdempotencyKey);
        var comparisonId = ReviewValidation.ValidateHash(request.ComparisonId, nameof(request.ComparisonId));
        var draftId = Guid.NewGuid().ToString("N");
        var revision = new DocumentChangeDraftRevision(draftId, 1, comparisonId, actor.Subject,
            DateTimeOffset.UtcNow, "", "", null, null, "", null, null,
            System.Collections.Immutable.ImmutableArray<string>.Empty,
            System.Collections.Immutable.ImmutableArray<string>.Empty,
            System.Collections.Immutable.ImmutableArray<DocumentChangeCitation>.Empty);
        return await store.CreateAsync(actor.Subject, revision, request.IdempotencyKey,
            ReviewValidation.HashPayload(request), cancellationToken);
    }
}
