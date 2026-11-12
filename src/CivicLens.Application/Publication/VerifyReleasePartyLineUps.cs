using CivicLens.Application.Review;
using CivicLens.Core.Review;

namespace CivicLens.Application.Publication;

/// <summary>
/// Proves in read-only transactions that the saved party line-up of every retained release equals the expected rows, and that no row is missing or extra.
/// It repeats the file checks of the backfill and never writes. A fingerprint pins the selection across pages.
/// </summary>
public sealed class VerifyReleasePartyLineUps(IPublicationStore publications, IReleaseDirectory releases)
{
    public Task<ReleaseBillCountVerificationResult> ExecuteAsync(ReviewActor actor, int limit,
        ReleaseBillCountVerificationCursor? after, CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireOwner(actor);
        return RetainedReleaseScan.VerifyAsync(publications, releases, ReleaseSnapshotKind.PartyLineUps, limit, after,
            (release, members) => publications.VerifyPartyLineUpsAsync(release, members, releases.GetActiveAsync, cancellationToken), cancellationToken);
    }
}
