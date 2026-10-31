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
        switch (entry.Kind)
        {
            case PublishedRecordKind.Vote:
                var vote = await ReleaseRecordFiles.ReadAsync<PublishedVote>(releases, entry, [], previous, cancellationToken);
                vote.Validate();
                return RequireSameOutsideNewFields(entry, vote.ToEntry());
            case PublishedRecordKind.Action:
                var action = await ReleaseRecordFiles.ReadAsync<PublishedAction>(releases, entry, [], previous, cancellationToken);
                action.Validate();
                return RequireSameOutsideNewFields(entry, action.ToEntry());
            default:
                return entry;
        }
    }

    private static PublishedRecordEntry RequireSameOutsideNewFields(PublishedRecordEntry old, PublishedRecordEntry rebuilt) =>
        Serialize(WithoutNewFields(old)) == Serialize(WithoutNewFields(rebuilt))
            ? rebuilt
            : throw new InvalidDataException($"The record file of {old.Kind} {old.RecordId} does not match its manifest entry, so the entry cannot be rebuilt.");

    private static PublishedRecordEntry WithoutNewFields(PublishedRecordEntry entry) =>
        entry with { YeaCount = null, NayCount = null, Alignment = null, CosponsorIds = null };

    private static string Serialize(PublishedRecordEntry entry) => JsonSerializer.Serialize(entry, PublicationProtocol.JsonOptions);
}
