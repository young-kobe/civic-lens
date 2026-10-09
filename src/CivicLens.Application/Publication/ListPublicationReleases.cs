using CivicLens.Application.Review;
using CivicLens.Core.Review;

namespace CivicLens.Application.Publication;

public sealed class ListPublicationReleases(IPublicationStore publications, IReleaseDirectory releases)
{
    public async Task<PublicationReleaseList> ExecuteAsync(ReviewActor actor, int limit,
        CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireReviewer(actor);
        ReviewValidation.ValidatePage(null, limit);
        var items = await publications.ListAsync(limit, cancellationToken);
        var state = await publications.GetStateAsync(cancellationToken);
        return new(items, state.Active?.ReleaseNumber, await releases.GetActiveAsync(cancellationToken));
    }
}
