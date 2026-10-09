using CivicLens.Core.Review;

namespace CivicLens.Application.Publication;

public sealed class ActivatePublicationRelease(IPublicationStore publications, IReleaseDirectory releases)
{
    public Task<PublicationReleaseSummary> ExecuteAsync(ReviewActor actor, int releaseNumber,
        CancellationToken cancellationToken = default)
    {
        _ = (publications, releases);
        throw new NotImplementedException();
    }
}
