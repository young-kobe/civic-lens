using CivicLens.Application.Publication;
using CivicLens.Publication.Contracts;

namespace CivicLens.Tests.Application.Publication;

public sealed class HomeHighlightsTests
{
    private static readonly DateTimeOffset Published = new(2026, 5, 11, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TheClosestVoteHasTheSmallestGapBetweenItsYeaAndNayCountsAndATieGoesToTheLowerRecordId()
    {
        var release = ReleaseOf(Vote('b', 216, 214), Vote('a', 215, 217), Vote('c', 300, 120));

        var highlights = HomeHighlights.From(release, []);

        Assert.Equal(Id('a'), highlights.ClosestVote!.RecordId);
        Assert.Equal(3, highlights.VotesInWindow);
    }

    [Fact]
    public void OnlyVotesDatedInTheSevenDaysThatEndOnTheReleaseDayCountSoARecessGivesNoStaleCard()
    {
        var release = ReleaseOf(Vote('a', 216, 215) with { EventDate = new DateOnly(2026, 5, 4) }, Vote('b', 300, 100) with { EventDate = new DateOnly(2026, 5, 5) },
            Vote('c', 216, 215) with { EventDate = new DateOnly(2026, 5, 12) });

        var highlights = HomeHighlights.From(release, []);

        Assert.Equal(new DateOnly(2026, 5, 5), highlights.WindowStart);
        Assert.Equal(new DateOnly(2026, 5, 11), highlights.WindowEnd);
        Assert.Equal(Id('b'), highlights.ClosestVote!.RecordId);
        Assert.Equal(1, highlights.VotesInWindow);
    }

    [Fact]
    public void AVoteDatedOnTheReleaseDayCountsAndOneDatedTheDayAfterDoesNot()
    {
        var release = ReleaseOf(Vote('a', 216, 215) with { EventDate = new DateOnly(2026, 5, 11) }, Vote('b', 100, 100) with { EventDate = new DateOnly(2026, 5, 12) });

        var highlights = HomeHighlights.From(release, []);

        Assert.Equal(Id('a'), highlights.ClosestVote!.RecordId);
        Assert.Equal(1, highlights.VotesInWindow);
    }

    [Fact]
    public void AVoteWithNoAlignmentIsNeverTheClosestVoteBecauseProceduralVotesAndSpeakerElectionsAreOut()
    {
        var release = ReleaseOf(Vote('a', 100, 100) with { Alignment = null }, Vote('b', 0, 0) with { YeaCount = null, NayCount = null, Alignment = null }, Vote('c', 220, 200));

        var highlights = HomeHighlights.From(release, []);

        Assert.Equal(Id('c'), highlights.ClosestVote!.RecordId);
        Assert.Equal(1, highlights.VotesInWindow);
    }

    [Fact]
    public void WithNoVoteInTheWindowThereIsNoClosestVote()
    {
        var highlights = HomeHighlights.From(ReleaseOf(Vote('a', 216, 215) with { EventDate = new DateOnly(2026, 1, 1) }), []);

        Assert.Null(highlights.ClosestVote);
        Assert.Equal(0, highlights.VotesInWindow);
    }

    [Fact]
    public void TheNewestExecutiveActionsComeNewestFirstAndTheTotalCountsAllOfThem()
    {
        var release = ReleaseOf(Executive('a', 1), Executive('b', 4), Executive('c', 2), Executive('d', 3), Vote('e', 1, 2));

        var highlights = HomeHighlights.From(release, []);

        Assert.Equal([Id('b'), Id('d'), Id('c')], highlights.ExecutiveActions.Select(entry => entry.RecordId));
        Assert.Equal(4, highlights.ExecutiveActionTotal);
    }

    [Fact]
    public void TheBreakLeaderHasTheHighestShareNotTheHighestCountAndNeedsTheMinimumOfSplitVotes()
    {
        var officials = new[] { Listing("long"), Listing("share"), Listing("short") };
        var summaries = new[]
        {
            Summary("long", 119, PublishedChamber.House, PublishedPartyMajority.Democrat, 100, 20),
            Summary("share", 119, PublishedChamber.House, PublishedPartyMajority.Democrat, 40, 10),
            Summary("short", 119, PublishedChamber.House, PublishedPartyMajority.Democrat, OfficialBoardPage.MinimumSplitVotes - 1, OfficialBoardPage.MinimumSplitVotes - 1)
        };

        var leader = Assert.Single(BreakLeader.All(OfficialBoardPage.All(officials, summaries)));

        Assert.Equal(("share", PublishedChamber.House, PublishedPartyMajority.Democrat, 40, 10),
            (leader.Official.Profile.Id, leader.Chamber, leader.Party, leader.Cast, leader.Breaks));
    }

    [Fact]
    public void AMemberWithExactlyTheMinimumOfSplitVotesCanLead()
    {
        var officials = new[] { Listing("exact") };
        var summaries = new[] { Summary("exact", 119, PublishedChamber.Senate, PublishedPartyMajority.Republican, OfficialBoardPage.MinimumSplitVotes, 1) };

        var leader = Assert.Single(BreakLeader.All(OfficialBoardPage.All(officials, summaries)));

        Assert.Equal(("exact", OfficialBoardPage.MinimumSplitVotes), (leader.Official.Profile.Id, leader.Cast));
    }

    [Fact]
    public void ATieOnShareAndOnBreaksGoesToTheFirstOfficialNameLikeTheBoardListNotTheLowerId()
    {
        var officials = new[] { Listing("a", "Zed"), Listing("b", "Adams") };
        var summaries = new[]
        {
            Summary("a", 119, PublishedChamber.House, PublishedPartyMajority.Democrat, 40, 10),
            Summary("b", 119, PublishedChamber.House, PublishedPartyMajority.Democrat, 40, 10)
        };
        var boards = OfficialBoardPage.All(officials, summaries);

        var leader = Assert.Single(BreakLeader.All(boards));

        Assert.Equal("b", leader.Official.Profile.Id);
        Assert.Equal(boards.Single(page => page.Board.Chamber == PublishedChamber.House && page.Board.Party == PublishedPartyMajority.Democrat).Breakers[0].Official, leader.Official);
    }

    [Fact]
    public void BreakLeadersComeFromTheNewestCongressOfTheChamberAndAnOfficialWithNoBreakIsNeverNamed()
    {
        var officials = new[] { Listing("older"), Listing("newer"), Listing("loyal"), Listing("senator") };
        var summaries = new[]
        {
            Summary("older", 118, PublishedChamber.House, PublishedPartyMajority.Republican, 90, 80),
            Summary("newer", 119, PublishedChamber.House, PublishedPartyMajority.Republican, 30, 3),
            Summary("loyal", 119, PublishedChamber.House, PublishedPartyMajority.Democrat, 60, 0),
            Summary("senator", 118, PublishedChamber.Senate, PublishedPartyMajority.Republican, 50, 5)
        };

        var leaders = BreakLeader.All(OfficialBoardPage.All(officials, summaries));

        Assert.Equal([("newer", PublishedChamber.House), ("senator", PublishedChamber.Senate)],
            leaders.Select(leader => (leader.Official.Profile.Id, leader.Chamber)));
    }

    [Fact]
    public void AnOfficialWithNoVoteFiguresNamesNoLeader()
    {
        Assert.Empty(BreakLeader.All(OfficialBoardPage.All([Listing("none")], [PublishedOfficialSummary.Empty("none")])));
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

    private static PublishedRecordEntry Vote(char id, int yea, int nay) => Entry(PublishedRecordKind.Vote, id) with
    {
        YeaCount = yea,
        NayCount = nay,
        Alignment = PublishedVoteAlignment.Split,
        EventDate = new DateOnly(2026, 5, 7)
    };

    private static PublishedRecordEntry Executive(char id, int day) => Entry(PublishedRecordKind.ExecutiveAction, id) with
    {
        DocumentType = PublishedExecutiveActionKind.Proclamation,
        EventDate = new DateOnly(2026, 4, day)
    };

    private static PublishedRecordEntry Entry(PublishedRecordKind kind, char id) => new()
    {
        Kind = kind,
        RecordId = Id(id),
        RevisionNumber = 1,
        Headline = $"Headline {id}",
        FirstPublishedAtUtc = Published,
        Officials = [],
        IssueIds = [],
        PolicyAreaIds = []
    };

    private static OfficialListing Listing(string id, string? name = null) => new(new PublishedOfficialProfile { Id = id, Name = name ?? id, Terms = [] }, 0);

    private static PublishedOfficialSummary Summary(string id, int congress, PublishedChamber chamber, string party, int cast, int breaks) => new()
    {
        Id = id,
        Congress = congress,
        CosponsoredBills = null,
        PartyVotes = [new() { Chamber = chamber, Party = party, SplitVotesCast = cast, Breaks = breaks }],
        Attendance = []
    };
}
