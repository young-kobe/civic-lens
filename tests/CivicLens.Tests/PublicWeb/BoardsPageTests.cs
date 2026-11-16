using CivicLens.PublicWeb.Publication;
using CivicLens.Publication.Contracts;

namespace CivicLens.Tests.PublicWeb;

public sealed class BoardsPageTests
{
    private static BoardPanelData Panel(PublishedChamber chamber, string party, int? congress, params BoardRowData[] rows) =>
        new(chamber, party, congress, null, rows.Length == 0 ? null : new DateOnly(2026, 9, 16), rows.Length > 0, rows);

    private static BoardRowData Row(string id, string name, string? seat, int cast, int breaks) => new(PublicationProtocol.OfficialPagePath(id), name, seat, cast, breaks);

    private static BoardAgreementData Agreement(PublishedChamber chamber, int total = 450, int nominations = 0) =>
        new(chamber, 119, [new(PublishedVoteKind.Passage, 15, 201)], 196, total - 196, 0, total, nominations);

    private static BoardsPageData Data(BoardPanelData[]? boards = null, BoardAgreementData[]? agreements = null) => new(7,
        boards ?? [Panel(PublishedChamber.House, "Democrat", 119, Row("cuellar", "Henry Cuellar", null, 254, 160))],
        agreements ?? [Agreement(PublishedChamber.House), Agreement(PublishedChamber.Senate, 387)]);

    private static Task<AngleSharp.Dom.IDocument> RenderAsync(BoardsPageData data) =>
        ListPageRender.RenderAsync<BoardsPage>(data, nameof(BoardsPage.Data));

    [Fact]
    public async Task ARowGivesTheCountFirstThenItsBaseAndTheShare()
    {
        var page = await RenderAsync(Data([Panel(PublishedChamber.House, "Democrat", 119, Row("cuellar", "Henry Cuellar", null, 254, 160))]));

        var row = page.QuerySelector(".board-panel .list-row")!;
        Assert.Equal("160 of 254 split votes", row.QuerySelector(".list-row-figure")!.TextContent);
        Assert.Equal("63% of split votes cast", row.QuerySelector(".badge")!.TextContent);
        Assert.Equal(["House Democrats"], row.QuerySelectorAll(".list-row-meta span").Select(span => span.TextContent));
        Assert.Contains("Count first, then the base it comes from.", page.QuerySelector(".board-lede")!.TextContent);
        Assert.Equal("The newest roll call counted here, in the 119th Congress, is on 16 Sep 2026. No count includes a later day.",
            page.QuerySelector(".board-panel details .muted")!.TextContent);
        Assert.DoesNotContain("Congress 119", page.Body!.TextContent);
    }

    [Fact]
    public async Task DelegatesRankInTheirOwnListWithItsHeadingAndSeatAfterTheVotingMembers()
    {
        var moylan = Row("moylan", "James Moylan", "Delegate for Guam", 51, 26);
        var page = await RenderAsync(Data([Panel(PublishedChamber.House, "Republican", 119, Row("fitz", "Brian Fitzpatrick", null, 250, 40)) with { Delegates = [moylan] }]));

        var panel = page.QuerySelector(".board-panel")!;
        var list = Assert.Single(panel.QuerySelectorAll(".board-delegates"));
        Assert.Equal(["Brian Fitzpatrick"], panel.QuerySelectorAll(".list-row-title").Where(link => link.Closest(".board-delegates") is null).Select(link => link.TextContent));
        Assert.Equal("Delegates and the Resident Commissioner", list.QuerySelector("h3")!.TextContent);
        Assert.Equal(BoardWords.DelegatesNote, list.QuerySelector("p")!.TextContent);
        Assert.Equal(["House Republicans", "Delegate for Guam"], list.QuerySelectorAll(".list-row-meta span").Select(span => span.TextContent));
        Assert.Equal("26 of 51 split votes", list.QuerySelector(".list-row-figure")!.TextContent);
        Assert.True(ListPageRender.Precedes(panel.QuerySelector(".board-lede + .list-rows")!, list));
        Assert.Contains("The 1 member of the House Republicans", panel.QuerySelector(".board-lede")!.TextContent);
    }

    [Fact]
    public async Task ABoardWithDelegatesButNoRankedVotingMemberStillShowsTheDelegateList()
    {
        var moylan = Row("moylan", "James Moylan", "Delegate for Guam", 51, 26);
        var page = await RenderAsync(Data([Panel(PublishedChamber.House, "Republican", 119) with { Delegates = [moylan] }]));

        Assert.Contains("No member of the House Republicans has 25 split votes cast", page.QuerySelector(".board-panel")!.TextContent);
        Assert.Equal("James Moylan", page.QuerySelector(".board-delegates .list-row-title")!.TextContent);
    }

