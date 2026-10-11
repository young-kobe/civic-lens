using CivicLens.Application.Review;
using CivicLens.Core.Review;

namespace CivicLens.Application.Analysis;

public sealed class GetDocumentChangeAnalysisStatus(IDocumentChangeAnalysisStatusStore store)
{
    public Task<DocumentChangeAnalysisStatus?> ExecuteAsync(ReviewActor actor, CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireReviewer(actor);
        return store.GetAsync(cancellationToken);
    }
}
