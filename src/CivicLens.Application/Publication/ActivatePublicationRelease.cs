using CivicLens.Application.Review;
using CivicLens.Core.Review;

namespace CivicLens.Application.Publication;

public sealed class ActivatePublicationRelease(IPublicationStore publications, IReleaseDirectory releases)
{
    public async Task<PublicationReleaseSummary> ExecuteAsync(ReviewActor actor, int releaseNumber,
        CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireOwner(actor);
        if (releaseNumber < 1) throw new ArgumentOutOfRangeException(nameof(releaseNumber), "Release number must be at least 1.");
        var summary = await publications.GetAsync(releaseNumber, cancellationToken)
            ?? throw new ArgumentException($"Release {releaseNumber} has not been committed.", nameof(releaseNumber));
        await releases.ActivateAsync(summary.DirectoryName, cancellationToken);
        return summary;
    }
}
