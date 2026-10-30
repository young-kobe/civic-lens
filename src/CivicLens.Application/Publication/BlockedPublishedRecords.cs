using CivicLens.Core.Review;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

/// <summary>Finds the published records that cannot move forward: review blocks them, or a person revision is stuck at the correction cap.</summary>
public sealed class BlockedPublishedRecords(IPublicationStore publications, IReleaseDirectory releases)
{
    public async Task<IReadOnlyList<BlockedPublishedRecord>> FindAsync(string directoryName, IReadOnlyList<PublishedRecordEntry> entries,
        IReadOnlyList<PublishedWithdrawal> withdrawals, CancellationToken cancellationToken)
    {
        var states = await publications.ReadReviewStatusesAsync(
            [.. entries.Select(PublishedRevisionBinding.Of), .. withdrawals.Select(WithdrawnBinding)], cancellationToken);
        List<BlockedPublishedRecord> found = [];
        foreach (var entry in entries)
        {
            if (!states.TryGetValue(new(entry.Kind, entry.RecordId), out var state)) continue;
            if (PublishedRecordRules.IsWithdrawable(state.ReviewStatus))
                found.Add(Blocked(entry, state, BlockedReason.ReviewBlocksIt));
            else if (await IsStuckAtCorrectionLimitAsync(directoryName, entry, state, cancellationToken))
                found.Add(Blocked(entry, state, BlockedReason.CorrectionLimit));
        }
        foreach (var withdrawal in withdrawals)
        {
            if (states.TryGetValue(new(withdrawal.Kind, withdrawal.RecordId), out var state) && IsApprovedByPerson(state) &&
                PublishedCorrection.IsAtNoteLimit(withdrawal.Corrections))
                found.Add(Blocked(withdrawal, state));
        }
        return found;
    }

    private async Task<bool> IsStuckAtCorrectionLimitAsync(string directoryName, PublishedRecordEntry entry, ReviewedRecord state,
        CancellationToken cancellationToken)
    {
        if (!IsApprovedByPerson(state)) return false;
        var corrections = await PublicationCorrections.ReadAsync(releases, directoryName, new(entry.Kind, entry.RecordId), cancellationToken);
        return PublishedCorrection.IsAtNoteLimit(corrections);
    }

    private static bool IsApprovedByPerson(ReviewedRecord state) =>
        state.UnpublishedApprovalActor is { } actor && !ReviewAuthor.IsCode(actor);

    private static PublishedRevisionBinding WithdrawnBinding(PublishedWithdrawal withdrawal) =>
        new(withdrawal.Kind, withdrawal.RecordId, withdrawal.WithdrawnRevisionNumber);

    private static BlockedPublishedRecord Blocked(PublishedRecordEntry entry, ReviewedRecord state, BlockedReason reason) =>
        new(entry.Kind, entry.RecordId, entry.Headline, entry.RevisionNumber, state.ReviewStatus, reason);

    // A withdrawal notice holds no headline, so the row names the record by its ID.
    private static BlockedPublishedRecord Blocked(PublishedWithdrawal withdrawal, ReviewedRecord state) =>
        new(withdrawal.Kind, withdrawal.RecordId, $"Withdrawn record {withdrawal.RecordId}", withdrawal.WithdrawnRevisionNumber,
            state.ReviewStatus, BlockedReason.WithdrawnAtCorrectionLimit);
}
