using System.Collections.Immutable;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

/// <summary>The facts that the home page shows, picked by fixed rules from the release: the newest House votes, the newest executive actions, the break leaders of the voting boards, and the count of officials.</summary>
public sealed record HomeHighlights(ImmutableArray<PublishedRecordEntry> Votes, ImmutableArray<PublishedRecordEntry> ExecutiveActions,
    ImmutableArray<BreakLeader> BreakLeaders, int OfficialCount)
{
    public const int VoteCount = 4;
    public const int ExecutiveActionCount = 3;

    public static HomeHighlights From(PublicationRelease release, ImmutableArray<OfficialBoardPage> boards, int officialCount)
    {
        var byKind = release.Records.ToLookup(entry => entry.Kind);
        return new(NewestOf(byKind[PublishedRecordKind.Vote].Where(entry => entry.Chamber == PublishedChamber.House), VoteCount), NewestOf(byKind[PublishedRecordKind.ExecutiveAction], ExecutiveActionCount),
            BreakLeader.All(boards), officialCount);
    }

    private static ImmutableArray<PublishedRecordEntry> NewestOf(IEnumerable<PublishedRecordEntry> entries, int count) =>
        [.. ReleaseRecordOrder.NewestFirst(entries, ReleaseRecordOrder.Day, entry => entry.FirstPublishedAtUtc, entry => entry.RecordId).Take(count)];
}
