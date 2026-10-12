using CivicLens.Application.Paging;
using CivicLens.Application.Review;
using CivicLens.Core.Review;

namespace CivicLens.Application.Statements;

public sealed class ListStatementReviews(IStatementReviewStore store)
{
    public Task<StatementReviewPage> ExecuteAsync(ReviewActor actor, string? cursor, int limit = PageLimit.Default,
        DraftStatusFilter filter = DraftStatusFilter.All, CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireReviewer(actor);
        if (!Enum.IsDefined(filter)) throw new ArgumentOutOfRangeException(nameof(filter));
        return store.ListAsync(ReviewValidation.ValidatePage(cursor, limit), limit, filter, cancellationToken);
    }
}
