using CivicLens.Core.Review;

namespace CivicLens.Application.Review;

public sealed class ListDocumentChangeReviews(IDocumentChangeReviewStore store, ReviewCatalog catalog)
{
    public Task<DocumentChangeReviewPage> ExecuteAsync(ReviewActor actor, string? cursor, int limit,
        CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireReviewer(actor);
        _ = catalog;
        ReviewValidation.ValidatePage(cursor, limit);
        return store.ListAsync(cursor, limit, cancellationToken);
    }
}
