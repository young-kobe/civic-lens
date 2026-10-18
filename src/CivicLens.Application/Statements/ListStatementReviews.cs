using CivicLens.Application.Paging;
using CivicLens.Application.Review;
using CivicLens.Core.Review;

namespace CivicLens.Application.Statements;

public sealed class ListStatementReviews(IStatementReviewStore store)
{
    public Task<StatementReviewPage> ExecuteAsync(ReviewActor actor, string? cursor, int limit = PageLimit.Default,
        DraftListFilter filter = default, CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireReviewer(actor);
        ReviewValidation.ValidateListFilter(filter);
        return store.ListAsync(ReviewValidation.ValidatePage(cursor, limit), limit, filter, cancellationToken);
    }
}
