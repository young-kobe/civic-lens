using CivicLens.PublicWeb.Publication;
using CivicLens.Publication.Contracts;

namespace CivicLens.Tests.PublicWeb;

public sealed class SearchResultsPageTests
{
    private const string Root = ListPageRender.Root;
    private static readonly string BillId = new('b', 64);

    private static SearchQueryData Query(string? text = "5334") => new(text, null, null, null, null, null);

    private static SearchHitData Hit(char id, string headline, string? snippet = null, string? billLine = null) =>
        new(PublishedRecordKind.Action, new string(id, 64), headline, new(2026, 2, 23), billLine, snippet, ["Jimmy Panetta"]);

    private static SearchPageData Data(SearchQueryData? query = null, SearchOfficialData[]? officials = null, SearchBillData? bill = null,
        SearchHitData[]? hits = null, string? next = null, string? filterName = null, bool moreOfficials = false) =>
        new(query ?? Query(), 1028, officials ?? [], moreOfficials, bill, hits ?? [], next, [new("housing", "Housing")], filterName);

    private static SearchOfficialData Official(string id = "jimmy-panetta", string name = "Jimmy Panetta") =>
        new(id, name, PublishedOfficeKind.Representative, null, "CA", 19, "Democrat");

    private static Task<AngleSharp.Dom.IDocument> RenderAsync(SearchPageData data) =>
        ListPageRender.RenderAsync<SearchResultsPage>(data, nameof(SearchResultsPage.Data));

    [Fact]
    public async Task NoResultsShowGuidanceFirstAndLeaveOutTheResultListAndTheStatusLine()
    {
        var page = await RenderAsync(Data(Query("zzqxv")));

        var empty = page.QuerySelector(".empty")!;
        Assert.Contains("No records match \"zzqxv\".", empty.TextContent);
        Assert.Equal(4, empty.QuerySelectorAll(".search-next li").Length);
        Assert.Equal("Next steps:", empty.QuerySelector("p")!.TextContent);
        Assert.True(ListPageRender.Precedes(empty.QuerySelector("p")!, empty.QuerySelector(".search-next")!));
        Assert.True(ListPageRender.Precedes(page.QuerySelector("form.search-box")!, empty));
        Assert.True(ListPageRender.Precedes(empty, page.QuerySelector(".list-fold")!));
        Assert.Null(page.QuerySelector(".record-rows"));
        Assert.Null(page.QuerySelector(".list-status"));
    }

    [Fact]
    public async Task OfficialThenBillPinThenResultsThenFiltersInThatOrder()
    {
        var page = await RenderAsync(Data(
            officials: [Official()],
            bill: new(BillId, "H.R. 5334, the Sanctioning Act", 119, "Jimmy Panetta", "119-111", new(2026, 9, 18), new(new string('v', 64), PublishedChamber.House),
                [new(new string('s', 64), "S. 5334", PublishedChamber.Senate, "John Cornyn")]),
            hits: [Hit('a', "H.R. 5334 would impose sanctions")]));

        var ordered = new[] { ".search-official", ".search-pin", ".record-rows", ".list-fold" }.Select(selector => page.QuerySelector(selector)!).ToArray();
        for (var index = 1; index < ordered.Length; index++) Assert.True(ListPageRender.Precedes(ordered[index - 1], ordered[index]));
        Assert.Equal("Representative for CA, district 19. Democrat.", page.QuerySelector(".search-official p")!.TextContent);
        var pin = page.QuerySelector(".search-pin")!;
        Assert.Equal("Bill in the 119th Congress. Sponsored by Jimmy Panetta. Became law on 18 Sep 2026 as Public Law 119-111.", pin.QuerySelector("p.muted")!.TextContent);
        Assert.Equal(Root + $"records/{BillId}.html", pin.QuerySelector("h2 a")!.GetAttribute("href"));
        Assert.Equal(Root + $"records/{new string('v', 64)}.html", pin.QuerySelectorAll(".search-pin-actions a").Single(link => link.TextContent == "House vote on it").GetAttribute("href"));
        var others = page.QuerySelector(".search-other-bills")!;
        Assert.True(ListPageRender.Precedes(page.QuerySelector(".record-rows")!, others));
        Assert.Contains("S. 5334, a Senate bill introduced by John Cornyn, is also in the list. A number match can find another bill with the same number.",
            string.Join(' ', others.TextContent.Split((char[])[' ', '\n'], StringSplitOptions.RemoveEmptyEntries)));
        Assert.Equal(Root + $"records/{new string('s', 64)}.html", others.QuerySelector("a")!.GetAttribute("href"));
        Assert.Equal("1 result for \"5334\". Release 1028.", page.QuerySelector(".list-status")!.TextContent);
    }

