using AngleSharp.Dom;
using CivicLens.Publication.Contracts;
using CivicLens.PublicWeb.Publication;
using CivicLens.Tests.Fixtures;
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
        Assert.Equal(["No member in the record", "", "", ""], row.Children.Skip(1).Select(cell => cell.TextContent));
        Assert.Equal(["1 Yes", "434 No member in the record"], page.QuerySelectorAll(".vote-map-legend-vote li").Select(item => item.TextContent));
        Assert.DoesNotContain("acant", page.QuerySelector(".vote-map")!.TextContent);
    }

    [Fact]
    public async Task TheVotePageMapReusesTheMapPageColorChoiceSoItCanColorByParty()
    {
        var page = await RenderAsync(House(Member("o1", "Ada Able", "Aye", "Democrat", "MA", 7)));

        Assert.Equal([VoteMapWords.ByVote, VoteMapWords.ByParty], page.QuerySelectorAll(".vote-map [data-vote-map-colors] button").Select(button => button.TextContent));
        Assert.Equal(["Democrats: 1 Yes", "434 No member in the record"], page.QuerySelectorAll(".vote-map-legend-party li").Select(item => item.TextContent));
    }

    [Fact]
    public async Task PartyBreaksOverlayOnlyKnownBreaksInShapesTilesAndDelegates()
    {
        var page = await RenderAsync(House(
            Member("o1", "Ada Able", "Aye", "Democrat", "MA", 7) with { BreaksParty = true },
            Member("o2", "Ben Baker", "Nay", "Democrat", "TX", 1) with { BreaksParty = false },
            Member("d1", "Dee Cee", "Aye", "Democrat", "DC", 0) with { BreaksParty = true },
            Member("d2", "Pat Rico", "Nay", "Republican", "PR", 0)));

        Assert.Equal(3, page.QuerySelectorAll(".vote-map-frame .vote-map-party-break-halo, .vote-map-delegates .vote-map-party-break-halo").Length);
        Assert.Equal(3, page.QuerySelectorAll(".vote-map-frame .vote-map-party-break-ring, .vote-map-delegates .vote-map-party-break-ring").Length);
        Assert.Equal(1, page.QuerySelectorAll(".vote-map-shapes .vote-map-party-break-ring").Length);
        Assert.Equal(1, page.QuerySelectorAll(".vote-map-tiles .vote-map-party-break-ring").Length);
        Assert.Equal(1, page.QuerySelectorAll(".vote-map-delegates .vote-map-party-break-ring").Length);
        Assert.Contains("Party line unavailable", page.QuerySelectorAll(".vote-map-delegates li").Single(item => item.TextContent.Contains("Pat Rico", StringComparison.Ordinal)).TextContent);
        Assert.DoesNotContain("Party line unavailable", Seat(page, "shapes", "Texas district 23").QuerySelector("title")!.TextContent);

        var rows = page.QuerySelectorAll(".vote-map tbody tr");
        string PartyLine(string name) => rows.Single(row => row.Children[1].TextContent == name).Children[4].TextContent;
        Assert.Equal("Voted against most Democrats", PartyLine("Ada Able"));
        Assert.Equal("Voted with most Democrats", PartyLine("Ben Baker"));
        Assert.Equal("Party line unavailable", PartyLine("Pat Rico"));
        Assert.Equal("", rows.Single(row => row.Children[0].TextContent == "Texas district 23").Children[4].TextContent);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [InlineData(null)]
    public async Task CompactMapKeepsItsTitleAndRendersNoPartyBreakControlOrRings(bool? breaksParty)
    {
        var member = Member("o1", "Ada Able", "Aye", "Democrat", "MA", 7) with { BreaksParty = breaksParty };
        var map = VoteMapData.Of(House(member))!;
        var page = await ListPageRender.RenderPageAsync<VoteMap>(new Dictionary<string, object?>
        {
            [nameof(VoteMap.Data)] = map,
            [nameof(VoteMap.RollCall)] = 308,
            [nameof(VoteMap.RootPath)] = Root,
            [nameof(VoteMap.Compact)] = true,
            [nameof(VoteMap.ColorModes)] = true
        });

        Assert.Null(page.QuerySelector("[data-party-breaks], [data-party-breaks-key], .vote-map-party-break-halo, .vote-map-party-break-ring"));
        var shapes = page.QuerySelector(".vote-map-shapes")!;
        Assert.Equal("img", shapes.GetAttribute("role"));
        Assert.Equal("false", shapes.GetAttribute("focusable"));
        Assert.Null(shapes.QuerySelector("a"));
        var seat = Seat(page, "shapes", "Massachusetts district 7");
        Assert.False(seat.HasAttribute("fill-opacity"));
        Assert.Equal("Massachusetts district 7: Ada Able, Democrat, Yes (Aye)", seat.QuerySelector("title")!.TextContent);
    }

    [Fact]
    public async Task ASenateStateSplitsInTwoInThePositionOrderOfTheVoteMembersInBothViews()
    {
        var page = await RenderAsync(Senate(
            Member("s1", "Ed Ward", "Nay", "Democrat", "MA", null) with { BreaksParty = true },
            Member("s2", "Bea Lee", "Yea", "Democrat", "MA", null),
            Member("s3", "Cal Ray", "Not Voting", "Republican", "TX", null)));

        var shapes = page.QuerySelectorAll(".vote-map-shapes .vote-map-seat").Where(seat => seat.QuerySelector("title")!.TextContent.StartsWith("Massachusetts:", StringComparison.Ordinal)).ToArray();
        Assert.Equal(["Massachusetts: Ed Ward, Democrat, No (Nay). Voted against most Democrats", "Massachusetts: Bea Lee, Democrat, Yes (Yea). Party line unavailable"], shapes.Select(seat => seat.QuerySelector("title")!.TextContent));
        Assert.Equal(["url(#vote-map-clip-ma-0)", "url(#vote-map-clip-ma-1)"], shapes.Select(seat => seat.GetAttribute("clip-path")));
        Assert.Equal("url(#vote-map-clip-ma-0)", page.QuerySelector(".vote-map-shapes .vote-map-party-break-ring")!.GetAttribute("clip-path"));
        var tiles = page.QuerySelectorAll(".vote-map-tiles .vote-map-seat").Where(seat => seat.QuerySelector("title")!.TextContent.StartsWith("Massachusetts:", StringComparison.Ordinal)).ToArray();
        Assert.Equal(["no", "yes"], tiles.Select(seat => seat.GetAttribute("data-mark")));
        Assert.True(double.Parse(tiles[0].GetAttribute("x")!, System.Globalization.CultureInfo.InvariantCulture) < double.Parse(tiles[1].GetAttribute("x")!, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(["not-voting", "no-member"], page.QuerySelectorAll(".vote-map-tiles .vote-map-seat")
            .Where(seat => seat.QuerySelector("title")!.TextContent.StartsWith("Texas:", StringComparison.Ordinal)).Select(seat => seat.GetAttribute("data-mark")));
        Assert.Equal(100, page.QuerySelectorAll(".vote-map-tiles .vote-map-seat").Length);
    }

    [Fact]
    public async Task ALinkedSeatKeepsItsGeometryAndLabelAlongsideAnUnlinkedSeatInBothViews()
    {
        var tiles = VoteMapTiles.Place([("MA", 2)]);
        MapFrameSeat[] seats =
        [
            new("MA", null, 0, tiles.Tiles[0], "custom", null, "Ada & Bea", Href: "/officials/ada.html"),
            new("MA", null, 1, tiles.Tiles[1], "unknown", null, "Unavailable")
        ];
        var page = await ListPageRender.RenderPageAsync<MapFrame>(new Dictionary<string, object?>
        {
            [nameof(MapFrame.IdPrefix)] = "test-map",
            [nameof(MapFrame.Seats)] = seats,
            [nameof(MapFrame.SplitStates)] = new[] { VoteMapGeometry.States["MA"] },
            [nameof(MapFrame.ShapesViewBox)] = VoteMapGeometry.ViewBox,
            [nameof(MapFrame.ShapesLabel)] = "Shapes",
            [nameof(MapFrame.TileLayout)] = tiles,
            [nameof(MapFrame.TilesLabel)] = "Tiles"
        });

        var views = page.QuerySelectorAll(".vote-map-frame > svg");
        Assert.Equal(2, views.Length);
        foreach (var view in views)
        {
            Assert.Equal("group", view.GetAttribute("role"));
            var link = Assert.Single(view.QuerySelectorAll("a"));
            Assert.Equal("/officials/ada.html", link.GetAttribute("href"));
            Assert.Equal("Ada & Bea", link.GetAttribute("aria-label"));
            Assert.Equal("Ada & Bea", link.QuerySelector("title")!.TextContent);
            Assert.Equal("custom", link.QuerySelector(".vote-map-seat")!.GetAttribute("data-mark"));
            Assert.Same(view, view.QuerySelector(".vote-map-seat[data-mark=unknown]")!.ParentElement);
        }
        Assert.Equal("url(#test-map-clip-ma-0)", page.QuerySelector(".vote-map-shapes a use")!.GetAttribute("clip-path"));
        Assert.NotNull(page.GetElementById("test-map-clip-ma-0"));
        Assert.NotNull(page.QuerySelector(".vote-map-tiles a rect"));
    }

    [Fact]
    public async Task ASeatWithGeometryInOnlyOneViewAddsNoMarkOrLinkToTheOtherView()
    {
        var tiles = VoteMapTiles.Place([("MA", 1)]);
        MapFrameSeat[] seats =
        [
            new("VT", "M0 0H10V10H0Z", 0, null, "unavailable", null, "Unavailable", Emphasized: true, FillOpacity: 0.375),
            new("MA", null, 0, tiles.Tiles[0], "percentage", null, "Ada Able", Emphasized: true, Href: "/officials/ada.html",
                DrawShape: false, FillOpacity: 0)
        ];
        var page = await ListPageRender.RenderPageAsync<MapFrame>(new Dictionary<string, object?>
        {
            [nameof(MapFrame.IdPrefix)] = "test-map",
            [nameof(MapFrame.Seats)] = seats,
            [nameof(MapFrame.ShapesViewBox)] = "0 0 10 10",
            [nameof(MapFrame.ShapesLabel)] = "Shapes",
            [nameof(MapFrame.TileLayout)] = tiles,
            [nameof(MapFrame.TilesLabel)] = "Tiles"
        });

        var shapes = page.QuerySelector(".vote-map-shapes")!;
        Assert.Equal("img", shapes.GetAttribute("role"));
        Assert.Null(shapes.QuerySelector("a, use"));
        var shapeMark = Assert.Single(shapes.QuerySelectorAll(".vote-map-seat"));
        Assert.Equal("unavailable", shapeMark.GetAttribute("data-mark"));
        Assert.Equal("0.375", shapeMark.GetAttribute("fill-opacity"));
        Assert.Single(shapes.QuerySelectorAll("path.vote-map-party-break-ring"));

        var tileView = page.QuerySelector(".vote-map-tiles")!;
        Assert.Equal("group", tileView.GetAttribute("role"));
        var tileMark = Assert.Single(tileView.QuerySelectorAll(".vote-map-seat"));
        Assert.Equal("percentage", tileMark.GetAttribute("data-mark"));
        Assert.Equal("0", tileMark.GetAttribute("fill-opacity"));
        Assert.Equal("/officials/ada.html", tileMark.ParentElement!.GetAttribute("href"));
        Assert.Single(tileView.QuerySelectorAll("rect.vote-map-party-break-ring"));
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
        Assert.Equal(["District of Columbia, Delegate: Dee Cee, Democrat, Present. Party line unavailable", "Puerto Rico, Resident Commissioner: Pat Rico, Republican, No (Nay). Party line unavailable"],
            page.QuerySelectorAll(".vote-map-delegates li > span").Select(item => item.TextContent));
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
        var legend = page.QuerySelectorAll(".vote-map-legend-vote .vote-map-seat").Select(seat => seat.GetAttribute("data-mark")).ToArray();
        Assert.Equal(used, legend.Order());
        Assert.Equal(["1 Yes", "1 No", "1 Present", "1 Not voting", "1 Other", "431 No member in the record"],
            page.QuerySelectorAll(".vote-map-legend-vote li").Select(item => item.TextContent));
        var css = WebStyleSheets.Read("public.css");
        Assert.All(legend, mark => Assert.Matches($@"\.vote-map-seat\[data-mark=""{mark}""\] \{{ fill: [^;]+;", css));
        Assert.Equal("Each mark counts House seats and delegate circles on roll call 308 by how the member voted. The counts add up to 436.",
            page.QuerySelector(".vote-map-legend-vote .vote-map-legend")!.PreviousElementSibling!.TextContent);
        Assert.True(Position(page, ".vote-map-legend-vote") < Position(page, ".vote-map-frame"));
    }

    [Fact]
    public async Task TheViewButtonsHaveAVisibleLabelThatNamesTheGroup()
    {
        var page = await RenderAsync(House(Member("o1", "Ada Able", "Aye", "Democrat", "MA", 7)));

        var group = page.QuerySelector("[data-vote-map-views]")!;
        var label = page.GetElementById(group.GetAttribute("aria-labelledby")!)!;
        Assert.Equal(VoteMapWords.ViewGroup, label.TextContent);
        Assert.Same(group, label.ParentElement);
        Assert.Null(group.GetAttribute("aria-label"));
    }

    [Fact]
    public async Task TheTileNoteTalksAboutDistrictsSoOnlyAHouseMapShowsIt()
    {
        var house = await RenderAsync(House(Member("o1", "Ada Able", "Aye", "Democrat", "MA", 7)));
        var senate = await RenderAsync(Senate(Member("s1", "Ed Ward", "Nay", "Democrat", "MA", null)));

        Assert.NotNull(house.QuerySelector(".vote-map-tile-note"));
        Assert.Equal(VoteMapWords.TileNote, house.QuerySelector(".vote-map-tile-note")!.TextContent);
        Assert.Null(senate.QuerySelector(".vote-map-tile-note"));
    }

    [Fact]
    public async Task TheSeatTableShowsTheCastLikeTheMemberTable()
    {
        var page = await RenderAsync(House(Member("o1", "Ada Able", "Aye", "Democrat", "MA", 7)));

        Assert.Equal("yea", page.QuerySelector(".vote-map tbody td[data-label=Vote] .shared-vote-cast-value")!.GetAttribute("data-side"));
    }

    [Fact]
    public async Task TheMapStaysOffWhenItCannotShowTheVoteTruly()
    {
        var otherCongress = House(Member("o1", "Ada Able", "Aye", "Democrat", "MA", 7)) with { Congress = 118 };
        var twoOnOneSeat = House(Member("o1", "Ada Able", "Aye", "Democrat", "MA", 7), Member("o2", "Ben Baker", "Nay", "Democrat", "MA", 7));
        var noShape = House(Member("o1", "Ada Able", "Aye", "Democrat", "MA", 40));
        var noDistrict = House(Member("o1", "Ada Able", "Aye", "Democrat", "MA", null));
        var noStateShape = Senate(Member("s1", "A", "Yea", "Democrat", "ZZ", null));
        var threeSenators = Senate(Member("s1", "A", "Yea", "Democrat", "MA", null), Member("s2", "B", "Yea", "Democrat", "MA", null), Member("s3", "C", "Yea", "Democrat", "MA", null));

        foreach (var vote in new[] { otherCongress, twoOnOneSeat, noShape, noDistrict, noStateShape, threeSenators, Vote() })
            Assert.Null((await RenderAsync(vote)).QuerySelector(".vote-map"));
    }

    [Fact]
    public async Task TheSeatTableCaptionNamesTheChamberAndTheRollCallOfTheVote()
    {
        var vote = House(Member("o1", "Ada Able", "Aye", "Democrat", "MA", 7));
        var page = await RenderAsync(vote);

        Assert.Equal($"How each House seat voted on roll call {vote.RollCall}", page.QuerySelector(".vote-map caption")!.TextContent.Trim());
    }

    [Fact]
    public async Task AMemberWithNoSeatInTheReleaseStaysInTheTableNotOnTheMap()
    {
        var page = await RenderAsync(House(Member("o1", "Ada Able", "Aye", "Democrat", "MA", 7), Vote().Members[1]));

        Assert.Contains("1 tracked member has no seat in the record, so the map leaves them out.", page.QuerySelector(".vote-map")!.TextContent);
        Assert.Equal(2, page.QuerySelectorAll(".vote-members tbody tr").Length);
    }

    [Fact]
    public async Task AHouseMemberWithAStateAndNoDistrictCountsAsUnplacedAndTheLegendTotalLeavesThemOut()
    {
        var page = await RenderAsync(House(Member("o1", "Ada Able", "Aye", "Democrat", "MA", 7), Member("o2", "Ben Baker", "Nay", "Democrat", "MA", null)));

        var map = page.QuerySelector(".vote-map")!;
        Assert.Contains("1 tracked member has no seat in the record, so the map leaves them out.", map.TextContent);
        Assert.Equal(["1 Yes", "434 No member in the record"], page.QuerySelectorAll(".vote-map-legend-vote li").Select(item => item.TextContent));
        Assert.EndsWith("The counts add up to 435.", page.QuerySelector(".vote-map-legend-vote .vote-map-legend")!.PreviousElementSibling!.TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain(page.QuerySelectorAll(".vote-map-frame title"), title => title.TextContent.Contains("Ben Baker"));
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

    [Fact]
    public void TheTilesLayOutForTheNarrowestFrameSoAPhoneDrawsTheLabelsAtElevenPixelsOrMore()
    {
        const double frameAtThreeHundredSixtyPixels = 360 - 2 * 16 - 2 * 24;
        var css = WebStyleSheets.Read("public.css");
        var cap = Pixels(css, @"\.vote-map-tiles \{ max-width: (\d+)px; \}");
        var label = Pixels(css, @"\.vote-map-tile-label \{[^}]*font-size: (\d+)px;");

        Assert.True(cap >= VoteMapTiles.Width, "The cap must not shrink the tiles below their layout size.");
        Assert.True(label * Math.Min(frameAtThreeHundredSixtyPixels, cap) / VoteMapTiles.Width >= 11, "A tile label renders below 11 px at 360 px.");
    }

    private static double Pixels(string css, string pattern)
    {
        var match = System.Text.RegularExpressions.Regex.Match(css, pattern);
        Assert.True(match.Success, $"No rule matches {pattern}.");
        return double.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
    }

    [Fact]
    public void EachSenateStateSplitsInHalvesThatHoldHalfOfItsArea()
    {
        Assert.NotEmpty(VoteMapGeometry.States);
        foreach (var state in VoteMapGeometry.States.Values)
        {
            var rings = VoteMapArea.Rings(state.Path);
            var share = VoteMapArea.LeftOf(rings, state.Middle) / VoteMapArea.Total(rings);
            Assert.InRange(share, 0.495, 0.505);
        }
    }

    [Fact]
    public void TheHalvingLineOfATriangleSitsWhereTheLeftPartHoldsHalfTheArea()
    {
        var triangle = VoteMapArea.Rings("M0 0 10 0 0 10Z");

        Assert.Equal(10 * (1 - Math.Sqrt(0.5)), VoteMapArea.HalvingX(triangle, 0, 10), 3);
    }

    private static int Position(IDocument page, string selector) =>
        page.Body!.InnerHtml.IndexOf(page.QuerySelector(selector)!.OuterHtml, StringComparison.Ordinal);
}
