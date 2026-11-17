using CivicLens.Application.Roster;
using CivicLens.PublicWeb.Publication;
using CivicLens.Publication.Contracts;

namespace CivicLens.Tests.PublicWeb;

public sealed class OfficialsPageTests
{
    private static OfficialListRowData Row(string id, string name, PublishedOfficeKind kind, string? state, int? district, string? party,
        DateOnly? end, int count) => new(id, name, kind, kind.ToString(), state, district, party, end, count);

    private static OfficialsPageData Page(OfficialListRowData[] rows, RecordListFilter? filter = null, string? next = null) =>
        new(7, 542, rows, filter ?? RecordListFilter.Of(null, null), next);

    private static Task<AngleSharp.Dom.IDocument> RenderAsync(OfficialsPageData data) =>
        ListPageRender.RenderAsync<OfficialsPage>(data, nameof(OfficialsPage.Data));

    [Fact]
    public async Task EachRowNamesSeatPartyTermEndAndOneRecordCountThatNamesWhatItCountsAndNoReleaseNumber()
    {
        var page = await RenderAsync(Page([
            Row("aaron-bean", "Aaron Bean", PublishedOfficeKind.Representative, "FL", 4, "Republican", new(2027, 1, 3), 1250),
            Row("adam-schiff", "Adam B. Schiff", PublishedOfficeKind.Senator, "CA", null, "Democrat", null, 1)]));

        var rows = page.QuerySelectorAll(".list-row");
        Assert.Equal(2, rows.Length);
        Assert.Equal(["Representative for FL, district 4", "Republican", "Term ends 3 Jan 2027"],
            rows[0].QuerySelectorAll(".list-row-meta span").Select(span => span.TextContent));
        Assert.Equal("1,250 published records name this official, not counting votes", rows[0].QuerySelector(".list-row-side")!.TextContent);
        Assert.Equal(["Senator for CA", "Democrat", "Our records show no end date for this term."],
            rows[1].QuerySelectorAll(".list-row-meta span").Select(span => span.TextContent));
        Assert.Equal("1 published record names this official, not counting votes", rows[1].QuerySelector(".list-row-side")!.TextContent);
        Assert.Equal(Root + "officials/aaron-bean.html", rows[0].QuerySelector("a")!.GetAttribute("href"));
        Assert.Equal("Showing 2 officials on this page.", page.QuerySelector(".list-status")!.TextContent);
        Assert.DoesNotContain("Release", page.Body!.TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain("entry row", page.Body!.TextContent);
    }

    [Theory]
    [InlineData(542, "542 officials have a page. Each page lists")]
    [InlineData(1542, "1,542 officials have a page. Each page lists")]
    [InlineData(1, "1 official has a page. Each page lists")]
    public async Task TheLedeStartsWithTheTotalOfTheReleaseWhateverTheFilter(int total, string start)
    {
        var page = await RenderAsync(Page([], RecordListFilter.Of(null, null, ("chamber", "house"), ("name", "zzz"))) with { OfficialsInRelease = total });

        Assert.StartsWith(start, page.QuerySelector(".page-header-lede")!.TextContent);
        Assert.Contains("Looking for the parties? Compare the parties.", page.QuerySelector(".page-header-lede")!.TextContent);
    }

    private const string Root = ListPageRender.Root;

    [Fact]
    public async Task ADelegateSeatKeepsItsOwnOfficeWordInsteadOfRepresentative()
    {
        var page = await RenderAsync(Page([Row("hal", "Hal Hale", PublishedOfficeKind.Representative, "DC", 0, "Democrat", null, 3)]));

        Assert.Equal("Delegate for District of Columbia", page.QuerySelector(".list-row-meta span")!.TextContent);
    }

    [Theory]
    [InlineData(ExecutiveRosterParser.President, "President")]
    [InlineData(ExecutiveRosterParser.VicePresident, "Vice President")]
    public async Task AnExecutiveRowNamesTheOfficeWordAndNeverTheRawExecutiveCode(string code, string words)
    {
        var row = Row("eve", "Eve East", PublishedOfficeKind.Executive, null, null, "Republican", null, 3) with { Office = code };

        var page = await RenderAsync(Page([row]));

        Assert.Equal(words, page.QuerySelector(".list-row-meta span")!.TextContent);
    }

    [Fact]
    public async Task NoMatchShowsGuidanceBeforeTheFiltersAndNoRowsOrPager()
    {
        var page = await RenderAsync(Page([], RecordListFilter.Of(null, null, ("chamber", "house"), ("name", "zzz")), next: null));

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
        var filter = RecordListFilter.Of(null, null, ("chamber", "senate"), ("state", "MA"), ("party", "democrat"), ("name", "an & d"));

        var next = await RenderAsync(Page(rows, filter, "o.7.abc"));
        var last = await RenderAsync(Page(rows, filter));

        Assert.Equal("?chamber=senate&state=MA&party=democrat&name=an%20%26%20d&cursor=o.7.abc",
            next.QuerySelector(".pager a[rel=next]")!.GetAttribute("href"));
        Assert.Equal("Next page", next.QuerySelector(".pager a[rel=next]")!.TextContent.Trim());
        Assert.Single(next.QuerySelectorAll(".pager a, .pager button"));
        Assert.DoesNotContain("Newer", next.QuerySelector(".pager")!.TextContent);
        Assert.Null(last.QuerySelector(".pager"));
    }

    [Fact]
    public async Task TheFormKeepsTheChosenFiltersAndOffersClearOnlyWhenOneIsActive()
    {
        var filtered = await RenderAsync(Page([Row("a", "Ann", PublishedOfficeKind.Senator, "MA", null, "Democrat", null, 0)],
            RecordListFilter.Of(null, null, ("chamber", "senate"), ("state", "MA"), ("party", "democrat"), ("name", "An"))));
        var plain = await RenderAsync(Page([Row("a", "Ann", PublishedOfficeKind.Senator, "MA", null, "Democrat", null, 0)]));

        Assert.Equal("senate", filtered.QuerySelector("#officials-chamber option[selected]")!.GetAttribute("value"));
        Assert.Equal("MA", filtered.QuerySelector("#officials-state option[selected]")!.GetAttribute("value"));
        Assert.Equal("democrat", filtered.QuerySelector("#officials-party option[selected]")!.GetAttribute("value"));
        Assert.Equal("An", filtered.QuerySelector("#officials-name")!.GetAttribute("value"));
        Assert.StartsWith("Showing 1 official on this page.", filtered.QuerySelector(".list-status")!.TextContent);
        Assert.NotNull(filtered.QuerySelector(".lists-filter-actions a"));
        Assert.Null(plain.QuerySelector(".lists-filter-actions a"));
        Assert.DoesNotContain("match", filtered.QuerySelector(".list-status")!.TextContent);
        Assert.DoesNotContain("match", plain.QuerySelector(".list-status")!.TextContent);
    }

    [Fact]
    public async Task TheCountLineNamesNoReleaseAndEachActiveFilterHasOneChipInFormOrder()
    {
        var row = Row("a", "Ann", PublishedOfficeKind.Senator, "MA", null, "Democrat", null, 0);
        var filtered = await RenderAsync(Page([row], RecordListFilter.Of(null, null, ("chamber", "senate"), ("state", "MA"), ("party", "democrat"), ("name", "An"))));
        var party = await RenderAsync(Page([row], RecordListFilter.Of(null, null, ("party", "independent"))));
        var plain = await RenderAsync(Page([row]));

        Assert.Equal("Showing 1 official on this page.", filtered.QuerySelector(".list-status")!.TextContent);
        Assert.Equal(["Senate", "MA", "Democrat", "Name: An"], filtered.QuerySelectorAll(".list-chips li").Select(chip => chip.TextContent.Trim()));
        Assert.Equal(["Independent"], party.QuerySelectorAll(".list-chips li").Select(chip => chip.TextContent.Trim()));
        Assert.Null(plain.QuerySelector(".list-chips"));
    }

    [Fact]
    public async Task StoredNamesAreEncodedAndOfficialIdsAreEscapedInTheLink()
    {
        var page = await RenderAsync(Page([Row("a b", "<script>x()</script>", PublishedOfficeKind.Senator, null, null, null, null, 0)]));

        Assert.Null(page.QuerySelector(".list-row script"));
        Assert.Equal("<script>x()</script>", page.QuerySelector(".list-row-title")!.TextContent);
        Assert.Equal(Root + "officials/a%20b.html", page.QuerySelector(".list-row-title")!.GetAttribute("href"));
    }

    [Fact]
    public async Task RefusedNameKeepsTypedTextMarksTheFieldAndDoesNotClaimThereAreNoMatches()
    {
        var name = "<script>bad & name" + new string('x', OfficialsPage.MaximumNameLength + 1);
        var filter = RecordListFilter.Refused("name", ("name", name));
        var page = await RenderAsync(Page([], filter));

        var input = page.QuerySelector("#officials-name")!;
        Assert.Equal(name, input.GetAttribute("value"));
        Assert.Equal("true", input.GetAttribute("aria-invalid"));
        Assert.Null(input.GetAttribute("aria-describedby"));
        Assert.Null(page.QuerySelector(".empty"));
        Assert.Null(page.QuerySelector(".list-rows"));
        Assert.Null(page.QuerySelector(".list-status"));
        Assert.Null(page.QuerySelector(".pager"));
    }

    [Theory]
    [InlineData("chamber", "moon", "ma", "Republican")]
    [InlineData("name", "senate", "<script>bad & choice</script>", "unlisted")]
    public async Task ARefusedFieldDoesNotEraseOtherSelectedValues(string refusedField, string chamber, string state, string party)
    {
        var page = await RenderAsync(Page([], RecordListFilter.Refused(refusedField,
            ("chamber", chamber), ("state", state), ("party", party))));

        foreach (var (field, value) in new[] { ("chamber", chamber), ("state", state), ("party", party) })
        {
            var select = page.QuerySelector($"select[name={field}]")!;
            Assert.Equal(value, select.QuerySelector("option[selected]")?.GetAttribute("value"));
            Assert.Equal(field == refusedField ? "true" : null, select.GetAttribute("aria-invalid"));
            Assert.Null(select.QuerySelector("script"));
        }
    }

    [Theory]
    [InlineData("chamber")]
    [InlineData("state")]
    [InlineData("party")]
    public async Task ARefusedSelectKeepsItsUnknownValueVisibleAsTextInsteadOfShowingAll(string field)
    {
        const string value = "<script>bad & choice</script>";
        var page = await RenderAsync(Page([], RecordListFilter.Refused(field, (field, value))));
        var select = page.QuerySelector($"select[name={field}]")!;

        Assert.Equal(value, select.QuerySelector("option[selected]")?.GetAttribute("value"));
        Assert.Equal(value, select.QuerySelector("option[selected]")?.TextContent);
        Assert.Equal("true", select.GetAttribute("aria-invalid"));
        Assert.Null(select.QuerySelector("script"));
        Assert.NotNull(select.QuerySelector("option[value='']"));
    }

}
