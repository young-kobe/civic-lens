using CivicLens.Application.Review;
using CivicLens.Core.Review;

namespace CivicLens.Application.Publication;

public sealed class ListBlockedPublished(IPublicationStore publications, IReleaseDirectory releases, BlockedPublishedRecords blocked)
{
    public async Task<IReadOnlyList<BlockedPublishedRecord>> ExecuteAsync(ReviewActor actor,
        CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireOwner(actor);
        var active = (await publications.GetStateAsync(cancellationToken)).Active;
        if (active is null) return [];
        var manifest = await PreviousRelease.LoadManifestAsync(releases, active, cancellationToken);
        return await blocked.FindAsync(manifest.Records, cancellationToken);
    }
}
