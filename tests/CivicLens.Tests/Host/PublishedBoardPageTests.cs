using AngleSharp.Html.Parser;
using CivicLens.Application.Publication;
using CivicLens.Host.Publication;
using CivicLens.Publication.Contracts;

namespace CivicLens.Tests.Host;

public sealed class PublishedBoardPageTests
{
    private static readonly OfficialBoard Board = new(PublishedChamber.House, PublishedPartyMajority.Democrat);

    [Fact]
    public async Task EveryCountNamesWhoAndWhatAndShowsItsTotalSoNoNumberStandsAlone()
    {
        var document = await RenderAsync(Page(Member("Ada Rep", 120, 14, missed: 12, held: 400)));

        var text = document.QuerySelector("main")!.TextContent;
        Assert.Contains("14 of 120 split votes (12%)", text);
        Assert.Contains("106 of 120 split votes (88%)", text);
        Assert.Contains("Missed 12 of the 400 roll call votes of the House of Representatives on this site (3%)", text);
        Assert.Contains("Voted against most House Democrats", text);
        Assert.Contains("Voted with most House Democrats", text);
    }

    [Fact]
    public async Task AMemberBelowTheMinimumIsNamedWithTheCountAndGetsNoRank()
    {
        var document = await RenderAsync(Page(Member("Short Record", 24, 12, missed: 1, held: 100), Member("Long Record", 25, 5, missed: 1, held: 100)));

        var tables = document.QuerySelectorAll("table").Where(table => table.GetAttribute("data-default-order")!.Contains("share")).ToList();
        Assert.All(tables, table => Assert.DoesNotContain("Short Record", table.TextContent));
        Assert.All(tables, table => Assert.Contains("Long Record", table.TextContent));
        var tooFew = document.QuerySelector("[aria-labelledby='panel-too-few-split-votes-yet']")!.TextContent;
        Assert.Contains("Short Record", tooFew);
        Assert.Contains("24 split votes cast", tooFew);
    }

    [Fact]
    public async Task TheSplitVoteTablesSortByTheShareBehindTheCellAndNameTheirDefaultOrder()
    {
        var document = await RenderAsync(Page(Member("Ada Rep", 100, 10, missed: 1, held: 100), Member("Bo Rep", 100, 40, missed: 1, held: 100)));

        var breakers = document.QuerySelector("[aria-labelledby='panel-voted-against-most-house-democrats']")!;
        Assert.Equal(["Bo Rep", "Ada Rep"], breakers.QuerySelectorAll("tbody th a, tbody td a").Select(link => link.TextContent));
        Assert.Equal(["0.4000", "0.1000"], breakers.QuerySelectorAll("tbody td[data-sort-value]").Select(cell => cell.GetAttribute("data-sort-value")));
        Assert.Contains("Highest share first", breakers.QuerySelector("caption")!.TextContent);
        Assert.Equal(["Official", "Share of split votes against most House Democrats"], breakers.QuerySelectorAll("thead th").Select(header => header.TextContent));
    }

    [Fact]
    public async Task APageWithNoCongressSaysSoAndStillListsWhatItLeavesOut()
    {
        var document = await RenderAsync(new OfficialBoardPage(Board, null, [], 8, 0, 0));

        Assert.Empty(document.QuerySelectorAll("table"));
        var text = document.QuerySelector("main")!.TextContent;
        Assert.Contains("No vote figures are in this release yet.", text);
        Assert.Contains("8 officials with no vote figures in this release", document.QuerySelector("[aria-labelledby='panel-what-this-page-leaves-out']")!.TextContent);
        Assert.DoesNotContain("no roll call votes", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TheTabsLinkEveryBoardAndMarkOnlyTheCurrentOne()
    {
        var document = await RenderAsync(Page());

        var tabs = document.QuerySelectorAll("nav[aria-label^='Show the voting boards'] a").ToList();
        Assert.Equal(["board-house-democrat.html", "board-house-republican.html", "board-senate-democrat.html", "board-senate-republican.html"],
            tabs.Select(tab => tab.GetAttribute("href")));
        Assert.Equal(["House Democrats"], tabs.Where(tab => tab.GetAttribute("aria-current") is not null).Select(tab => tab.TextContent.Trim()));
    }

    private static async Task<AngleSharp.Dom.IDocument> RenderAsync(OfficialBoardPage page) =>
        new HtmlParser().ParseDocument(await new ReleaseRenderer(null, null).RenderBoardAsync(page, CancellationToken.None));

    private static OfficialBoardPage Page(params BoardMember[] members) => new(Board, 119, [.. members], 0, 0, 0);

    private static BoardMember Member(string name, int cast, int breaks, int missed, int held) =>
        new(new OfficialListing(new PublishedOfficialProfile { Id = name.ToLowerInvariant().Replace(' ', '-'), Name = name, Terms = [] }, 0), cast, breaks,
            new MissedVotes(PublishedChamber.House, missed, held, new DateOnly(2025, 1, 3), new DateOnly(2026, 10, 27)));

    [Theory]
    [InlineData(1, 400, "1 of 400 split votes (under 1%)")]
    [InlineData(0, 400, "0 of 400 split votes (0%)")]
    [InlineData(399, 400, "399 of 400 split votes (over 99%)")]
    [InlineData(400, 400, "400 of 400 split votes (100%)")]
    [InlineData(14, 120, "14 of 120 split votes (12%)")]
    public async Task ASplitVoteCountNeverRoundsANonzeroCountToZeroOrANonTotalToAHundredPercent(int breaks, int cast, string expected)
    {
        var document = await RenderAsync(Page(Member("Ada Rep", cast, breaks, missed: 1, held: 100)));

        var cell = document.QuerySelector("[aria-labelledby='panel-voted-against-most-house-democrats'] tbody td:last-child")!;
        Assert.Equal(expected, cell.TextContent.Trim());
    }
}
