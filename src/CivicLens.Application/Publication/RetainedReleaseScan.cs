namespace CivicLens.Application.Publication;

/// <summary>
/// Owns the bounded walk over the retained, active, and served releases that the bill count and party line-up owner commands share. It reads a page of
/// releases, checks the files of each release, and then asks the caller to write or verify one release in its own transaction.
/// A fingerprint pins the selection of the verifier across pages.
/// </summary>
internal static class RetainedReleaseScan
{
    private const int BatchSize = 5;
    private const int MaximumReportedFailures = 50;

    public static async Task<ReleaseBillCountBackfillResult> BackfillAsync(IPublicationStore publications, IReleaseDirectory releases,
        ReleaseSnapshotKind kind, int limit, int after, Func<PublicationReleaseSummary, IReadOnlyList<PublishedRevisionBinding>, Task<bool>> commit,
        CancellationToken cancellationToken)
    {
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
                    if (await commit(release, members)) added++;
                    else alreadyPresent++;
                }
                catch (Exception exception) when (ReleaseBillCountFailure.IsReportable(exception))
                {
                    failed++;
                    if (failures.Count < MaximumReportedFailures) failures.Add(ReleaseBillCountFailure.Of(release.ReleaseNumber, exception, kind));
                }
            }
            if (!page.HasMore) break;
            if (examined == limit) continuation = cursor;
        }
        return new(examined, added, alreadyPresent, failed, failures, continuation);
    }

    public static async Task<ReleaseBillCountVerificationResult> VerifyAsync(IPublicationStore publications, IReleaseDirectory releases,
        ReleaseSnapshotKind kind, int limit, ReleaseBillCountVerificationCursor? after,
        Func<PublicationReleaseSummary, IReadOnlyList<PublishedRevisionBinding>, Task<BillCountStatus>> verify, CancellationToken cancellationToken)
    {
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
                var failure = await VerifyOneAsync(publications, releases, kind, release, verify, cancellationToken);
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

    private static async Task<ReleaseBillCountFailure?> VerifyOneAsync(IPublicationStore publications, IReleaseDirectory releases,
        ReleaseSnapshotKind kind, PublicationReleaseSummary release,
        Func<PublicationReleaseSummary, IReadOnlyList<PublishedRevisionBinding>, Task<BillCountStatus>> verify, CancellationToken cancellationToken)
    {
        try
        {
            var members = await ReleaseBillCountFiles.ReadValidatedMembersAsync(publications, releases, release, cancellationToken);
            return await verify(release, members) switch
            {
                BillCountStatus.Verified => null,
                BillCountStatus.MissingRows => ReleaseBillCountFailure.Missing(release.ReleaseNumber, kind),
                _ => ReleaseBillCountFailure.Different(release.ReleaseNumber, kind)
            };
        }
        catch (Exception exception) when (ReleaseBillCountFailure.IsReportable(exception))
        {
            return ReleaseBillCountFailure.Of(release.ReleaseNumber, exception, kind);
        }
    }
}
