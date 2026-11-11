using CivicLens.PublicWeb.Publication;
using CivicLens.Publication.Contracts;

namespace CivicLens.Tests.PublicWeb;

public sealed class OfficialsPageTests
{
    private const string TotalScope = "Officials that the active release lists, with or without a term. A filter does not change this number.";
    private const string Scope = "Records of the active release that name the official. Votes are not counted.";

    private static OfficialListRowData Row(string id, string name, PublishedOfficeKind kind, string? state, int? district, string? party,
        DateOnly? end, int count) => new(id, name, kind, kind.ToString(), state, district, party, null, end, count);

    private static OfficialsPageData Page(OfficialListRowData[] rows, OfficialsFilterData? filter = null, string? next = null) =>
        new(7, Scope, 542, TotalScope, rows, filter ?? OfficialsFilterData.None, next);

    private static Task<AngleSharp.Dom.IDocument> RenderAsync(OfficialsPageData data) =>
        ListPageRender.RenderAsync<OfficialsPage>(data, nameof(OfficialsPage.Data));

    [Fact]
    public async Task EachRowNamesSeatPartyTermEndAndOneRecordCountWhileTheScopeAppearsOnce()
    {
        var page = await RenderAsync(Page([
            Row("aaron-bean", "Aaron Bean", PublishedOfficeKind.Representative, "FL", 4, "Republican", new(2027, 1, 3), 25),
            Row("adam-schiff", "Adam B. Schiff", PublishedOfficeKind.Senator, "CA", null, "Democrat", null, 1)]));

        var rows = page.QuerySelectorAll(".list-row");
        Assert.Equal(2, rows.Length);
        Assert.Equal(["Representative for FL, district 4", "Republican", "Term ends 3 Jan 2027"],
            rows[0].QuerySelectorAll(".list-row-meta span").Select(span => span.TextContent));
        Assert.Equal("25 records other than votes", rows[0].QuerySelector(".list-row-side")!.TextContent);
        Assert.Equal(["Senator for CA", "Democrat", "Our records show no end date for this term."],
            rows[1].QuerySelectorAll(".list-row-meta span").Select(span => span.TextContent));
        Assert.Equal("1 record other than votes", rows[1].QuerySelector(".list-row-side")!.TextContent);
        Assert.Equal(Root + "officials/aaron-bean.html", rows[0].QuerySelector("a")!.GetAttribute("href"));
        var scope = Assert.Single(page.QuerySelectorAll(".list-scope"));
        Assert.Contains(TotalScope, scope.TextContent);
        Assert.Contains(Scope, scope.TextContent);
        Assert.Single(page.Body!.TextContent.Split(Scope).Skip(1));
    }

    [Theory]
    [InlineData(542, "542 officials have a page. Each page lists")]
    [InlineData(1, "1 official has a page. Each page lists")]
    public async Task TheLedeStartsWithTheTotalOfTheReleaseWhateverTheFilter(int total, string start)
    {
        var page = await RenderAsync(Page([], new("house", null, null, "zzz")) with { OfficialsInRelease = total });

        Assert.StartsWith(start, page.QuerySelector(".page-header-lede")!.TextContent);
    }

    private const string Root = ListPageRender.Root;

    [Fact]
    public async Task ADelegateSeatKeepsItsOwnOfficeWordInsteadOfRepresentative()
    {
        var page = await RenderAsync(Page([Row("hal", "Hal Hale", PublishedOfficeKind.Representative, "DC", 0, "Democrat", null, 3)]));

        Assert.Equal("Delegate for DC", page.QuerySelector(".list-row-meta span")!.TextContent);
    }

    [Fact]
    public async Task NoMatchShowsGuidanceBeforeTheFiltersAndNoRowsOrPager()
    {
        var page = await RenderAsync(Page([], new("house", null, null, "zzz"), next: null));

        var empty = page.QuerySelector(".empty")!;
        Assert.Contains("No official matches these filters.", empty.TextContent);
        Assert.True(ListPageRender.Precedes(empty, page.QuerySelector("form.lists-filter")!));
        Assert.Null(page.QuerySelector(".list-rows"));
        Assert.Null(page.QuerySelector(".pager"));
        Assert.Null(page.QuerySelector(".list-status"));
    }

    [Fact]
    public async Task TheNextLinkKeepsEveryFilterAndTheCursorAndIsAbsentOnTheLastPage()
    {
        var rows = new[] { Row("a", "Ann", PublishedOfficeKind.Senator, "MA", null, "Democrat", null, 0) };
        var filter = new OfficialsFilterData("senate", "MA", "democrat", "an d");

        var next = await RenderAsync(Page(rows, filter, "o.7.abc"));
        var last = await RenderAsync(Page(rows, filter));

        Assert.Equal("?chamber=senate&state=MA&party=democrat&name=an%20d&cursor=o.7.abc",
            next.QuerySelector(".pager a[rel=next]")!.GetAttribute("href"));
        Assert.Null(last.QuerySelector(".pager"));
    }

    [Fact]
    public async Task TheFormKeepsTheChosenFiltersAndOffersClearOnlyWhenOneIsActive()
    {
        var filtered = await RenderAsync(Page([Row("a", "Ann", PublishedOfficeKind.Senator, "MA", null, "Democrat", null, 0)],
            new("senate", "MA", "democrat", "An")));
        var plain = await RenderAsync(Page([Row("a", "Ann", PublishedOfficeKind.Senator, "MA", null, "Democrat", null, 0)]));

        Assert.Equal("senate", filtered.QuerySelector("#officials-chamber option[selected]")!.GetAttribute("value"));
        Assert.Equal("MA", filtered.QuerySelector("#officials-state option[selected]")!.GetAttribute("value"));
        Assert.Equal("democrat", filtered.QuerySelector("#officials-party option[selected]")!.GetAttribute("value"));
        Assert.Equal("An", filtered.QuerySelector("#officials-name")!.GetAttribute("value"));
        Assert.Contains("that match your filters", filtered.QuerySelector(".list-status")!.TextContent);
        Assert.NotNull(filtered.QuerySelector(".lists-filter-actions a"));
        Assert.Null(plain.QuerySelector(".lists-filter-actions a"));
        Assert.DoesNotContain("match your filters", plain.QuerySelector(".list-status")!.TextContent);
    }

    [Fact]
    public async Task StoredNamesAreEncodedAndOfficialIdsAreEscapedInTheLink()
    {
        var page = await RenderAsync(Page([Row("a b", "<script>x()</script>", PublishedOfficeKind.Senator, null, null, null, null, 0)]));

        Assert.Null(page.QuerySelector(".list-row script"));
        Assert.Equal("<script>x()</script>", page.QuerySelector(".list-row-title")!.TextContent);
        Assert.Equal(Root + "officials/a%20b.html", page.QuerySelector(".list-row-title")!.GetAttribute("href"));
    }
}
