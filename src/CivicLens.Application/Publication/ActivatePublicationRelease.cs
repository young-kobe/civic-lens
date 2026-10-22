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
        await publications.ServeActiveAsync(releases.ActivateAsync, cancellationToken);
        return await CompareAsync(replaced, summary, cancellationToken);
    }

    // The switch is done when this runs, so a failed manifest read must not fail the command.
    private async Task<ActivatedRelease> CompareAsync(PublicationReleaseSummary? replaced, PublicationReleaseSummary activated,
        CancellationToken cancellationToken)
    {
        try
        {
            var before = replaced is null ? null : await PreviousRelease.LoadManifestAsync(releases, replaced, cancellationToken);
            var after = await PreviousRelease.LoadManifestAsync(releases, activated, cancellationToken);
            return new(activated, Overlap(before?.Withdrawals, after.Records), Overlap(after.Withdrawals, before?.Records),
                await blocked.FindAsync(after.Records, cancellationToken));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Release {ReleaseNumber} is active, but its rollback comparison could not be read.",
                activated.ReleaseNumber);
            return new(activated, [], [], []);
        }
    }

    private static PublishedRecordReference[] Overlap(IEnumerable<PublishedWithdrawal>? withdrawals, IEnumerable<PublishedRecordEntry>? records)
    {
        var withdrawn = (withdrawals ?? []).Select(withdrawal => new PublishedRecordReference(withdrawal.Kind, withdrawal.RecordId)).ToHashSet();
        return [.. (records ?? []).Select(entry => new PublishedRecordReference(entry.Kind, entry.RecordId)).Where(withdrawn.Contains)];
    }
}
