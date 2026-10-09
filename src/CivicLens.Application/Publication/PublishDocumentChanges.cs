using CivicLens.Application.Documents;
using CivicLens.Application.Review;
using CivicLens.Core.Review;

namespace CivicLens.Application.Publication;

public sealed class PublishDocumentChanges(IPublicationStore publications, IReleaseDirectory releases,
    IReleaseRenderer renderer, IDocumentChangeReviewStore reviews, IDocumentExtractionStore extractions,
    PublicationCatalog catalog, TimeProvider clock)
{
    public Task<PublicationReleaseSummary> ExecuteAsync(ReviewActor actor, PublishDocumentChangesRequest request,
        CancellationToken cancellationToken = default)
    {
        _ = (publications, releases, renderer, reviews, extractions, catalog, clock);
        throw new NotImplementedException();
    }
}
