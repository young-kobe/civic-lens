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
        SearchHitData[]? hits = null, string? next = null) =>
        new(query ?? Query(), 1028, officials ?? [], bill, hits ?? [], next, [new("housing", "Housing")]);

    private static Task<AngleSharp.Dom.IDocument> RenderAsync(SearchPageData data) =>
        ListPageRender.RenderAsync<SearchResultsPage>(data, nameof(SearchResultsPage.Data));

    [Fact]
    public async Task NoResultsShowGuidanceFirstAndLeaveOutTheResultListAndTheStatusLine()
    {
        var page = await RenderAsync(Data(Query("zzqxv")));

        var empty = page.QuerySelector(".empty")!;
        Assert.Contains("No records match \"zzqxv\".", empty.TextContent);
        Assert.Equal(4, empty.QuerySelectorAll(".search-next li").Length);
        Assert.True(ListPageRender.Precedes(page.QuerySelector("form.search-box")!, empty));
        Assert.True(ListPageRender.Precedes(empty, page.QuerySelector(".list-fold")!));
        Assert.Null(page.QuerySelector(".list-rows"));
        Assert.Null(page.QuerySelector(".list-status"));
    }

    [Fact]
    public async Task OfficialThenBillPinThenResultsThenFiltersInThatOrder()
    {
        var page = await RenderAsync(Data(
            officials: [new("jimmy-panetta", "Jimmy Panetta", PublishedOfficeKind.Representative, "CA", 19, "Democrat")],
            bill: new(BillId, "H.R. 5334, the Sanctioning Act", 119, "119-111", 1),
            hits: [Hit('a', "H.R. 5334 would impose sanctions")]));

        var ordered = new[] { ".search-official", ".search-pin", ".list-rows", ".list-fold" }.Select(selector => page.QuerySelector(selector)!).ToArray();
        for (var index = 1; index < ordered.Length; index++) Assert.True(ListPageRender.Precedes(ordered[index - 1], ordered[index]));
        Assert.Equal("Representative for CA, district 19. Democrat.", page.QuerySelector(".search-official p")!.TextContent);
        Assert.Contains("Bill in the 119th Congress. Became law as Public Law 119-111.", page.QuerySelector(".search-pin")!.TextContent);
        Assert.Equal(Root + $"records/{BillId}.html", page.QuerySelector(".search-pin h2 a")!.GetAttribute("href"));
        Assert.Contains("another bill with the same number", page.QuerySelector(".search-pin")!.TextContent);
        Assert.Equal("1 result for \"5334\". Release 1028.", page.QuerySelector(".list-status")!.TextContent);
    }

    [Fact]
    public async Task TheDuplicateNumberNoteAppearsOnlyWhenAnotherBillHasTheNumber()
    {
        var page = await RenderAsync(Data(bill: new(BillId, "H.R. 1", 119, null, 0), hits: [Hit('a', "x")]));

        Assert.DoesNotContain("another bill", page.QuerySelector(".search-pin")!.TextContent);
        Assert.DoesNotContain("Public Law", page.QuerySelector(".search-pin")!.TextContent);
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

        var rows = page.QuerySelectorAll(".search-hit");
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
        Assert.Contains("Type a topic or a bill number", page.QuerySelector(".list-scope")!.TextContent);
        Assert.Equal("", page.QuerySelector("#search-text")!.GetAttribute("value") ?? "");
    }

    [Fact]
    public async Task ATypedQueryIsEncodedInTheFieldAndInTheEmptyMessage()
    {
        var page = await RenderAsync(Data(Query("\"><script>x()</script>")));

        Assert.Empty(page.QuerySelectorAll("main script"));
        Assert.Equal("\"><script>x()</script>", page.QuerySelector("#search-text")!.GetAttribute("value"));
    }
}
