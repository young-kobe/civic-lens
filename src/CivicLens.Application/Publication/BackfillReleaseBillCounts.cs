using CivicLens.Application.Review;
using CivicLens.Core.Review;

namespace CivicLens.Application.Publication;

/// <summary>
/// Fills the bill count rows of retained releases that earlier publications wrote without them. One release is one write transaction.
/// The files of a release must agree with the tables before the store writes. An exact existing snapshot is a replay, and a partial or changed one fails.
/// </summary>
public sealed class BackfillReleaseBillCounts(IPublicationStore publications, IReleaseDirectory releases)
{
    public Task<ReleaseBillCountBackfillResult> ExecuteAsync(ReviewActor actor, int limit, ReleaseBillCountVerificationCursor? after, CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireOwner(actor);
        return RetainedReleaseScan.BackfillAsync(publications, releases, ReleaseSnapshotKind.BillCounts, limit, after,
            (release, members) => publications.CommitBillCountsAsync(release, members, releases.GetActiveAsync, cancellationToken), cancellationToken);
    }
}
