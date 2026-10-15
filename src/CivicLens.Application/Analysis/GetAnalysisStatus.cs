using CivicLens.Application.Review;
using CivicLens.Core.Review;

namespace CivicLens.Application.Analysis;

public sealed class GetAnalysisStatus<TRun, TView, TItem>(IAnalysisQueueStore<TRun, TView, TItem> store, IAnalysisStatusStore status)
    where TRun : class
    where TView : struct, Enum
{
    public async Task<AnalysisStatusReport<TView>> ExecuteAsync(ReviewActor actor, CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireOwner(actor);
        return new(await status.GetAsync(cancellationToken), await store.GetChargedTodayAsync(cancellationToken),
            await store.CountAsync(cancellationToken));
    }
}