    [Fact]
    public async Task TheDuplicateNumberNoteAppearsOnlyWhenAnotherBillWithTheNumberIsListed()
    {
        var page = await RenderAsync(Data(bill: new(BillId, "H.R. 1", 119, null, null, null, null, []), hits: [Hit('a', "x")]));

        Assert.Null(page.QuerySelector(".search-other-bills"));
        Assert.DoesNotContain("Public Law", page.QuerySelector(".search-pin")!.TextContent);
        Assert.DoesNotContain("Sponsored by", page.QuerySelector(".search-pin")!.TextContent);
        Assert.DoesNotContain("vote on it", page.QuerySelector(".search-pin")!.TextContent);
    }

    [Fact]
    public async Task ABillWhoseOnlyVoteIsInTheSenateLinksThatVoteAsTheSenateVote()
    {
        var page = await RenderAsync(Data(bill: new(BillId, "S. 1", 119, null, null, null, new(new string('v', 64), PublishedChamber.Senate), []), hits: [Hit('a', "x")]));

        var link = Assert.Single(page.QuerySelectorAll(".search-pin-actions a"), link => link.TextContent.EndsWith("vote on it", StringComparison.Ordinal));
        Assert.Equal("Senate vote on it", link.TextContent);
        Assert.Equal(Root + $"records/{new string('v', 64)}.html", link.GetAttribute("href"));
    }

    [Fact]
    public async Task MoreMatchingOfficialsLinkTheOfficialsListFilteredByTheTypedName()
    {
        var more = await RenderAsync(Data(Query(" ann lee "), officials: [Official()], moreOfficials: true));
        var three = await RenderAsync(Data(Query("ann lee"), officials: [Official()]));

        var line = more.QuerySelector(".search-more-officials")!;
        Assert.Equal("More officials match. See all of them in the officials list.", line.TextContent);
        Assert.Equal(Root + PublicationProtocol.OfficialsPagePath + "?name=ann%20lee", line.QuerySelector("a")!.GetAttribute("href"));
        Assert.True(ListPageRender.Precedes(more.QuerySelector(".search-official")!, line));
        Assert.Null(three.QuerySelector(".search-more-officials"));
    }

    [Fact]
    public async Task TheKindFilterListsKindsInTheMockupOrder()
    {
        var page = await RenderAsync(Data());

        Assert.Equal(["Any", "Bill or resolution", "Executive action", "Statement", "Vote", "Weekly brief", "Document change", "Relationship"],
            page.QuerySelectorAll("#search-kind option").Select(option => option.TextContent));
    }

    [Fact]
    public async Task ASnippetMarksItsMatchesAndStoredTextCannotBecomeMarkup()
    {
        var snippet = "Introduced by <b>Ann</b>, H.R. 5334 would act now";
        var page = await RenderAsync(Data(hits: [Hit('a', "Headline", snippet)]));

        var text = page.QuerySelector(".search-snippet")!;
        Assert.Equal(["5334", "act"], text.QuerySelectorAll("mark").Select(mark => mark.TextContent));
        Assert.Null(text.QuerySelector("b"));
        Assert.Equal("Introduced by <b>Ann</b>, H.R. 5334 would act now", text.TextContent);
    }

    [Fact]
    public async Task AHitWithoutASnippetShowsItsBillLineAndOneWithNeitherShowsNoSnippetParagraph()
    {
        var page = await RenderAsync(Data(Query(null) with { Kind = "action" },
            hits: [Hit('a', "One", billLine: "This bill funds housing."), Hit('b', "Two")]));

        var rows = page.QuerySelectorAll(".record-row");
        Assert.Equal("This bill funds housing.", rows[0].QuerySelector(".search-snippet")!.TextContent);
        Assert.Null(rows[1].QuerySelector(".search-snippet"));
    }

    [Fact]
    public async Task TheMoreResultsLinkKeepsTheQueryAndEveryFilter()
    {
        var query = new SearchQueryData("rent cap", "statement", "o1", "housing", "2026-01-01", "2026-03-01");
        var page = await RenderAsync(Data(query, hits: [Hit('a', "x")], next: "r.1.abc"));

        Assert.Equal("?q=rent%20cap&kind=statement&official=o1&issue=housing&from=2026-01-01&to=2026-03-01&cursor=r.1.abc",
            page.QuerySelector(".pager a[rel=next]")!.GetAttribute("href"));
        Assert.Equal("statement", page.QuerySelector("#search-kind option[selected]")!.GetAttribute("value"));
        Assert.Equal("housing", page.QuerySelector("#search-issue option[selected]")!.GetAttribute("value"));
        Assert.Equal("2026-01-01", page.QuerySelector("#search-from")!.GetAttribute("value"));
        Assert.Equal("o1", page.QuerySelector(".list-fold input[name=official]")!.GetAttribute("value"));
    }

    [Fact]
    public async Task ABlankSearchShowsTheFormAndTheHintAndNoEmptyResultMessage()
    {
        var page = await RenderAsync(Data(Query(null)));

        Assert.Null(page.QuerySelector(".empty"));
        Assert.Equal("Type a topic, a bill number, or the name of an official.", page.QuerySelector(".list-scope")!.TextContent);
        Assert.Equal("", page.QuerySelector("#search-text")!.GetAttribute("value") ?? "");
    }

