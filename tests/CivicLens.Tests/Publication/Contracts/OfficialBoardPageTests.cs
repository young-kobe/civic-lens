using CivicLens.Publication.Contracts;

namespace CivicLens.Tests.Publication.Contracts;

public sealed class OfficialBoardPageTests
{
    private static readonly OfficialBoard HouseDemocrats = new(PublishedChamber.House, PublishedPartyMajority.Democrat);

    [Fact]
    public void AListRanksOnlyMembersWithEnoughSplitVotesAndNamesTheOthersSoAShortRecordNeverWinsByChance()
    {
        var page = PageOf(
            Row("few", 119, 24, 12),
            Row("enough", 119, 25, 5),
            Row("most", 119, 100, 30));

        Assert.Equal(["most", "enough"], page.Breakers.Select(member => member.Official.Profile.Id));
        Assert.Equal(["enough", "most"], page.Loyalists.Select(member => member.Official.Profile.Id));
        Assert.Equal(["few"], page.TooFewSplitVotes.Select(member => member.Official.Profile.Id));
    }

    [Fact]
    public void ABoardHoldsOnlyMembersOfItsChamberAndPartyAndNeverMixesTwoParties()
    {
        var page = PageOf(
            Row("house-democrat", 119, 30, 3),
            Row("house-republican", 119, 30, 3, PublishedChamber.House, PublishedPartyMajority.Republican),
            Row("senate-democrat", 119, 30, 3, PublishedChamber.Senate));

        Assert.Equal(["house-democrat"], page.Members.Select(member => member.Official.Profile.Id));
    }

    [Fact]
    public void AnOfficialWithNoCongressOrAnEarlierCongressIsOnNoBoardAndIsCountedAsLeftOut()
    {
        var page = PageOf(
            Row("current", 119, 30, 3),
            Row("earlier", 118, 30, 3),
            (Listing("none"), PublishedOfficialSummary.Empty("none")),
            (Listing("no-party"), new PublishedOfficialSummary { Id = "no-party", Congress = 119, CosponsoredBills = null, PartyVotes = [], Attendance = [new PublishedAttendanceSummary { Chamber = PublishedChamber.House, Held = 10, Missed = 0, First = new DateOnly(2025, 1, 3), Last = new DateOnly(2026, 10, 27) }] }));

        Assert.Equal(119, page.Congress);
        Assert.Equal(["current"], page.Members.Select(member => member.Official.Profile.Id));
        Assert.Equal((1, 1, 1), (page.WithoutVotes, page.NotInCongress, page.WithoutMajorParty));
    }

    [Fact]
    public void TheMissedListOrdersByMissedVotesAndCarriesTheHeldCountAndDatesOfTheMember()
    {
        var page = PageOf(Row("a", 119, 30, 3, missed: 2, held: 90), Row("b", 119, 30, 3, missed: 9, held: 40));

        Assert.Equal(["b", "a"], page.MostMissed.Select(member => member.Official.Profile.Id));
        Assert.Equal((9, 40, new DateOnly(2025, 1, 3), new DateOnly(2026, 10, 27)),
            (page.MostMissed[0].Missed!.Missed, page.MostMissed[0].Missed!.Held, page.MostMissed[0].Missed!.First, page.MostMissed[0].Missed!.Last));
        Assert.Equal(new DateOnly(2026, 10, 27), page.NewestRollCall);
    }

    [Fact]
    public void EachChamberUsesItsOwnNewestCongressSoAHouseAtOneCongressNeverHidesASenateAtAnother()
    {
        var rows = new[]
        {
            Row("house-new", 120, 30, 3), Row("house-old", 119, 30, 3),
            Row("senate", 119, 30, 3, PublishedChamber.Senate), Row("senate-old", 118, 30, 3, PublishedChamber.Senate)
        };
        var pages = OfficialBoardPage.All([.. rows.Select(row => row.Item1)], [.. rows.Select(row => row.Item2)]);

        var house = pages.Single(page => page.Board == HouseDemocrats);
        var senate = pages.Single(page => page.Board == new OfficialBoard(PublishedChamber.Senate, PublishedPartyMajority.Democrat));
        Assert.Equal((120, 1, 1), (house.Congress, house.NotInCongress, house.Members.Length));
        Assert.Equal((119, 1, 1), (senate.Congress, senate.NotInCongress, senate.Members.Length));
    }

