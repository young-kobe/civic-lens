using System.Collections.Immutable;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

/// <summary>The facts that the home page shows, picked by fixed rules from the release: the newest votes, the newest executive actions, the break leaders of the voting boards, and the count of officials.</summary>
public sealed record HomeHighlights(ImmutableArray<PublishedRecordEntry> Votes, ImmutableArray<PublishedRecordEntry> ExecutiveActions,
    ImmutableArray<BreakLeader> BreakLeaders, int OfficialCount)
{
    public const int VoteCount = 4;
    public const int ExecutiveActionCount = 3;

    public static HomeHighlights From(PublicationRelease release, ImmutableArray<OfficialBoardPage> boards, int officialCount) =>
        new(NewestOfKind(release, PublishedRecordKind.Vote, VoteCount), NewestOfKind(release, PublishedRecordKind.ExecutiveAction, ExecutiveActionCount),
            BreakLeader.All(boards), officialCount);

    private static ImmutableArray<PublishedRecordEntry> NewestOfKind(PublicationRelease release, PublishedRecordKind kind, int count) =>
        [.. ReleaseRecordOrder.NewestFirst(release.Records.Where(entry => entry.Kind == kind),
            ReleaseRecordOrder.Day, entry => entry.FirstPublishedAtUtc, entry => entry.RecordId).Take(count)];
}
