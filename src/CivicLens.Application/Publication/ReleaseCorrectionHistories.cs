using System.Collections.Immutable;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

internal static class ReleaseCorrectionHistories
{
    public static async Task<ImmutableArray<CorrectionHistoryBinding>> PrepareAsync(IPublicationStore publications,
        IReleaseDirectory releases, int? previousReleaseNumber, PreviousRelease previous,
        IReadOnlyCollection<PublishedRecordEntry> carried, IReadOnlyCollection<PendingRecord> additions,
        CancellationToken cancellationToken)
    {
        var existing = previousReleaseNumber is { } number
            ? await publications.ReadCorrectionHistoriesAsync(number, [.. carried.Select(PublishedRevisionBinding.Of)], cancellationToken)
            : new Dictionary<PublishedRecordReference, StoredCorrectionHistory>();
        var bindings = new List<CorrectionHistoryBinding>(carried.Count + additions.Count);
        var pending = new List<PublicationCorrectionHistory>(256);
        foreach (var entry in carried)
        {
            var record = PublishedRevisionBinding.Of(entry);
            if (existing.TryGetValue(new(entry.Kind, entry.RecordId), out var stored))
                bindings.Add(stored.Binding);
            else
            {
                var content = RecordFileEntry.ReadContent(entry.Kind,
                    await ReleaseRecordFiles.ReadAsync(releases, previous.DirectoryName!, entry.RecordId, cancellationToken));
                if (PublishedRevisionBinding.Of(content.Entry) != record)
                    throw new InvalidDataException("A carried record file does not match its published revision.");
                pending.Add(new(record, content.Corrections));
            }
            await FlushIfFullAsync(publications, pending, bindings, cancellationToken);
        }
        foreach (var addition in additions)
        {
            pending.Add(new(PublishedRevisionBinding.Of(addition.Entry), addition.Corrections));
            await FlushIfFullAsync(publications, pending, bindings, cancellationToken);
        }
        if (pending.Count > 0)
            bindings.AddRange(await publications.PrepareCorrectionHistoriesAsync(pending, cancellationToken));
        return [.. bindings];
    }

    private static async Task FlushIfFullAsync(IPublicationStore publications, List<PublicationCorrectionHistory> pending,
        List<CorrectionHistoryBinding> bindings, CancellationToken cancellationToken)
    {
        if (pending.Count < 256) return;
        bindings.AddRange(await publications.PrepareCorrectionHistoriesAsync(pending, cancellationToken));
        pending.Clear();
    }
}
