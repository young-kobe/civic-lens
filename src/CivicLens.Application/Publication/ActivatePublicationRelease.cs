using CivicLens.Application.Review;
using CivicLens.Core.Review;
using CivicLens.Publication.Contracts;
using Microsoft.Extensions.Logging;

namespace CivicLens.Application.Publication;

public sealed class ActivatePublicationRelease(IPublicationStore publications, IReleaseDirectory releases,
    BlockedPublishedRecords blocked, ILogger<ActivatePublicationRelease> logger)
{
    public async Task<ActivatedRelease> ExecuteAsync(ReviewActor actor, int releaseNumber,
        CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireOwner(actor);
        if (releaseNumber < 1) throw new ArgumentOutOfRangeException(nameof(releaseNumber), "Release number must be at least 1.");
        var replaced = (await publications.GetStateAsync(cancellationToken)).Active;
        var summary = await publications.ActivateAsync(releaseNumber, cancellationToken);
        var served = await publications.ServeActiveAsync(releases.ActivateAsync, cancellationToken) ?? summary;
        return await CompareAsync(replaced, summary, served, cancellationToken);
    }

    // The switch is done when this runs, so a failed read must not fail the command. Each failure empties only its own lists.
    private async Task<ActivatedRelease> CompareAsync(PublicationReleaseSummary? replaced, PublicationReleaseSummary activated,
        PublicationReleaseSummary served, CancellationToken cancellationToken)
    {
        var after = await TryReadAsync<PublicationRelease?>(
            async () => await PreviousRelease.LoadManifestAsync(releases, served, cancellationToken), null, served, "manifest");
        if (after is null) return new(activated, [], [], []);
        var (restored, withdrawnAgain) = await TryReadAsync(() => CompareWithReplacedAsync(replaced, after, cancellationToken),
            ([], []), served, "rollback comparison");
        var blockedRecords = await TryReadAsync<IReadOnlyList<BlockedPublishedRecord>>(
            () => blocked.FindAsync(after.Records, cancellationToken), [], served, "blocked list");
        return new(activated, restored, withdrawnAgain, blockedRecords);
    }

    private async Task<(PublishedRecordReference[] Restored, PublishedRecordReference[] WithdrawnAgain)> CompareWithReplacedAsync(
        PublicationReleaseSummary? replaced, PublicationRelease after, CancellationToken cancellationToken)
    {
        var before = replaced is null ? null : await PreviousRelease.LoadManifestAsync(releases, replaced, cancellationToken);
        return (Overlap(before?.Withdrawals, after.Records), Overlap(after.Withdrawals, before?.Records));
    }

    private async Task<T> TryReadAsync<T>(Func<Task<T>> read, T unread, PublicationReleaseSummary release, string what)
    {
        try
        {
            return await read();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Release {ReleaseNumber} is active, but its {Read} could not be read.", release.ReleaseNumber, what);
            return unread;
        }
    }

    private static PublishedRecordReference[] Overlap(IEnumerable<PublishedWithdrawal>? withdrawals, IEnumerable<PublishedRecordEntry>? records)
    {
        var withdrawn = (withdrawals ?? []).Select(withdrawal => new PublishedRecordReference(withdrawal.Kind, withdrawal.RecordId)).ToHashSet();
        return [.. (records ?? []).Select(entry => new PublishedRecordReference(entry.Kind, entry.RecordId)).Where(withdrawn.Contains)];
    }
}
