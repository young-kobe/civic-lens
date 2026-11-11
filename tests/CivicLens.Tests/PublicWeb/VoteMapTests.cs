using AngleSharp.Dom;
using CivicLens.Publication.Contracts;
using CivicLens.PublicWeb.Publication;
using static CivicLens.Tests.PublicWeb.VoteDetailPageFixture;

namespace CivicLens.Tests.PublicWeb;

public sealed class VoteMapTests
{
    private static VoteMemberData Member(string id, string name, string cast, string party, string state, int? district) =>
        new(id, name, cast, party, Cite(Lines[4]), state, district);

    private static VotePageData House(params VoteMemberData[] members) => Vote() with { Members = members };

    private static VotePageData Senate(params VoteMemberData[] members) => Vote() with { Chamber = PublishedChamber.Senate, Members = members };

    private static IElement Seat(IDocument page, string view, string name) =>
        page.QuerySelectorAll($".vote-map-{view} .vote-map-seat").Single(seat => seat.QuerySelector("title")!.TextContent.StartsWith(name + ":", StringComparison.Ordinal));

    [Fact]
    public async Task ASeatWithNoMemberRowIsNeverLabeledAsACast()
    {
        var page = await RenderAsync(House(Member("o1", "Ada Able", "Aye", "Democrat", "MA", 7)));

        foreach (var view in new[] { "shapes", "tiles" })
        {
            var empty = Seat(page, view, "Texas district 23");
            Assert.Equal("no-member", empty.GetAttribute("data-mark"));
            Assert.Equal("Texas district 23: No member in the record", empty.QuerySelector("title")!.TextContent);
            Assert.Equal("yes", Seat(page, view, "Massachusetts district 7").GetAttribute("data-mark"));
        }
        var row = page.QuerySelectorAll(".vote-map tbody tr").Single(item => item.Children[0].TextContent == "Texas district 23");
        Assert.Equal(["No member in the record", "", ""], row.Children.Skip(1).Select(cell => cell.TextContent));
        Assert.Equal(["1 Yes", "434 No member in the record"], page.QuerySelectorAll(".vote-map-legend li").Select(item => item.TextContent));
        Assert.DoesNotContain("acant", page.QuerySelector(".vote-map")!.TextContent);
    }

