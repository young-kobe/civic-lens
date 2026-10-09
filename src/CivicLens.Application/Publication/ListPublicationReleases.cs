using CivicLens.Application.Review;
using CivicLens.Core.Review;

namespace CivicLens.Application.Publication;

public sealed class ListPublicationReleases(IPublicationStore publications, IReleaseDirectory releases)
{
    public async Task<PublicationReleaseList> ExecuteAsync(ReviewActor actor, int limit,
        CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireReviewer(actor);
        if (limit is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(limit), "Limit must be from 1 to 100.");
        var items = await publications.ListAsync(limit, cancellationToken);
        return new(items, await releases.GetActiveAsync(cancellationToken));
    }
}
