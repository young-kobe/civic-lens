using CivicLens.Core.Review;

namespace CivicLens.Application.Publication;

public sealed class ListPublicationReleases(IPublicationStore publications, IReleaseDirectory releases)
{
    public Task<PublicationReleaseList> ExecuteAsync(ReviewActor actor, int limit,
        CancellationToken cancellationToken = default)
    {
        _ = (publications, releases);
        throw new NotImplementedException();
    }
}
