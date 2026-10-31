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

    private static Task<PublishedRecordEntry> RebuildAsync(IReleaseDirectory releases, PublishedRecordEntry entry,
        PreviousRelease previous, CancellationToken cancellationToken) =>
        entry.Kind switch
        {
            PublishedRecordKind.Vote => RebuildAsync<PublishedVote>(releases, entry, previous, vote => { vote.Validate(); return vote.ToEntry(); }, cancellationToken),
            PublishedRecordKind.Action => RebuildAsync<PublishedAction>(releases, entry, previous, action => { action.Validate(); return action.ToEntry(); }, cancellationToken),
            _ => Task.FromResult(entry)
        };

    private static async Task<PublishedRecordEntry> RebuildAsync<TRecord>(IReleaseDirectory releases, PublishedRecordEntry entry,
        PreviousRelease previous, Func<TRecord, PublishedRecordEntry> rebuild, CancellationToken cancellationToken)
        where TRecord : class
    {
        PublishedRecordEntry rebuilt;
        try
        {
            rebuilt = rebuild(await ReleaseRecordFiles.ReadAsync<TRecord>(releases, entry, [], previous, cancellationToken));
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException)
        {
            throw new ReleaseRewriteException($"The record file of {entry.Kind} {entry.RecordId} is invalid, so the entries cannot be rebuilt.", exception);
        }
        return RequireSameOutsideNewFields(entry, rebuilt);
    }

    private static PublishedRecordEntry RequireSameOutsideNewFields(PublishedRecordEntry old, PublishedRecordEntry rebuilt) =>
        Serialize(WithoutNewFields(old)) == Serialize(WithoutNewFields(rebuilt))
            ? rebuilt
            : throw new ReleaseRewriteException($"The record file of {old.Kind} {old.RecordId} does not match its manifest entry, so the entry cannot be rebuilt.");

    private static PublishedRecordEntry WithoutNewFields(PublishedRecordEntry entry) =>
        entry with { YeaCount = null, NayCount = null, Alignment = null, CosponsorIds = null };

    private static string Serialize(PublishedRecordEntry entry) => JsonSerializer.Serialize(entry, PublicationProtocol.JsonOptions);
}
