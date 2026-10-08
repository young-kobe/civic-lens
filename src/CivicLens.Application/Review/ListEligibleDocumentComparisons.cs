using CivicLens.Core.Review;

namespace CivicLens.Application.Review;

public sealed class ListEligibleDocumentComparisons(IDocumentChangeReviewStore store, ReviewCatalog catalog)
{
    public Task<EligibleDocumentComparisonPage> ExecuteAsync(ReviewActor actor, string? cursor, int limit,
        CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireReviewer(actor);
        _ = catalog;
        ReviewValidation.ValidatePage(cursor, limit);
        return store.ListEligibleComparisonsAsync(cursor, limit, cancellationToken);
    }
}
