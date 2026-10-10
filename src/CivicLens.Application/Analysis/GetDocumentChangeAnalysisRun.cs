using CivicLens.Application.Review;
using CivicLens.Core.Analysis;
using CivicLens.Core.Review;

namespace CivicLens.Application.Analysis;

public sealed class GetDocumentChangeAnalysisRun(IDocumentChangeAnalysisStore store)
{
    public Task<AnalysisRun?> ExecuteAsync(ReviewActor actor, string draftId, CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireReviewer(actor);
        return store.GetDraftRunAsync(ReviewValidation.ValidateDraftId(draftId), cancellationToken);
    }
}