    [Fact]
    public void RankedMembersWithTheSameShareOrderByCountThenNameSoTheOrderNeverDependsOnInputOrder()
    {
        var page = PageOf(Row("b", 119, 50, 5, name: "Adams"), Row("a", 119, 50, 5, name: "Zed"), Row("c", 119, 100, 10), Row("d", 119, 25, 2));

        Assert.Equal(["c", "b", "a", "d"], page.Breakers.Select(member => member.Official.Profile.Id));
        Assert.Equal(["d", "c", "b", "a"], page.Loyalists.Select(member => member.Official.Profile.Id));
    }

    [Fact]
    public void MissedVotesWithTheSameCountOrderByName()
    {
        var page = PageOf(Row("b", 119, 30, 3, missed: 4), Row("a", 119, 30, 3, missed: 4), Row("c", 119, 30, 3, missed: 7));

        Assert.Equal(["c", "a", "b"], page.MostMissed.Select(member => member.Official.Profile.Id));
    }

    [Fact]
    public void AMemberWhoChangedPartyShowsTheSameMissedCountOnBothBoardsBecauseAttendanceHasNoParty()
    {
        var switcher = new PublishedOfficialSummary
        {
            Id = "switcher",
            Congress = 119,
            CosponsoredBills = null,
            PartyVotes =
            [
                new PublishedPartyVoteSummary { Chamber = PublishedChamber.House, Party = PublishedPartyMajority.Democrat, SplitVotesCast = 20, Breaks = 1 },
                new PublishedPartyVoteSummary { Chamber = PublishedChamber.House, Party = PublishedPartyMajority.Republican, SplitVotesCast = 30, Breaks = 2 }
            ],
            Attendance = [new PublishedAttendanceSummary { Chamber = PublishedChamber.House, Held = 100, Missed = 7, First = new DateOnly(2025, 1, 3), Last = new DateOnly(2026, 10, 27) }]
        };

        var pages = OfficialBoardPage.All([Listing("switcher")], [switcher]);

        var missed = pages.Where(page => page.Board.Chamber == PublishedChamber.House).Select(page => Assert.Single(page.MostMissed).Missed).ToList();
        Assert.Equal(2, missed.Count);
        Assert.Equal(missed[0], missed[1]);
        Assert.Equal(7, missed[0]!.Missed);
    }

    [Fact]
    public void AnOfficialWithOnlyAttendanceInANewerCongressNeverMovesTheBoardBecauseNoMemberThereHasEnoughSplitVotes()
    {
        var attendanceOnly = new PublishedOfficialSummary
        {
            Id = "attendance",
            Congress = 120,
            CosponsoredBills = null,
            PartyVotes = [],
            Attendance = [new PublishedAttendanceSummary { Chamber = PublishedChamber.House, Held = 10, Missed = 1, First = new DateOnly(2027, 1, 3), Last = new DateOnly(2027, 1, 20) }]
        };
        var (voter, voterSummary) = Row("voter", 119, 30, 3);

        var page = OfficialBoardPage.All([Listing("attendance"), voter], [attendanceOnly, voterSummary]).Single(page => page.Board == HouseDemocrats);

        Assert.Equal(119, page.Congress);
        Assert.Equal(["voter"], page.Members.Select(member => member.Official.Profile.Id));
        Assert.Equal(1, page.NotInCongress);
    }

    [Fact]
    public void ABoardKeepsTheEarlierCongressWhileNoMemberOfTheNewCongressHasEnoughSplitVotes()
    {
        var movedOn = Row("moved-on", 120, 24, 3, previous: Previous(119, 60, 6));
        var stayed = Row("stayed", 119, 40, 4);

        var page = PageOf(movedOn, stayed);

        Assert.Equal(119, page.Congress);
        Assert.Equal(["moved-on", "stayed"], page.Breakers.Select(member => member.Official.Profile.Id).Order());
        Assert.Equal(60, page.Members.Single(member => member.Official.Profile.Id == "moved-on").Cast);
        Assert.Equal(0, page.NotInCongress);
        Assert.Equal(120, page.NewerCongress);
        Assert.Equal(["officials/moved-on/119.html", "officials/stayed.html"], page.Members.OrderBy(member => member.Official.Profile.Id).Select(member => member.PagePath));
    }

    [Fact]
    public void ABoardMovesToTheNewCongressWhenOneMemberReachesTheMinimumSplitVotes()
    {
        var below = Row("below", 120, MinimumMinusOne, 3, previous: Previous(119, 60, 6));
        var reached = Row("reached", 120, OfficialBoardPage.MinimumSplitVotes, 3, previous: Previous(119, 60, 6));

        Assert.Equal(119, PageOf(below).Congress);
        var page = PageOf(below, reached);

        Assert.Equal(120, page.Congress);
        Assert.Equal(["below", "reached"], page.Members.Select(member => member.Official.Profile.Id));
        Assert.Null(page.NewerCongress);
        Assert.All(page.Members, member => Assert.Equal(member.Official.PagePath, member.PagePath));
    }

