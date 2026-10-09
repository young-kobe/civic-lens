using CivicLens.Application.Paging;
using CivicLens.Core.Review;

namespace CivicLens.Application.Review;

public sealed class ListEligibleDocumentComparisons(IDocumentChangeReviewStore store, ReviewCatalog catalog)
{
    public Task<EligibleDocumentComparisonPage> ExecuteAsync(ReviewActor actor, string? cursor,
        int limit = PageLimit.Default, CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireReviewer(actor);
        _ = catalog;
        return store.ListEligibleComparisonsAsync(ReviewValidation.ValidatePage(cursor, limit), limit, cancellationToken);
    }
}
