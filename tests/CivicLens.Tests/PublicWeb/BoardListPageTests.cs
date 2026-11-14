using CivicLens.PublicWeb.Publication;
using CivicLens.Publication.Contracts;

namespace CivicLens.Tests.PublicWeb;

public sealed class BoardListPageTests
{
    private static BoardRowData Row(string id, string name, int cast, int breaks) => new(PublicationProtocol.OfficialPagePath(id), name, null, cast, breaks);

    private static BoardListPageData Data(PublishedChamber chamber, string party, params BoardRowData[] rows) =>
        new(7, new(chamber, party, 119, null, new DateOnly(2026, 9, 16), rows.Length > 0, rows));

    private static Task<AngleSharp.Dom.IDocument> RenderAsync(BoardListPageData data) =>
        ListPageRender.RenderAsync<BoardListPage>(data, nameof(BoardListPage.Data));

    [Fact]
    public async Task ThePageShowsEveryRowOfTheBoardInTheOrderOfTheReaderWithNoLinkToItself()
    {
        var rows = Enumerable.Range(0, 12).Select(index => Row($"m{index}", $"Member {index}", 30, 20 - index)).ToArray();

        var page = await RenderAsync(Data(PublishedChamber.House, "Democrat", rows));

        Assert.Equal(rows.Select(row => row.Name), page.QuerySelectorAll(".board-panel .list-row-title").Select(link => link.TextContent));
        Assert.Equal("The 12 members of the House Democrats with the highest share of split votes cast against most Democrats. Count first, then the base it comes from.",
            page.QuerySelector(".board-lede")!.TextContent);
        Assert.DoesNotContain(page.QuerySelectorAll("a"), link => link.TextContent.StartsWith("See the full list", StringComparison.Ordinal));
        Assert.Equal("The newest roll call counted here, in the 119th Congress, is on 16 Sep 2026. No count includes a later day.",
            page.QuerySelector(".board-panel > p.muted")!.TextContent);
    }

    [Fact]
    public async Task TheTitleAndTrailNameTheBoardAndTheTrailLinksTheBoardsPage()
    {
        var page = await RenderAsync(Data(PublishedChamber.Senate, "Republican", Row("a", "Ann", 30, 5)));

        Assert.Equal("Senate Republicans: voted against most of their party", page.QuerySelector("h1")!.TextContent);
        var trail = page.QuerySelector("main nav[aria-label=Breadcrumb]")!;
        Assert.Equal(["Home", "Compare parties"], trail.QuerySelectorAll("a").Select(link => link.TextContent));
        Assert.Equal(ListPageRender.Root + PublicationProtocol.BoardsPagePath, trail.QuerySelectorAll("a")[1].GetAttribute("href"));
        Assert.Equal("Senate Republicans: voted against most of their party", trail.QuerySelector("[aria-current=page]")!.TextContent);
    }

    [Fact]
    public async Task EachTabLinksTheFullBoardAndOnlyTheTabOfThisBoardIsCurrent()
    {
        var page = await RenderAsync(Data(PublishedChamber.House, "Republican", Row("a", "Ann", 30, 5)));

        var tabs = page.QuerySelectorAll(".segmented a");
        Assert.Equal(["House Democrats", "House Republicans", "Senate Democrats", "Senate Republicans"], tabs.Select(tab => tab.TextContent.Trim()));
        Assert.Equal(new[] { "board-house-democrat.html", "board-house-republican.html", "board-senate-democrat.html", "board-senate-republican.html" }.Select(path => ListPageRender.Root + path),
            tabs.Select(tab => tab.GetAttribute("href")));
        Assert.Equal([null, "true", null, null], tabs.Select(tab => tab.GetAttribute("aria-current")));
    }
}
