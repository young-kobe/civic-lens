using CivicLens.Application.Analysis;
using CivicLens.Application.Review;
using CivicLens.Core.Review;

namespace CivicLens.Application.Statements;

public sealed class GetStatementAnalysisStatus<TRun, TView, TItem>(IStatementQueueStore<TRun, TView, TItem> store, IAnalysisStatusStore status)
    where TRun : class
    where TView : struct, Enum
{
    public async Task<StatementAnalysisStatusReport<TView>> ExecuteAsync(ReviewActor actor, CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireOwner(actor);
        return new(await status.GetAsync(cancellationToken), await store.GetChargedTodayAsync(cancellationToken),
            await store.CountAsync(cancellationToken));
    }
}
