using CivicLens.Application.Review;
using CivicLens.Core.Review;

namespace CivicLens.Application.Publication;

/// <summary>
/// Fills the bill count rows of retained releases that earlier publications wrote without them. One release is one write transaction.
/// The files of a release must agree with the tables before the store writes. An exact existing snapshot is a replay, and a partial or changed one fails.
/// </summary>
public sealed class BackfillReleaseBillCounts(IPublicationStore publications, IReleaseDirectory releases)
{
    private const int MaximumReportedFailures = 50;
    private const int BatchSize = 5;

    public async Task<ReleaseBillCountBackfillResult> ExecuteAsync(ReviewActor actor, int limit, int after,
        CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireOwner(actor);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(after);
        var examined = 0;
        var added = 0;
        var alreadyPresent = 0;
        var failures = new List<ReleaseBillCountFailure>();
        var failed = 0;
        var cursor = after;
        int? continuation = null;
        while (examined < limit)
        {
            var page = await publications.ReadBillCountReleasePageAsync(cursor, null, Math.Min(BatchSize, limit - examined),
                releases.GetActiveAsync, cancellationToken);
            foreach (var release in page.Releases)
            {
                examined++;
                cursor = release.ReleaseNumber;
                try
                {
                    var members = await ReleaseBillCountFiles.ReadValidatedMembersAsync(publications, releases, release, cancellationToken);
                    if (await publications.CommitBillCountsAsync(release, members, releases.GetActiveAsync, cancellationToken)) added++;
                    else alreadyPresent++;
                }
                catch (Exception exception) when (ReleaseBillCountFailure.IsReportable(exception))
                {
                    failed++;
                    if (failures.Count < MaximumReportedFailures) failures.Add(ReleaseBillCountFailure.Of(release.ReleaseNumber, exception));
                }
            }
            if (!page.HasMore) break;
            if (examined == limit) continuation = cursor;
        }
        return new(examined, added, alreadyPresent, failed, failures, continuation);
    }
}
