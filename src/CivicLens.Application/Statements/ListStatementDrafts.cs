using CivicLens.Application.Review;
using CivicLens.Core.Review;

namespace CivicLens.Application.Statements;

public sealed class ListStatementDrafts(IStatementDraftStore store)
{
    public const int MaximumLimit = 500;

    public Task<IReadOnlyList<StatementDraftListItem>> ExecuteAsync(ReviewActor actor, StatementDraftView? view, int limit,
        CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireOwner(actor);
        if (limit is < 1 or > MaximumLimit) throw new ArgumentOutOfRangeException(nameof(limit));
        return store.ListAsync(view, limit, cancellationToken);
    }
}
