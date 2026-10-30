using CivicLens.Core.Review;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

/// <summary>Finds the published records that cannot move forward: review blocks them, or a person revision is stuck at the correction cap.</summary>
public sealed class BlockedPublishedRecords(IPublicationStore publications, IReleaseDirectory releases)
{
    public async Task<IReadOnlyList<BlockedPublishedRecord>> FindAsync(string directoryName, IReadOnlyList<PublishedRecordEntry> entries,
        CancellationToken cancellationToken)
    {
        var states = await publications.ReadReviewStatusesAsync([.. entries.Select(PublishedRevisionBinding.Of)], cancellationToken);
        List<BlockedPublishedRecord> found = [];
        foreach (var entry in entries)
        {
            if (!states.TryGetValue(new(entry.Kind, entry.RecordId), out var state)) continue;
            if (PublishedRecordRules.IsWithdrawable(state.ReviewStatus))
                found.Add(Blocked(entry, state, BlockedReason.ReviewBlocksIt));
            else if (await IsStuckAtCorrectionLimitAsync(directoryName, entry, state, cancellationToken))
                found.Add(Blocked(entry, state, BlockedReason.CorrectionLimit));
        }
        return found;
    }

    private async Task<bool> IsStuckAtCorrectionLimitAsync(string directoryName, PublishedRecordEntry entry, ReviewedRecord state,
        CancellationToken cancellationToken)
    {
        if (state.UnpublishedApprovalActor is not { } actor || ReviewAuthor.IsCode(actor)) return false;
        var corrections = await PublicationCorrections.ReadAsync(releases, directoryName, new(entry.Kind, entry.RecordId), cancellationToken);
        return PublishedCorrection.NotedCount(corrections) >= PublishedCorrection.MaximumNotedEntries;
    }

    private static BlockedPublishedRecord Blocked(PublishedRecordEntry entry, ReviewedRecord state, BlockedReason reason) =>
        new(entry.Kind, entry.RecordId, entry.Headline, entry.RevisionNumber, state.ReviewStatus, reason);
}
