using CivicLens.Application.Paging;
using CivicLens.Application.Review;
using CivicLens.Core.Review;

namespace CivicLens.Application.Statements;

public sealed class ListStatementsNotInReview(IStatementReviewStore store)
{
    public Task<StatementNotInReviewPage> ExecuteAsync(ReviewActor actor, string? cursor, int limit = PageLimit.Default,
        CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireReviewer(actor);
        return store.ListNotInReviewAsync(ReviewValidation.ValidatePage(cursor, limit), limit, cancellationToken);
    }
}
