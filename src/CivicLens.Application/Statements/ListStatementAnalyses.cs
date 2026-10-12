using CivicLens.Application.Review;
using CivicLens.Core.Review;

namespace CivicLens.Application.Statements;

public sealed class ListStatementAnalyses<TRun, TView, TItem>(IStatementQueueStore<TRun, TView, TItem> store)
    where TRun : class
    where TView : struct, Enum
{
    public const int MaximumLimit = 500;

    public Task<IReadOnlyList<TItem>> ExecuteAsync(ReviewActor actor, TView? view, int limit, CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireOwner(actor);
        if (limit is < 1 or > MaximumLimit) throw new ArgumentOutOfRangeException(nameof(limit));
        return store.ListAsync(view, limit, cancellationToken);
    }
}
