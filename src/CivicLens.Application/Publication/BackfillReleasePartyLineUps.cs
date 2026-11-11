using CivicLens.Application.Review;
using CivicLens.Core.Review;

namespace CivicLens.Application.Publication;

/// <summary>
/// Fills the party line-up rows of retained releases that earlier publications wrote without them. One release is one write transaction.
/// It checks the files of the release as the bill count backfill does, because the line-up counts the same bills. An exact existing snapshot is a replay,
/// and a partial or changed one fails. A release with no counted issue needs no rows and counts as already present.
/// </summary>
public sealed class BackfillReleasePartyLineUps(IPublicationStore publications, IReleaseDirectory releases)
{
    public Task<ReleaseBillCountBackfillResult> ExecuteAsync(ReviewActor actor, int limit, int after, CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireOwner(actor);
        return RetainedReleaseScan.BackfillAsync(publications, releases, ReleaseSnapshotKind.PartyLineUps, limit, after,
            (release, members) => publications.CommitPartyLineUpsAsync(release, members, releases.GetActiveAsync, cancellationToken), cancellationToken);
    }
}
