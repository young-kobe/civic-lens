using System.Collections.Immutable;
using CivicLens.Core.Relationships;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

/// <summary>
/// One approved link from a statement of an official to a bill or a vote. A link belongs to the Congress of its statement day.
/// The cast is the cast of the official on a linked vote, and it is empty when the official has no vote row for that vote.
/// </summary>
public sealed record LinkedPair(DateOnly StatementDay, string StatementRecordId, string StatementHeadline, PublishedRelationshipType Type,
    PublishedRecordKind TargetKind, string TargetRecordId, string TargetTitle, string? Cast)
{
    public static ImmutableArray<LinkedPair> In(int congress, IEnumerable<PublishedRecordEntry> entries,
        IReadOnlyDictionary<(PublishedRecordKind Kind, string RecordId), PublishedRecordEntry> release, IEnumerable<PublishedOfficialVote> votes)
    {
        var casts = votes.ToDictionary(vote => vote.RecordId, vote => vote.Cast, StringComparer.Ordinal);
        var pairs = new List<(LinkedPair Pair, DateTimeOffset PublishedAtUtc)>();
        foreach (var link in entries.Where(entry => entry.Kind == PublishedRecordKind.Relationship &&
                     CongressCalendar.CongressOn(ReleaseRecordOrder.Day(entry)) == congress))
        {
            if (!release.TryGetValue((PublishedRecordKind.Statement, link.StatementRecordId!), out var statement) ||
                !release.TryGetValue((link.TargetKind!.Value, link.TargetRecordId!), out var target))
                continue;
            var cast = link.TargetKind == PublishedRecordKind.Vote && casts.TryGetValue(target.RecordId, out var found) ? found : null;
            pairs.Add((new(ReleaseRecordOrder.Day(link), statement.RecordId, statement.Headline, link.LinkType!.Value, target.Kind,
                target.RecordId, target.BillTitle ?? target.Headline, cast), link.FirstPublishedAtUtc));
        }
        return [.. ReleaseRecordOrder.NewestFirst(pairs, pair => pair.Pair.StatementDay, pair => pair.PublishedAtUtc,
            pair => pair.Pair.StatementRecordId + pair.Pair.TargetRecordId).Select(pair => pair.Pair)];
    }
}