    [Fact]
    public async Task ASenateStateSplitsInTwoInThePositionOrderOfTheVoteMembersInBothViews()
    {
        var page = await RenderAsync(Senate(
            Member("s1", "Ed Ward", "Nay", "Democrat", "MA", null),
            Member("s2", "Bea Lee", "Yea", "Democrat", "MA", null),
            Member("s3", "Cal Ray", "Not Voting", "Republican", "TX", null)));

        var shapes = page.QuerySelectorAll(".vote-map-shapes .vote-map-seat").Where(seat => seat.QuerySelector("title")!.TextContent.StartsWith("Massachusetts:", StringComparison.Ordinal)).ToArray();
        Assert.Equal(["Massachusetts: Ed Ward, Democrat, No (Nay)", "Massachusetts: Bea Lee, Democrat, Yes (Yea)"], shapes.Select(seat => seat.QuerySelector("title")!.TextContent));
        Assert.Equal(["url(#vote-map-clip-ma-0)", "url(#vote-map-clip-ma-1)"], shapes.Select(seat => seat.GetAttribute("clip-path")));
        var tiles = page.QuerySelectorAll(".vote-map-tiles .vote-map-seat").Where(seat => seat.QuerySelector("title")!.TextContent.StartsWith("Massachusetts:", StringComparison.Ordinal)).ToArray();
        Assert.Equal(["no", "yes"], tiles.Select(seat => seat.GetAttribute("data-mark")));
        Assert.True(double.Parse(tiles[0].GetAttribute("x")!, System.Globalization.CultureInfo.InvariantCulture) < double.Parse(tiles[1].GetAttribute("x")!, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(["not-voting", "no-member"], page.QuerySelectorAll(".vote-map-tiles .vote-map-seat")
            .Where(seat => seat.QuerySelector("title")!.TextContent.StartsWith("Texas:", StringComparison.Ordinal)).Select(seat => seat.GetAttribute("data-mark")));
        Assert.Equal(100, page.QuerySelectorAll(".vote-map-tiles .vote-map-seat").Length);
    }

    [Fact]
    public async Task DelegatesAreCirclesBelowTheMapAndNeverInTheSeatGrid()
    {
        var page = await RenderAsync(House(
            Member("o1", "Ada Able", "Aye", "Democrat", "MA", 7),
            Member("d1", "Dee Cee", "Present", "Democrat", "DC", 0),
            Member("d2", "Pat Rico", "Nay", "Republican", "PR", 0)));

        Assert.Equal(435, page.QuerySelectorAll(".vote-map-shapes .vote-map-seat").Length);
        Assert.Equal(435, page.QuerySelectorAll(".vote-map-tiles .vote-map-seat").Length);
        Assert.DoesNotContain(page.QuerySelectorAll(".vote-map-frame title"), title => title.TextContent.Contains("Dee Cee") || title.TextContent.Contains("Pat Rico"));
        Assert.Equal(["District of Columbia, Delegate: Dee Cee, Democrat, Present", "Puerto Rico, Resident Commissioner: Pat Rico, Republican, No (Nay)"],
            page.QuerySelectorAll(".vote-map-delegates li").Select(item => item.TextContent));
        Assert.Equal(["present", "no"], page.QuerySelectorAll(".vote-map-delegates circle").Select(circle => circle.GetAttribute("data-mark")));
        Assert.True(Position(page, ".vote-map-delegates") > Position(page, ".vote-map-frame"));
    }

    [Fact]
    public async Task TheLegendListsEveryPatternInUseWithCountsThatAddUpToEverySeatAndCircle()
    {
        var page = await RenderAsync(House(
            Member("o1", "Ada Able", "Aye", "Democrat", "MA", 7),
            Member("o2", "Ben Baker", "Nay", "Republican", "TX", 1),
            Member("o3", "Cy Cole", "Not Voting", "Republican", "TX", 2),
            Member("o4", "Di Dole", "Smith", "Republican", "TX", 3),
            Member("d1", "Dee Cee", "Present", "Democrat", "DC", 0)));

        var used = page.QuerySelectorAll(".vote-map-frame .vote-map-seat, .vote-map-delegates .vote-map-seat").Select(seat => seat.GetAttribute("data-mark")).Distinct().Order();
        var legend = page.QuerySelectorAll(".vote-map-legend .vote-map-seat").Select(seat => seat.GetAttribute("data-mark")).ToArray();
        Assert.Equal(used, legend.Order());
        Assert.Equal(["1 Yes", "1 No", "1 Present", "1 Not voting", "1 Other", "431 No member in the record"],
            page.QuerySelectorAll(".vote-map-legend li").Select(item => item.TextContent));
        Assert.All(legend, mark => Assert.NotNull(page.QuerySelector(mark is "yes" or "no-member" ? ".vote-map" : $"#vote-map-{mark}")));
    }

    [Fact]
    public async Task TheMapStaysOffWhenItCannotShowTheVoteTruly()
    {
        var otherCongress = House(Member("o1", "Ada Able", "Aye", "Democrat", "MA", 7)) with { Congress = 118 };
        var twoOnOneSeat = House(Member("o1", "Ada Able", "Aye", "Democrat", "MA", 7), Member("o2", "Ben Baker", "Nay", "Democrat", "MA", 7));
        var noShape = House(Member("o1", "Ada Able", "Aye", "Democrat", "MA", 40));
        var threeSenators = Senate(Member("s1", "A", "Yea", "Democrat", "MA", null), Member("s2", "B", "Yea", "Democrat", "MA", null), Member("s3", "C", "Yea", "Democrat", "MA", null));

        foreach (var vote in new[] { otherCongress, twoOnOneSeat, noShape, threeSenators, Vote() })
            Assert.Null((await RenderAsync(vote)).QuerySelector(".vote-map"));
    }

    [Fact]
    public async Task AMemberWithNoSeatInTheReleaseStaysInTheTableNotOnTheMap()
    {
        var page = await RenderAsync(House(Member("o1", "Ada Able", "Aye", "Democrat", "MA", 7), Vote().Members[1]));

        Assert.Contains("1 tracked member has no seat in the record, so the map leaves them out.", page.QuerySelector(".vote-map")!.TextContent);
        Assert.Equal(2, page.QuerySelectorAll(".vote-members tbody tr").Length);
    }

    [Fact]
    public void TheTilesPutStatesInNameOrderEachInASquareGrid()
    {
        var map = VoteMapData.Of(House(Member("o1", "Ada Able", "Aye", "Democrat", "MA", 7)))!;

        Assert.Equal(["AL", "AK", "AZ", "AR", "CA"], map.Tiles.Labels.Take(5).Select(label => label.State));
        var california = map.Seats.Select((seat, index) => (seat, tile: map.Tiles.Tiles[index])).Where(item => item.seat.State == "CA").ToArray();
        Assert.Equal(52, california.Length);
        Assert.Equal(8, california.Select(item => item.tile.X).Distinct().Count());
        Assert.Equal(7, california.Select(item => item.tile.Y).Distinct().Count());
        Assert.Equal(Enumerable.Range(1, 52).Cast<int?>(), california.Select(item => item.seat.District));
    }

    private static int Position(IDocument page, string selector) =>
        page.Body!.InnerHtml.IndexOf(page.QuerySelector(selector)!.OuterHtml, StringComparison.Ordinal);
}
