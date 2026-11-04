using CivicLens.Application.Publication;
using CivicLens.Publication.Contracts;

namespace CivicLens.Tests.Application.Publication;

public sealed class HomeHighlightsTests
{
    private static readonly DateTimeOffset Published = new(2026, 5, 11, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TheNewestVotesComeNewestFirstAndOnlyTheFourNewestStayWhateverTheCountsOrAlignment()
    {
        var release = ReleaseOf(Vote('a', 1, 2) with { EventDate = new DateOnly(2026, 5, 1) }, Vote('b', 1, 2) with { EventDate = new DateOnly(2026, 5, 5) },
            Vote('c', 1, 2) with { EventDate = new DateOnly(2026, 5, 3), Alignment = null }, Vote('d', 0, 0) with { YeaCount = null, NayCount = null, Alignment = null, EventDate = new DateOnly(2026, 5, 4) },
            Vote('e', 1, 2) with { EventDate = new DateOnly(2026, 5, 2) }, Executive('f', 28));

        var highlights = HomeHighlights.From(release, [], 0);

        Assert.Equal([Id('b'), Id('d'), Id('c'), Id('e')], highlights.Votes.Select(entry => entry.RecordId));
    }

    [Fact]
    public void TheNewestExecutiveActionsComeNewestFirstAndOnlyTheThreeNewestStay()
    {
        var release = ReleaseOf(Executive('a', 1), Executive('b', 4), Executive('c', 2), Executive('d', 3), Vote('e', 1, 2));

        var highlights = HomeHighlights.From(release, [], 0);

        Assert.Equal([Id('b'), Id('d'), Id('c')], highlights.ExecutiveActions.Select(entry => entry.RecordId));
    }

    [Fact]
    public void TheOfficialCountIsTheCountThatThePublishGives()
    {
        Assert.Equal(541, HomeHighlights.From(ReleaseOf(), [], 541).OfficialCount);
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
            (leader.Member.Official.Profile.Id, leader.Chamber, leader.Party, leader.Member.Cast, leader.Member.Breaks));
    }

    [Fact]
    public void TheLeaderKeepsTheCongressOfItsBoardAndLinksThePageOfThatCongressWhenTheBoardKeepsAnEarlierOne()
    {
        var moved = Summary("moved", 120, PublishedChamber.House, PublishedPartyMajority.Democrat, 5, 1);
        var earlier = Summary("moved", 119, PublishedChamber.House, PublishedPartyMajority.Democrat, 40, 10);
        moved = moved with { Previous = new PublishedVoteFigures { Congress = 119, PartyVotes = earlier.PartyVotes, Attendance = earlier.Attendance } };
        var stayed = Summary("stayed", 119, PublishedChamber.House, PublishedPartyMajority.Democrat, 30, 3);

        var kept = Assert.Single(BreakLeader.All(OfficialBoardPage.All([Listing("moved"), Listing("stayed")], [moved, stayed])));
        var current = Assert.Single(BreakLeader.All(OfficialBoardPage.All([Listing("stayed")], [stayed])));

        Assert.Equal(("moved", 119, "officials/moved/119.html"), (kept.Member.Official.Profile.Id, kept.Congress, kept.Member.PagePath));
        Assert.Equal((119, "officials/stayed.html"), (current.Congress, current.Member.PagePath));
    }

    [Fact]
    public void AMemberWithExactlyTheMinimumOfSplitVotesCanLead()
    {
        var officials = new[] { Listing("exact") };
        var summaries = new[] { Summary("exact", 119, PublishedChamber.Senate, PublishedPartyMajority.Republican, OfficialBoardPage.MinimumSplitVotes, 1) };

        var leader = Assert.Single(BreakLeader.All(OfficialBoardPage.All(officials, summaries)));

        Assert.Equal(("exact", OfficialBoardPage.MinimumSplitVotes), (leader.Member.Official.Profile.Id, leader.Member.Cast));
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

        Assert.Equal("b", leader.Member.Official.Profile.Id);
        Assert.Equal(boards.Single(page => page.Board.Chamber == PublishedChamber.House && page.Board.Party == PublishedPartyMajority.Democrat).Breakers[0], leader.Member);
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

        Assert.Equal([("senator", PublishedChamber.Senate), ("newer", PublishedChamber.House)],
            leaders.Select(leader => (leader.Member.Official.Profile.Id, leader.Chamber)));
    }

    [Fact]
    public void BreakLeadersComeHighestShareFirstThenMoreBreaksThenLowerOfficialIdSoTheCardOrderNeverDependsOnBoardOrder()
    {
        var officials = new[] { Listing("low"), Listing("big"), Listing("z-small"), Listing("a-small") };
        var summaries = new[]
        {
            Summary("low", 119, PublishedChamber.House, PublishedPartyMajority.Democrat, 100, 10),
            Summary("big", 119, PublishedChamber.House, PublishedPartyMajority.Republican, 200, 40),
            Summary("z-small", 119, PublishedChamber.Senate, PublishedPartyMajority.Democrat, 100, 20),
            Summary("a-small", 119, PublishedChamber.Senate, PublishedPartyMajority.Republican, 100, 20)
        };

        var leaders = BreakLeader.All(OfficialBoardPage.All(officials, summaries));

        Assert.Equal(["big", "a-small", "z-small", "low"], leaders.Select(leader => leader.Member.Official.Profile.Id));
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
