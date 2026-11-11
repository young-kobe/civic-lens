using CivicLens.Application.Review;
using CivicLens.Core.Review;

namespace CivicLens.Application.Publication;

/// <summary>
/// Proves in read-only transactions that the saved bill counts of every retained release equal the expected counts, and that no row is missing or extra.
/// It repeats the file checks of the backfill and never writes. A fingerprint pins the selection across pages.
/// </summary>
public sealed class VerifyReleaseBillCounts(IPublicationStore publications, IReleaseDirectory releases)
{
    private const int BatchSize = 5;
    private const int MaximumReportedFailures = 50;

    public async Task<ReleaseBillCountVerificationResult> ExecuteAsync(ReviewActor actor, int limit,
        ReleaseBillCountVerificationCursor? after, CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireOwner(actor);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        var examined = 0;
        var verified = 0;
        var failures = new List<ReleaseBillCountFailure>();
        var failed = 0;
        var cursor = after?.ReleaseNumber ?? 0;
        var selectionFingerprint = after?.SelectionFingerprint;
        string? next = null;
        while (examined < limit)
        {
            var page = await publications.ReadBillCountReleasePageAsync(cursor, selectionFingerprint, Math.Min(BatchSize, limit - examined),
                releases.GetActiveAsync, cancellationToken);
            if (page.StaleCursor)
                return new(examined, verified, failed, failures, null, true) { SelectionFingerprint = page.SelectionFingerprint };
            selectionFingerprint = page.SelectionFingerprint;
            foreach (var release in page.Releases)
            {
                examined++;
                cursor = release.ReleaseNumber;
                var failure = await VerifyAsync(release, cancellationToken);
                if (failure is null) verified++;
                else
                {
                    failed++;
                    if (failures.Count < MaximumReportedFailures) failures.Add(failure);
                }
            }
            if (!page.HasMore) break;
            if (examined >= limit) next = new ReleaseBillCountVerificationCursor(selectionFingerprint, cursor).Encode();
        }
        if (next is null)
        {
            // File reads run outside the short database snapshot, so refresh the selection once before declaring the scan terminal.
            var currentFingerprint = await publications.ReadRetainedReleaseFingerprintAsync(releases.GetActiveAsync, cancellationToken);
            if (currentFingerprint != selectionFingerprint)
                return new(examined, verified, failed, failures, null, true) { SelectionFingerprint = currentFingerprint };
        }
        return new(examined, verified, failed, failures, next, false) { SelectionFingerprint = selectionFingerprint ?? "" };
    }

    private async Task<ReleaseBillCountFailure?> VerifyAsync(PublicationReleaseSummary release, CancellationToken cancellationToken)
    {
        try
        {
            var members = await ReleaseBillCountFiles.ReadValidatedMembersAsync(publications, releases, release, cancellationToken);
            return await publications.VerifyBillCountsAsync(release, members, releases.GetActiveAsync, cancellationToken) switch
            {
                BillCountStatus.Verified => null,
                BillCountStatus.MissingRows => ReleaseBillCountFailure.Missing(release.ReleaseNumber),
                _ => ReleaseBillCountFailure.Different(release.ReleaseNumber)
            };
        }
        catch (Exception exception) when (ReleaseBillCountFailure.IsReportable(exception))
        {
            return ReleaseBillCountFailure.Of(release.ReleaseNumber, exception);
        }
    }
}