    [Fact]
    public void AMemberOfTheOtherPartyReachingTheMinimumMovesBothPagesOfTheChamberToTheNewCongress()
    {
        var democrat = Row("democrat", 120, 5, 1, previous: Previous(119, 60, 6));
        var republican = Row("republican", 120, OfficialBoardPage.MinimumSplitVotes, 3, party: PublishedPartyMajority.Republican,
            previous: Previous(119, 60, 6, PublishedPartyMajority.Republican));

        var pages = OfficialBoardPage.All([democrat.Item1, republican.Item1], [democrat.Item2, republican.Item2])
            .Where(page => page.Board.Chamber == PublishedChamber.House).ToList();

        Assert.Equal(2, pages.Count);
        Assert.All(pages, page => Assert.Equal(120, page.Congress));
    }

    [Fact]
    public void WhenOnlyANewcomerHoldsTheNewCongressAndNoRowHasPreviousFiguresTheBoardKeepsTheCongressOfTheOthers()
    {
        var page = PageOf(Row("newcomer", 120, 5, 1), Row("veteran", 119, 60, 6));

        Assert.Equal(119, page.Congress);
        Assert.Equal(["veteran"], page.Members.Select(member => member.Official.Profile.Id));
        Assert.Equal(1, page.NotInCongress);
    }

    [Fact]
    public void AMemberNewToTheChamberHasNoFiguresInTheKeptCongressAndIsCountedAsLeftOut()
    {
        var page = PageOf(Row("moved-on", 120, 5, 1, previous: Previous(119, 60, 6)), Row("newcomer", 120, 5, 1));

        Assert.Equal(119, page.Congress);
        Assert.Equal(["moved-on"], page.Members.Select(member => member.Official.Profile.Id));
        Assert.Equal(1, page.NotInCongress);
    }

    [Fact]
    public void AReleaseWithNoOfficialsStillGivesFourEmptyBoardsWithNoCongressAndNoDate()
    {
        var pages = OfficialBoardPage.All([], []);

        Assert.Equal(4, pages.Length);
        Assert.All(pages, page => Assert.Null(page.Congress));
        Assert.All(pages, page => Assert.Null(page.NewestRollCall));
    }

    private static OfficialBoardPage PageOf(params (OfficialListing, PublishedOfficialSummary)[] rows) =>
        OfficialBoardPage.All([.. rows.Select(row => row.Item1)], [.. rows.Select(row => row.Item2)]).Single(page => page.Board == HouseDemocrats);

    private static OfficialListing Listing(string id, string? name = null) => new(new PublishedOfficialProfile { Id = id, Name = name ?? id, Terms = [] }, 0);

    private static (OfficialListing, PublishedOfficialSummary) Row(string id, int congress, int cast, int breaks,
        PublishedChamber chamber = PublishedChamber.House, string party = PublishedPartyMajority.Democrat, int missed = 1, int held = 100, string? name = null, PublishedVoteFigures? previous = null) =>
        (Listing(id, name), new PublishedOfficialSummary
        {
            Id = id,
            Congress = congress,
            CosponsoredBills = null,
            PartyVotes = [new PublishedPartyVoteSummary { Chamber = chamber, Party = party, SplitVotesCast = cast, Breaks = breaks }],
            Attendance = [new PublishedAttendanceSummary { Chamber = chamber, Held = held, Missed = missed, First = new DateOnly(2025, 1, 3), Last = new DateOnly(2026, 10, 27) }],
            Previous = previous
        });

    private const int MinimumMinusOne = OfficialBoardPage.MinimumSplitVotes - 1;

    private static PublishedVoteFigures Previous(int congress, int cast, int breaks, string party = PublishedPartyMajority.Democrat) => new()
    {
        Congress = congress,
        PartyVotes = [new PublishedPartyVoteSummary { Chamber = PublishedChamber.House, Party = party, SplitVotesCast = cast, Breaks = breaks }],
        Attendance = [new PublishedAttendanceSummary { Chamber = PublishedChamber.House, Held = 100, Missed = 1, First = new DateOnly(2023, 1, 3), Last = new DateOnly(2024, 12, 20) }]
    };
}
