using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

/// <summary>
/// Builds the manifest entries of carried vote and action records again from their record files. A carried entry from before a contract
/// change then gets the new optional fields. The record files stay as they are, and every other entry stays as it is.
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
        switch (entry.Kind)
        {
            case PublishedRecordKind.Vote:
                return (await ReleaseRecordFiles.ReadAsync<PublishedVote>(releases, entry, [], previous, cancellationToken)).ToEntry();
            case PublishedRecordKind.Action:
                return (await ReleaseRecordFiles.ReadAsync<PublishedAction>(releases, entry, [], previous, cancellationToken)).ToEntry();
            default:
                return entry;
        }
    }
}
