using CivicLens.Application.Review;
using CivicLens.Core.Review;

namespace CivicLens.Application.Statements;

public sealed class GetStatementReviewCounts(IStatementReviewStore store)
{
    public Task<StatementReviewCounts> ExecuteAsync(ReviewActor actor, CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireReviewer(actor);
        return store.CountAsync(cancellationToken);
    }
}
