using AngleSharp.Dom;
using CivicLens.Publication.Contracts;
using CivicLens.PublicWeb.Publication;

namespace CivicLens.Tests.PublicWeb;

public sealed class MapsPageTests
{
    [Fact]
    public async Task FractionsKeepObservedZeroEmptyBasesAndMissingEvidenceDistinctInShapesAndTable()
    {
        var data = Data(Member("zero", "Zero", new(0, 10)), Member("empty", "Empty", new(0, 0)), Member("missing", "Missing", null));
        var page = await RenderAsync(data);

        Assert.Equal(["measure", "empty-base", "unavailable"], page.QuerySelectorAll(".vote-map-shapes .vote-map-seat").Select(seat => seat.GetAttribute("data-mark")));
        Assert.Equal("0.15", page.QuerySelector(".vote-map-shapes [data-mark=measure]")!.GetAttribute("fill-opacity"));
        var rows = page.QuerySelectorAll("#maps-table tbody tr");
        Assert.Equal("0 of 10", rows[0].LastElementChild!.TextContent);
        Assert.Contains("0 of 0", rows[1].LastElementChild!.TextContent);
        Assert.Contains(MapsWords.EmptyBase, rows[1].LastElementChild!.TextContent);
        Assert.Equal(MapsWords.Unknown, rows[2].LastElementChild!.TextContent);
        Assert.All(page.QuerySelectorAll(".vote-map-frame a"), link => Assert.StartsWith(ListPageRender.Root + "officials/", link.GetAttribute("href")));
    }

    [Fact]
    public async Task TheHistoricalHouseMapDrawsOnlyKnownSeatsWithMatchingCongressGeometry()
    {
        var known = Member("a", "Ada", new(4, 5));
        var unmatched = Member("b", "Bea", new(3, 5));
        var data = Data(known, unmatched) with
        {
            Congress = 118,
            AsOf = new(2025, 1, 2),
            Seats = [new("AL", 2, null, known.OfficialId), new("AL", 40, null, unmatched.OfficialId)],
            Filter = RecordListFilter.Of(null, null, ("congress", "118"), ("chamber", "house"), ("layer", "attendance"))
        };
        var page = await RenderAsync(data);
        var geometry = VoteMapGeometry.ForHouseCongress(118)!;
        var shapes = page.QuerySelector(".vote-map-shapes")!;
        var path = Assert.Single(shapes.QuerySelectorAll(".vote-map-seat"));

        Assert.Equal(geometry.ViewBox, shapes.GetAttribute("viewBox"));
        Assert.Equal(geometry.Districts[("AL", 2)], path.GetAttribute("d"));
        Assert.NotEqual(VoteMapGeometry.ForHouseCongress(119)!.Districts[("AL", 2)], path.GetAttribute("d"));
        Assert.Equal(ListPageRender.Root + known.PagePath, path.ParentElement!.GetAttribute("href"));
        Assert.Contains("Census Bureau 2023 cartographic boundary file for the 118th Congress", page.QuerySelector(".vote-map-sources dd")!.TextContent);
        Assert.Equal(2, page.QuerySelectorAll(".vote-map-tiles a").Length);
        Assert.Equal(2, page.QuerySelectorAll("#maps-table tbody tr").Length);
    }

    [Fact]
    public async Task AnOlderHouseCongressKeepsTilesAndTheEquivalentTableWithoutBorrowedDistrictShapes()
    {
        var page = await RenderAsync(Data(Member("a", "Ada", new(4, 5))) with { Congress = 117 });
        Assert.Null(page.QuerySelector(".vote-map-shapes"));
        Assert.Single(page.QuerySelectorAll(".vote-map-tiles a"));
        Assert.Contains(MapsWords.NoGeometry, page.Body!.TextContent);
        Assert.Single(page.QuerySelectorAll("#maps-table tbody tr"));
        Assert.Equal("tile", page.QuerySelector("[data-maps]")!.GetAttribute("data-view"));
    }

    [Fact]
    public async Task OneKnownSenateClassHasATileWhileUnknownClassesRemainOnlyInTheNamedTable()
    {
        var known = Member("known", "Known", new(1, 2));
        var missing = Member("missing", "Legacy", new(2, 3)) with { HasSeat = false };
        var data = Data(known, missing) with { Chamber = PublishedChamber.Senate, Seats = [new("MA", null, 2, known.OfficialId)] };
        var page = await RenderAsync(data);
        Assert.Empty(page.QuerySelectorAll(".vote-map-shapes a"));
        Assert.Single(page.QuerySelectorAll(".vote-map-tiles a"));
        Assert.Equal(2, page.QuerySelectorAll("#maps-table tbody tr").Length);
        Assert.Contains(MapsWords.UnknownSeat, page.QuerySelectorAll("#maps-table tbody tr")[1].TextContent);
        Assert.DoesNotContain("Legacy", page.QuerySelector(".vote-map-frame")!.TextContent);
    }

