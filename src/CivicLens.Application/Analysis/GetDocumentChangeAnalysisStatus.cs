using CivicLens.Application.Review;
using CivicLens.Core.Review;

namespace CivicLens.Application.Analysis;

public sealed class GetDocumentChangeAnalysisStatus(IAnalysisStatusStore store)
{
    public Task<AnalysisStatus?> ExecuteAsync(ReviewActor actor, CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireReviewer(actor);
        return store.GetAsync(cancellationToken);
    }
}
