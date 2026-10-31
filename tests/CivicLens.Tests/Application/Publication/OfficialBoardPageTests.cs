using CivicLens.Application.Publication;
using CivicLens.Publication.Contracts;

namespace CivicLens.Tests.Application.Publication;

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
        Assert.Equal((1, 1, 1), (page.WithoutVotes, page.InEarlierCongress, page.WithoutMajorParty));
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
        Assert.Equal((120, 1, 1), (house.Congress, house.InEarlierCongress, house.Members.Length));
        Assert.Equal((119, 1, 1), (senate.Congress, senate.InEarlierCongress, senate.Members.Length));
    }

    [Fact]
    public void RankedMembersWithTheSameShareOrderByCountThenNameSoTheOrderNeverDependsOnInputOrder()
    {
        var page = PageOf(Row("b", 119, 50, 5), Row("a", 119, 50, 5), Row("c", 119, 100, 10), Row("d", 119, 25, 2));

        Assert.Equal(["c", "a", "b", "d"], page.Breakers.Select(member => member.Official.Profile.Id));
        Assert.Equal(["d", "c", "a", "b"], page.Loyalists.Select(member => member.Official.Profile.Id));
    }

    [Fact]
    public void MissedVotesWithTheSameCountOrderByName()
    {
        var page = PageOf(Row("b", 119, 30, 3, missed: 4), Row("a", 119, 30, 3, missed: 4), Row("c", 119, 30, 3, missed: 7));

        Assert.Equal(["c", "a", "b"], page.MostMissed.Select(member => member.Official.Profile.Id));
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

    private static OfficialListing Listing(string id) => new(new PublishedOfficialProfile { Id = id, Name = id, Terms = [] }, 0);

    private static (OfficialListing, PublishedOfficialSummary) Row(string id, int congress, int cast, int breaks,
        PublishedChamber chamber = PublishedChamber.House, string party = PublishedPartyMajority.Democrat, int missed = 1, int held = 100) =>
        (Listing(id), new PublishedOfficialSummary
        {
            Id = id,
            Congress = congress,
            CosponsoredBills = null,
            PartyVotes = [new PublishedPartyVoteSummary { Chamber = chamber, Party = party, SplitVotesCast = cast, Breaks = breaks }],
            Attendance = [new PublishedAttendanceSummary { Chamber = chamber, Held = held, Missed = missed, First = new DateOnly(2025, 1, 3), Last = new DateOnly(2026, 10, 27) }]
        });
}
