using CivicLens.Application.Publication;
using CivicLens.Publication.Contracts;

namespace CivicLens.Tests.Application.Publication;

public sealed class ClosestVoteTests
{
    private static readonly DateTimeOffset Published = new(2026, 5, 11, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TheClosestVoteHasTheSmallestGapBetweenItsYeaAndNayCountsAndATieGoesToTheLowerRecordId()
    {
        var closest = ClosestVote.From(ReleaseOf(Vote('b', 216, 214), Vote('a', 215, 217), Vote('c', 300, 120)));

        Assert.Equal(Id('a'), closest!.Entry.RecordId);
        Assert.Equal(2, closest.Gap);
        Assert.Equal(3, closest.VotesInWindow);
    }

    [Fact]
    public void OnlyVotesDatedInTheSevenDaysThatEndOnTheReleaseDayCountSoARecessGivesNoStaleCard()
    {
        var release = ReleaseOf(Vote('a', 216, 215) with { EventDate = new DateOnly(2026, 5, 4) }, Vote('b', 300, 100) with { EventDate = new DateOnly(2026, 5, 5) },
            Vote('c', 216, 215) with { EventDate = new DateOnly(2026, 5, 12) });

        var closest = ClosestVote.From(release)!;

        Assert.Equal(new DateOnly(2026, 5, 5), closest.WindowStart);
        Assert.Equal(new DateOnly(2026, 5, 11), closest.WindowEnd);
        Assert.Equal(Id('b'), closest.Entry.RecordId);
        Assert.Equal(1, closest.VotesInWindow);
    }

    [Fact]
    public void AVoteDatedOnTheReleaseDayCountsAndOneDatedTheDayAfterDoesNot()
    {
        var release = ReleaseOf(Vote('a', 216, 215) with { EventDate = new DateOnly(2026, 5, 11) }, Vote('b', 100, 100) with { EventDate = new DateOnly(2026, 5, 12) });

        var closest = ClosestVote.From(release)!;

        Assert.Equal(Id('a'), closest.Entry.RecordId);
        Assert.Equal(1, closest.VotesInWindow);
    }

    [Fact]
    public void AVoteWithNoAlignmentIsNeverTheClosestVoteBecauseProceduralVotesAndSpeakerElectionsAreOut()
    {
        var release = ReleaseOf(Vote('a', 100, 100) with { Alignment = null }, Vote('b', 0, 0) with { YeaCount = null, NayCount = null, Alignment = null }, Vote('c', 220, 200));

        var closest = ClosestVote.From(release)!;

        Assert.Equal(Id('c'), closest.Entry.RecordId);
        Assert.Equal(1, closest.VotesInWindow);
    }

    [Fact]
    public void WithNoVoteInTheWindowThereIsNoClosestVote()
    {
        Assert.Null(ClosestVote.From(ReleaseOf(Vote('a', 216, 215) with { EventDate = new DateOnly(2026, 1, 1) })));
    }

    private static string Id(char letter) => new(letter, 32);

    private static PublicationRelease ReleaseOf(params PublishedRecordEntry[] entries) => new()
    {
        SchemaVersion = PublicationProtocol.SchemaVersion,
        ReleaseNumber = 3,
        PublishedAtUtc = Published,
        Withdrawals = [],
        Records = entries
    };

    private static PublishedRecordEntry Vote(char id, int yea, int nay) => new()
    {
        Kind = PublishedRecordKind.Vote,
        RecordId = Id(id),
        RevisionNumber = 1,
        Headline = $"Headline {id}",
        FirstPublishedAtUtc = Published,
        Officials = [],
        IssueIds = [],
        PolicyAreaIds = [],
        YeaCount = yea,
        NayCount = nay,
        Alignment = PublishedVoteAlignment.Split,
        EventDate = new DateOnly(2026, 5, 7)
    };
}
