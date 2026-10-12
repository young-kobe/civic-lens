using CivicLens.Application.Review;
using CivicLens.Core.Review;

namespace CivicLens.Application.Analysis;

public sealed class RequeueAnalyses<TRun>(IAnalysisQueue<TRun> store) where TRun : class
{
    public Task<int> ExecuteAsync(ReviewActor actor, string? subjectId, CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireOwner(actor);
        if (subjectId is not null) ReviewValidation.ValidateHash(subjectId, nameof(subjectId));
        return store.RequeueFailedAsync(subjectId, cancellationToken);
    }
}
