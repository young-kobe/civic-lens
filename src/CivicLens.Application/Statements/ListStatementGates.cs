using CivicLens.Application.Review;
using CivicLens.Core.Review;

namespace CivicLens.Application.Statements;

public sealed class ListStatementGates(IStatementAnalysisStore store)
{
    public const int MaximumLimit = 500;

    public Task<IReadOnlyList<StatementGateListItem>> ExecuteAsync(ReviewActor actor, StatementGateView? view, int limit,
        CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireOwner(actor);
        if (limit is < 1 or > MaximumLimit) throw new ArgumentOutOfRangeException(nameof(limit));
        return store.ListAsync(view, limit, cancellationToken);
    }
}