    [Fact]
    public async Task ClassOrderingKeepsSenateHalvesStableWhenTheInputOrderChanges()
    {
        var a = Member("a", "Ada", new(2, 3));
        var b = Member("b", "Bea", new(3, 4));
        var data = Data(a, b) with { Chamber = PublishedChamber.Senate, Seats = [new("MA", null, 2, b.OfficialId), new("MA", null, 1, a.OfficialId)] };
        var page = await RenderAsync(data);
        Assert.Equal([ListPageRender.Root + a.PagePath, ListPageRender.Root + b.PagePath], page.QuerySelectorAll(".vote-map-shapes a").Select(link => link.GetAttribute("href")));
        Assert.Equal(["url(#maps-clip-ma-0)", "url(#maps-clip-ma-1)"], page.QuerySelectorAll(".vote-map-shapes a use").Select(shape => shape.GetAttribute("clip-path")));
    }

    [Fact]
    public async Task ActivityShowsBothWholeCongressFractionsAndUsesOnlyTheChosenMeasureForFill()
    {
        var member = Member("a", "Ada", null) with { Bills = new(1, 4), Votes = new(3, 4) };
        var data = Data(member) with { Layer = "issueActivity", Measure = "votes", IssueName = "Health", Filter = RecordListFilter.Of(null, null, ("issue", "health")) };
        var page = await RenderAsync(data);
        Assert.Equal("0.7875", page.QuerySelector(".vote-map-shapes [data-mark=measure]")!.GetAttribute("fill-opacity"));
        Assert.Equal(["1 of 4", "3 of 4"], page.QuerySelectorAll("#maps-table tbody tr")[0].Children.Skip(3).Select(cell => cell.TextContent));
        Assert.Contains("across both chambers", page.Body!.TextContent);
        var url = page.QuerySelector("#maps-table .table-data-link a")?.GetAttribute("href") ?? page.QuerySelectorAll("#maps-table a").Last().GetAttribute("href");
        Assert.Contains("/v1/maps?", url);
        Assert.Contains("issue=health", url);
    }

    [Fact]
    public async Task ExactTermDatesHighlightOnlyTheSelectedSavedDate()
    {
        var date = new DateOnly(2027, 1, 3);
        var a = Member("a", "Ada", null) with { TermEndsOn = date };
        var b = Member("b", "Bea", null) with { TermEndsOn = date.AddYears(2) };
        var data = Data(a, b) with { Layer = "termEnds", SelectedEnd = date, EndDates = [date, date.AddYears(2)] };
        var page = await RenderAsync(data);
        Assert.Equal(["term-selected", "term-other"], page.QuerySelectorAll(".vote-map-shapes .vote-map-seat").Select(seat => seat.GetAttribute("data-mark")));
        Assert.Equal(["3 Jan 2027", "3 Jan 2029"], page.QuerySelectorAll("#maps-table tbody tr").Select(row => row.LastElementChild!.TextContent));
    }

    [Fact]
    public async Task ARefusedChoiceStaysEncodedAndVisibleWithoutAMapOrMisleadingEmptyState()
    {
        var data = Data() with { Layer = "attendance", Filter = RecordListFilter.Refused("measure", ("measure", "<script>bad</script>")) };
        var page = await RenderAsync(data);
        Assert.Equal("true", page.QuerySelector("#maps-measure")!.GetAttribute("aria-invalid"));
        Assert.Equal("<script>bad</script>", page.QuerySelector("#maps-measure option[selected]")!.TextContent);
        Assert.Null(page.QuerySelector("[data-maps]"));
        Assert.DoesNotContain(MapsWords.Empty, page.Body!.TextContent);
    }

    private static MapMemberData Member(string id, string name, MapFraction? attendance) =>
        new(id, name, PublicationProtocol.OfficialPagePath(id), "Representative for Massachusetts", "Democrat", null, attendance, null, null, true);

    private static MapsPageData Data(params MapMemberData[] members) => new(1, 119, 119, PublishedChamber.House, "attendance", new(2026, 11, 17), null,
        members, [.. members.Select((member, index) => new MapSeatData("MA", index + 1, null, member.OfficialId))], [],
        RecordListFilter.Of(null, null, ("congress", "119"), ("chamber", "house"), ("layer", "attendance")), "bills", null);

    private static Task<IDocument> RenderAsync(MapsPageData data) => ListPageRender.RenderPageAsync<MapsPage>(new Dictionary<string, object?>
    {
        [nameof(MapsPage.Data)] = data,
        [nameof(MapsPage.RootPath)] = ListPageRender.Root,
        [nameof(MapsPage.BrowserApiUrl)] = "https://api.civic-lens.test"
    });
}
