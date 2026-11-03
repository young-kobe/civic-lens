using CivicLens.Application.Review;
using CivicLens.Core.Review;

namespace CivicLens.Application.Publication;

/// <summary>Copies the first publication times of the active release into the store. Run it once after the migration that adds the table.</summary>
public sealed class BackfillFirstPublished(IPublicationStore publications, IReleaseDirectory releases)
{
    public async Task<FirstPublishedBackfillResult> ExecuteAsync(ReviewActor actor, CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireOwner(actor);
        var state = await publications.GetStateAsync(cancellationToken);
        if (state.Active is null) return new(0, 0, 0);
        var manifest = await PreviousRelease.LoadManifestAsync(releases, state.Active, cancellationToken);
        var records = manifest.Records.Select(entry => new PublishedFirstPublication(entry.Kind, entry.RecordId, entry.FirstPublishedAtUtc))
            .Concat(manifest.Withdrawals.Select(withdrawal => new PublishedFirstPublication(withdrawal.Kind, withdrawal.RecordId, withdrawal.FirstPublishedAtUtc)))
            .ToList();
        var added = await publications.AddFirstPublishedAsync(records, cancellationToken);
        return new(records.Count, added, records.Count - added);
    }
}