    [Fact]
    public async Task ABoardWithoutDelegatesHasNoDelegateList()
    {
        var page = await RenderAsync(Data());

        Assert.Null(page.QuerySelector(".board-delegates"));
        Assert.DoesNotContain(BoardWords.DelegatesTitle, page.Body!.TextContent);
    }

    [Fact]
    public async Task TheEyebrowNamesTheCongressWhenEveryBoardSharesItAndIsAbsentWhenTheChambersDiffer()
    {
        var shared = await RenderAsync(Data([
            Panel(PublishedChamber.House, "Democrat", 119, Row("a", "Ann", null, 30, 10)),
            Panel(PublishedChamber.Senate, "Republican", 119, Row("b", "Bob", null, 30, 5)),
            Panel(PublishedChamber.Senate, "Democrat", null)]));
        var mixed = await RenderAsync(Data([
            Panel(PublishedChamber.House, "Democrat", 118, Row("a", "Ann", null, 30, 10)),
            Panel(PublishedChamber.Senate, "Republican", 119, Row("b", "Bob", null, 30, 5))]));

        Assert.Equal("119th Congress | Four lists, one for each chamber and party", shared.QuerySelector(".page-header-eyebrow")!.TextContent);
        Assert.Null(mixed.QuerySelector(".page-header-eyebrow"));
    }

    [Fact]
    public async Task ABoardThatKeepsAnOlderCongressSaysSoAboveItsList()
    {
        var page = await RenderAsync(Data([Panel(PublishedChamber.House, "Democrat", 118, Row("a", "Ann", null, 30, 10)) with { NewerCongress = 119 }]));

        Assert.Contains("The 119th Congress has no member with 25 split votes yet, so this list keeps the 118th Congress.", page.QuerySelector(".board-panel")!.TextContent);
        Assert.DoesNotContain("this page keeps", page.Body!.TextContent);
    }

    [Fact]
    public async Task AMemberOfAnOlderCongressBoardLinksTheOfficialPageOfThatCongressSoItsFiguresMatchTheCount()
    {
        var older = new BoardRowData(PublicationProtocol.OfficialCongressPagePath("a", 118), "Ann", null, 30, 10);
        var page = await RenderAsync(Data([Panel(PublishedChamber.House, "Democrat", 118, older) with { NewerCongress = 119 }]));

        Assert.Equal(ListPageRender.Root + "officials/a/118.html", page.QuerySelector(".board-panel .list-row-title")!.GetAttribute("href"));
    }

    [Fact]
    public async Task WhenTheChambersKeepDifferentCongressesEachRankedPanelNamesItsCongressAboveTheListOutsideTheFold()
    {
        var page = await RenderAsync(Data([
            Panel(PublishedChamber.House, "Democrat", 118, Row("a", "Ann", null, 30, 10)),
            Panel(PublishedChamber.Senate, "Republican", 119, Row("b", "Bob", null, 30, 5))]));

        var panels = page.QuerySelectorAll(".board-panel");
        foreach (var (panel, ordinal) in panels.Zip(["118th", "119th"]))
        {
            var line = Assert.Single(panel.Children.Where(child => child.LocalName == "p"), paragraph => paragraph.TextContent.StartsWith("The newest roll call counted here", StringComparison.Ordinal));
            Assert.Equal($"The newest roll call counted here, in the {ordinal} Congress, is on 16 Sep 2026. No count includes a later day.", line.TextContent);
            Assert.True(ListPageRender.Precedes(line, panel.QuerySelector(".list-rows")!));
            Assert.DoesNotContain("The newest roll call", panel.QuerySelector("details")!.TextContent);
        }
    }

    [Fact]
    public async Task TheMethodLinkUsesTheApprovedTextAndLeadsToTheMethodPage()
    {
        var page = await RenderAsync(Data());

        var link = page.QuerySelectorAll("a").Single(link => link.TextContent == "Read the method note: how Civic Lens counts split votes and agreed votes");
        Assert.Equal(ListPageRender.Root + PublicationProtocol.MethodPagePath, link.GetAttribute("href"));
    }

