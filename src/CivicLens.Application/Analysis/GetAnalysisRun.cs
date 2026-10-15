using CivicLens.Application.Review;
using CivicLens.Core.Review;

namespace CivicLens.Application.Analysis;

public sealed class GetAnalysisRun<TRun, TView, TItem>(IAnalysisQueueStore<TRun, TView, TItem> store)
    where TRun : class
    where TView : struct, Enum
{
    public Task<TRun?> ExecuteAsync(ReviewActor actor, string runId, CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireOwner(actor);
        return store.GetRunAsync(ReviewValidation.ValidateRunId(runId), cancellationToken);
    }
}