    [Fact]
    public async Task OfficialCardsWithNoRecordStillShowTheEmptyGuidanceAfterTheCards()
    {
        var page = await RenderAsync(Data(Query("member"), officials: [Official()]));

        var empty = page.QuerySelector(".empty")!;
        Assert.Contains("No records match \"member\".", empty.TextContent);
        Assert.True(ListPageRender.Precedes(page.QuerySelector(".search-official")!, empty));
    }

    [Fact]
    public async Task AnOfficialMatchOrABillPinLeavesOutTheSpellingStepBecauseTheTextFoundAMatch()
    {
        var official = await RenderAsync(Data(Query("sen"), officials: [Official()], moreOfficials: true));
        var pin = await RenderAsync(Data(Query("77"), bill: new(BillId, "S. 77, the Example Act", 119, "John Cornyn", null, null, null, [])));
        var neither = await RenderAsync(Data(Query("zzqxv")));

        Assert.DoesNotContain("spelling", official.QuerySelector(".empty")!.TextContent);
        Assert.Equal(3, official.QuerySelectorAll(".search-next li").Length);
        Assert.DoesNotContain("spelling", pin.QuerySelector(".empty")!.TextContent);
        Assert.Contains("Check the spelling, or type fewer words.", neither.QuerySelector(".empty")!.TextContent);
    }

    [Fact]
    public async Task AFilterOnlySearchLeavesOutTheSpellingStep()
    {
        var page = await RenderAsync(Data(Query(null) with { Kind = "statement" }));

        Assert.Contains("No records match these filters.", page.QuerySelector(".empty")!.TextContent);
        Assert.Equal(3, page.QuerySelectorAll(".search-next li").Length);
        Assert.DoesNotContain("spelling", page.QuerySelector(".empty")!.TextContent);
    }

    [Fact]
    public async Task TheRecordsOfAnOfficialLinkDropsTheTextAndTheActiveFilterNamesTheOfficialWithAWayToRemoveIt()
    {
        var cards = await RenderAsync(Data(Query("panetta"), officials: [Official()], hits: [Hit('a', "x")]));
        var filtered = await RenderAsync(Data(new("rent", "statement", "jimmy-panetta", null, null, null), hits: [Hit('a', "x")], filterName: "Jimmy Panetta"));

        Assert.Equal("?official=jimmy-panetta", cards.QuerySelectorAll(".search-official a").Single(link => link.TextContent == "Records that name Jimmy Panetta").GetAttribute("href"));
        Assert.Null(cards.QuerySelector(".search-filter-official"));
        var active = filtered.QuerySelector(".search-filter-official")!;
        Assert.StartsWith("Only records that name Jimmy Panetta are shown.", active.TextContent);
        Assert.Equal("?q=rent&kind=statement", active.QuerySelector("a")!.GetAttribute("href"));
    }

    [Fact]
    public async Task ACountWithAnotherPageSaysItCoversThisPageAndThePagerOffersNoWayBack()
    {
        var page = await RenderAsync(Data(hits: [Hit('a', "x"), Hit('b', "y")], next: "r.1.abc"));

        Assert.Equal("2 results on this page for \"5334\". Release 1028.", page.QuerySelector(".list-status")!.TextContent);
        Assert.Single(page.QuerySelectorAll(".pager a, .pager button"));
    }

    [Fact]
    public async Task OnlyBalancedMarksWrapTextAndAStrayMarkKeepsItsText()
    {
        var page = await RenderAsync(Data(hits: [Hit('a', "Headline", "\uE000one\uE001 two\uE001 three \uE000four")]));

        var text = page.QuerySelector(".search-snippet")!;
        Assert.Equal(["one"], text.QuerySelectorAll("mark").Select(mark => mark.TextContent));
        Assert.Equal("one two three four", text.TextContent);
    }

    [Fact]
    public async Task AnExecutiveOfficialCardNamesTheOfficeAndEachCardRegionIsNamedByItsHeading()
    {
        var page = await RenderAsync(Data(Query("trump"), officials: [new("donald-trump", "Donald J. Trump", PublishedOfficeKind.Executive, "President", null, null, "Republican")]));

        var card = page.QuerySelector(".search-official")!;
        Assert.Equal("President. Republican.", card.QuerySelector("p")!.TextContent);
        Assert.Equal(card.QuerySelector("h2")!.Id, card.GetAttribute("aria-labelledby"));
        Assert.Equal("Search records", page.QuerySelector("form.search-box")!.GetAttribute("aria-label"));
    }

    [Fact]
    public async Task ATypedQueryIsEncodedInTheFieldAndInTheEmptyMessage()
    {
        var page = await RenderAsync(Data(Query("\"><script>x()</script>")));

        Assert.Empty(page.QuerySelectorAll("main script"));
        Assert.Equal("\"><script>x()</script>", page.QuerySelector("#search-text")!.GetAttribute("value"));
    }
}