    [Fact]
    public async Task AgreementAppearsOncePerChamberOutsideThePartyPanels()
    {
        var page = await RenderAsync(Data([
            Panel(PublishedChamber.House, "Democrat", 119, Row("a", "Ann", null, 30, 10)),
            Panel(PublishedChamber.House, "Republican", 119, Row("b", "Bob", null, 30, 5))]));

        var cards = page.QuerySelectorAll(".board-agreement");
        Assert.Equal(2, cards.Length);
        Assert.Equal(["When both parties agreed (House, 119th Congress)", "When both parties agreed (Senate, 119th Congress)"],
            cards.Select(card => card.QuerySelector("h2")!.TextContent));
        Assert.Empty(page.QuerySelectorAll(".board-panel .board-agreement, .board-panel .board-kinds"));
        Assert.Equal("Vote to pass or agree to", cards[0].QuerySelector(".board-kind dt")!.TextContent);
        Assert.Equal("196 of 450", cards[0].QuerySelectorAll(".board-kind dd").Last().TextContent);
    }

    [Fact]
    public async Task ABoardWithoutVoteFiguresSaysSoAndABoardWithoutRankedMembersNamesTheMinimum()
    {
        var page = await RenderAsync(Data([
            Panel(PublishedChamber.House, "Democrat", null),
            Panel(PublishedChamber.Senate, "Republican", 119)], []));

        var panels = page.QuerySelectorAll(".board-panel");
        Assert.Contains("No vote figures are in this release yet.", panels[0].TextContent);
        Assert.Contains("No member of the Senate Republicans has 25 split votes cast in the 119th Congress.", panels[1].TextContent);
        Assert.Empty(page.QuerySelectorAll(".board-panel .list-row"));
    }

    [Fact]
    public async Task EachTabLinksToTheHeadingOfItsPanelAndNoTabClaimsToBeCurrent()
    {
        var page = await RenderAsync(Data([
            Panel(PublishedChamber.House, "Democrat", 119, Row("a", "Ann", null, 30, 10)),
            Panel(PublishedChamber.Senate, "Republican", 119, Row("b", "Bob", null, 30, 5))]));

        var tabs = page.QuerySelectorAll(".segmented a");
        Assert.Equal(["#h-house-democrat", "#h-senate-republican"], tabs.Select(tab => tab.GetAttribute("href")));
        Assert.All(tabs, tab => Assert.NotNull(page.QuerySelector(tab.GetAttribute("href")!)));
        Assert.All(tabs, tab => Assert.Null(tab.GetAttribute("aria-current")));
    }

    [Fact]
    public async Task EachPanelLinksTheFullListOfItsBoard()
    {
        var page = await RenderAsync(Data([
            Panel(PublishedChamber.House, "Democrat", 119, Row("a", "Ann", null, 30, 10)),
            Panel(PublishedChamber.House, "Republican", 119, Row("b", "Bob", null, 30, 5)),
            Panel(PublishedChamber.Senate, "Democrat", 119, Row("c", "Cy", null, 30, 4)),
            Panel(PublishedChamber.Senate, "Republican", 119, Row("d", "Di", null, 30, 3))]));

        var links = page.QuerySelectorAll(".board-panel a").Where(link => link.TextContent.StartsWith("See the full list", StringComparison.Ordinal)).ToArray();
        Assert.Equal(["See the full list of the House Democrats", "See the full list of the House Republicans", "See the full list of the Senate Democrats", "See the full list of the Senate Republicans"],
            links.Select(link => link.TextContent));
        Assert.Equal(new[] { "board-house-democrat.html", "board-house-republican.html", "board-senate-democrat.html", "board-senate-republican.html" }.Select(path => ListPageRender.Root + path),
            links.Select(link => link.GetAttribute("href")));
    }

    [Theory]
    [InlineData(1, 1000, "under 1% of split votes cast")]
    [InlineData(999, 1000, "over 99% of split votes cast")]
    [InlineData(1, 2, "50% of split votes cast")]
    public async Task TheShareUsesTheRoundingWordsOfBoardWordsAndNamesItsBase(int breaks, int cast, string share)
    {
        var page = await RenderAsync(Data([Panel(PublishedChamber.House, "Democrat", 119, Row("a", "Ann", null, cast, breaks))]));

        Assert.Equal(share, page.QuerySelector(".list-row .badge")!.TextContent);
    }

    [Fact]
    public async Task TheNominationNoteAppearsOnlyWhenMostAgreementVotesAreNominations()
    {
        var most = await RenderAsync(Data(agreements: [Agreement(PublishedChamber.Senate, 387, nominations: 200)]));
        var few = await RenderAsync(Data(agreements: [Agreement(PublishedChamber.Senate, 387, nominations: 193)]));

        Assert.Contains("Most of these 387 votes are on nominations.", most.QuerySelector(".board-agreement")!.TextContent);
        Assert.DoesNotContain("nominations", few.QuerySelector(".board-agreement")!.TextContent);
    }
}
