using CivicLens.Application.Paging;
using CivicLens.Core.Review;

namespace CivicLens.Application.Review;

public sealed class ListDocumentChangeReviews(IDocumentChangeReviewStore store, ReviewCatalog catalog)
{
    public Task<DocumentChangeReviewPage> ExecuteAsync(ReviewActor actor, string? cursor,
        int limit = PageLimit.Default, DraftStatusFilter filter = DraftStatusFilter.All,
        CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireReviewer(actor);
        _ = catalog;
        if (!Enum.IsDefined(filter)) throw new ArgumentOutOfRangeException(nameof(filter));
        return store.ListAsync(ReviewValidation.ValidatePage(cursor, limit), limit, filter, cancellationToken);
    }
}
