using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

/// <summary>Finds the published records that review now blocks, which are the records a release may withdraw.</summary>
public sealed class BlockedPublishedRecords(IPublicationStore publications)
{
    public async Task<IReadOnlyList<BlockedPublishedRecord>> FindAsync(IReadOnlyList<PublishedRecordEntry> entries,
        CancellationToken cancellationToken)
    {
        var statuses = await publications.ReadReviewStatusesAsync(
            [.. entries.Select(entry => new PublishedRecordReference(entry.Kind, entry.RecordId))], cancellationToken);
        List<BlockedPublishedRecord> found = [];
        foreach (var entry in entries)
        {
            if (!statuses.TryGetValue(new(entry.Kind, entry.RecordId), out var status) || !PublishedRecordRules.IsWithdrawable(status))
                continue;
            found.Add(new(entry.Kind, entry.RecordId, entry.Headline, entry.RevisionNumber, status));
        }
        return found;
    }
}
