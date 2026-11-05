using System.Text.Json;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

/// <summary>
/// Builds the manifest entries of carried vote and action records again from their record files. A carried entry from before a contract
/// change then gets the new optional fields. The record files stay as they are, and every other entry stays as it is. A record file that
/// is invalid, or that differs from its entry outside the new fields, stops the rewrite.
/// </summary>
internal static class ReleaseEntryRewrite
{
    public static async Task<List<PublishedRecordEntry>> RebuildAsync(IReleaseDirectory releases, IEnumerable<PublishedRecordEntry> carried,
        PreviousRelease previous, CancellationToken cancellationToken)
    {
        List<PublishedRecordEntry> rebuilt = [];
        foreach (var entry in carried) rebuilt.Add(await RebuildAsync(releases, entry, previous, cancellationToken));
        return rebuilt;
    }

    private static async Task<PublishedRecordEntry> RebuildAsync(IReleaseDirectory releases, PublishedRecordEntry entry,
        PreviousRelease previous, CancellationToken cancellationToken)
    {
        if (entry.Kind is not (PublishedRecordKind.Vote or PublishedRecordKind.Action)) return entry;
        PublishedRecordEntry rebuilt;
        try
        {
            rebuilt = RecordFileEntry.Read(entry.Kind, await ReleaseRecordFiles.ReadAsync(releases, previous.DirectoryName!, entry.RecordId,
                cancellationToken));
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException)
        {
            throw new ReleaseRewriteException($"The record file of {entry.Kind} {entry.RecordId} is invalid, so the entries cannot be rebuilt.", exception);
        }
        return ReleaseEntryComparison.SameOutsideNewFields(entry, rebuilt)
            ? rebuilt
            : throw new ReleaseRewriteException($"The record file of {entry.Kind} {entry.RecordId} does not match its manifest entry, so the entry cannot be rebuilt.");
    }
}
