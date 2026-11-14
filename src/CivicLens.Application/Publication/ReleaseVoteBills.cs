using System.Collections.Immutable;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

/// <summary>
/// The action that each vote of one release links. A vote links the first action on its bill that the release holds.
/// The commit stores these links, and the vote page and the vote list read them.
/// </summary>
public static class ReleaseVoteBills
{
    public static ImmutableArray<ReleaseVoteBill> From(IEnumerable<PublishedRecordEntry> releaseRecords,
        IReadOnlyDictionary<string, ImmutableArray<string>> voteBillActions)
    {
        var actions = releaseRecords.Where(entry => entry.Kind == PublishedRecordKind.Action).Select(entry => entry.RecordId).ToHashSet(StringComparer.Ordinal);
        return [.. voteBillActions.OrderBy(vote => vote.Key, StringComparer.Ordinal)
            .Select(vote => (VoteRecordId: vote.Key, ActionRecordId: vote.Value.FirstOrDefault(actions.Contains)))
            .Where(vote => vote.ActionRecordId is not null)
            .Select(vote => new ReleaseVoteBill(vote.VoteRecordId, vote.ActionRecordId!))];
    }
}
