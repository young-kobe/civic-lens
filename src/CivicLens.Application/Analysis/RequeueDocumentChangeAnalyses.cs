using CivicLens.Application.Review;
using CivicLens.Core.Review;

namespace CivicLens.Application.Analysis;

public sealed class RequeueDocumentChangeAnalyses(IDocumentChangeAnalysisStore store)
{
    public Task<int> ExecuteAsync(ReviewActor actor, string? comparisonId, CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireOwner(actor);
        if (comparisonId is not null) ReviewValidation.ValidateHash(comparisonId, nameof(comparisonId));
        return store.RequeueFailedAsync(comparisonId, cancellationToken);
    }
}
