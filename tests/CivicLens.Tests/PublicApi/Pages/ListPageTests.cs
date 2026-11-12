using System.Net;
using AngleSharp.Html.Parser;
using CivicLens.PublicApi;
using CivicLens.PublicApi.Officials;
using CivicLens.PublicApi.Pages;
using CivicLens.PublicApi.Search;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.Infrastructure.Collection;
using CivicLens.Tests.PublicApi.Bills;
using CivicLens.Tests.PublicApi.Officials;
using CivicLens.Tests.PublicApi.Votes;

namespace CivicLens.Tests.PublicApi.Pages;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class ListPageTests(PostgresCollection postgres)
{
    private static readonly PublicBaseUrl BaseUrl = PublicBaseUrl.Parse("https://civic-lens.test", "test")!;
    private static readonly DateTimeOffset PublishedAt = new(2026, 6, 9, 12, 0, 0, TimeSpan.Zero);

    private Task<ApiHost> StartAsync(FakeReleaseSite? site = null) =>
        ApiHost.StartAsync(postgres, site ?? new FakeReleaseSite(), options => options with { PublicBaseUrl = BaseUrl });

    private static SeededOfficial Senator(int index) => new($"p{index:00}", $"Sen {index:00}",
        new SeededTerm("Senate", "Senator", "2021-01-03") { State = "NY", Party = "Democrat", EndsOn = "2027-01-03" });

    private static async Task<AngleSharp.Dom.IDocument> ParseAsync(HttpResponseMessage response) =>
        await new HtmlParser().ParseDocumentAsync(await response.Content.ReadAsStringAsync());

    [Fact]
    public async Task TheOfficialsPagePagesByTwentyFiveKeepsItsFiltersAndUsesTheSharedPageCache()
    {
        await using var host = await StartAsync();
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 1, PublishedAt);
        await OfficialsSeed.AddOfficialsAsync(host.OperationalDatabase,
            [.. Enumerable.Range(0, 30).Select(Senator), new("h1", "Rep One", new SeededTerm("House", "Representative", "2021-01-03") { State = "TX", District = 2, Party = "Republican" })]);

        var first = await host.Client.GetAsync("/web/officials?chamber=senate");
        var firstPage = await ParseAsync(first);
        var next = firstPage.QuerySelector(".pager a[rel=next]")!.GetAttribute("href")!;
        var second = await ParseAsync(await host.Client.GetAsync("/web/officials" + next));

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(PageCache.CacheControl, string.Join(", ", first.Headers.GetValues("Cache-Control")));
        Assert.StartsWith("W/\"r1-", first.Headers.ETag!.ToString());
        Assert.Equal(OfficialsPageReader.PageSize, firstPage.QuerySelectorAll(".list-row").Length);
        Assert.StartsWith("?chamber=senate&cursor=", next);
        Assert.Equal(5, second.QuerySelectorAll(".list-row").Length);
        Assert.Null(second.QuerySelector(".pager"));
        Assert.DoesNotContain("Rep One", firstPage.Body!.TextContent + second.Body!.TextContent);
        Assert.Contains("Term ends 3 Jan 2027", firstPage.QuerySelector(".list-row")!.TextContent);
        Assert.StartsWith("31 officials have a page.", firstPage.QuerySelector(".page-header-lede")!.TextContent);
        Assert.StartsWith("31 officials have a page.", second.QuerySelector(".page-header-lede")!.TextContent);
    }

    [Fact]
    public async Task ACursorOfAnotherReleaseRedirectsToTheFirstPageOfTheSameFilters()
    {
        await using var host = await StartAsync();
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 1, PublishedAt);
        await OfficialsSeed.AddOfficialsAsync(host.OperationalDatabase, Senator(1));
        var stale = new OfficialCursor(99, "Sen 01", "p01").Encode();

        var response = await new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = host.Client.BaseAddress }
            .GetAsync($"/web/officials?party=democrat&cursor={stale}");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/web/officials?party=democrat", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task AnInvalidOfficialsFilterAnswers400AndAMissingReleaseAnswers503()
    {
        await using var host = await StartAsync();

        var none = await host.Client.GetAsync("/web/officials");
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 1, PublishedAt);
        var invalid = await host.Client.GetAsync("/web/officials?chamber=moon");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, none.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    [Fact]
    public async Task TheBoardsPageRendersFourPartyPanelsWithTheReleaseETag()
    {
        await using var host = await StartAsync();
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 3, PublishedAt);

        var response = await host.Client.GetAsync("/web/boards");
        var page = await ParseAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("W/\"r3-", response.Headers.ETag!.ToString());
        Assert.Equal(4, page.QuerySelectorAll(".board-panel").Length);
        Assert.Equal("Compare the parties", page.QuerySelector("h1")!.TextContent);
    }

    [Fact]
    public async Task SearchShowsTheOfficialFirstPinsTheBillNumberAndNeverEntersASharedCache()
    {
        var site = new FakeReleaseSite();
        site.Publish(1, PublishedSamples.Statement('a', "H.R. 5334 debate", official: "p1"));
        await using var host = await StartAsync(site);
        await host.WaitForReleaseAsync();
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 1, PublishedAt);
        await OfficialsSeed.AddOfficialsAsync(host.OperationalDatabase,
            new SeededOfficial("p1", "Ayanna Pressley", new SeededTerm("House", "Representative", "2021-01-03") { State = "MA", District = 7, Party = "Democrat" }));
        await BillsSeed.AddBillsAsync(host.OperationalDatabase, new SeededBill('1', "2026-03-03") { BillNumber = 5334, BillLine = "H.R. 5334, the Sanctions Act" });

        var byNumber = await host.Client.GetAsync("/web/search?q=5334");
        var byName = await host.Client.GetAsync("/web/search?q=Pressley");
        var number = await ParseAsync(byNumber);
        var name = await ParseAsync(byName);

        Assert.True(byNumber.Headers.CacheControl!.NoStore);
        Assert.True(byNumber.Headers.CacheControl.Private);
        Assert.Null(byNumber.Headers.ETag);
        Assert.Equal("H.R. 5334, the Sanctions Act", number.QuerySelector(".search-pin h2")!.TextContent);
        Assert.Contains("H.R. 5334 debate", number.QuerySelector(".record-rows")!.TextContent);
        Assert.Null(number.QuerySelector(".search-official"));
        Assert.Equal("Ayanna Pressley", name.QuerySelector(".search-official h2")!.TextContent);
        Assert.Null(name.QuerySelector(".search-pin"));
        Assert.Null((await ParseAsync(await host.Client.GetAsync("/web/search?q=Pr"))).QuerySelector(".search-official"));
        Assert.Equal("Ayanna Pressley", (await ParseAsync(await host.Client.GetAsync("/web/search?q=ayanna%20pre"))).QuerySelector(".search-official h2")!.TextContent);
    }

    [Fact]
    public async Task TheBillPinNamesTheSponsorLinksTheNewestHouseVoteAndNamesTheOtherBillWithItsSponsor()
    {
        var site = new FakeReleaseSite();
        site.Publish(1, PublishedSamples.Statement('a', "H.R. 5334 debate", official: "p1"));
        await using var host = await StartAsync(site);
        await host.WaitForReleaseAsync();
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 1, PublishedAt);
        await OfficialsSeed.AddOfficialsAsync(host.OperationalDatabase,
            new SeededOfficial("p1", "Jimmy Panetta", new SeededTerm("House", "Representative", "2021-01-03") { State = "CA", District = 19, Party = "Democrat" }),
            new SeededOfficial("p3", "John Cornyn", new SeededTerm("Senate", "Senator", "2021-01-03") { State = "TX", Party = "Republican" }));
        await BillsSeed.AddBillsAsync(host.OperationalDatabase,
            new SeededBill('1', "2026-03-03") { BillNumber = 5334, BillLine = "H.R. 5334, the Sanctions Act" },
            new SeededBill('2', "2026-02-01") { BillType = "s", BillNumber = 5334, Sponsorships = [new("p3", "Sponsor", "2026-01-10")] });
        await ViewsSeed.AddVotesAsync(host.OperationalDatabase,
            new SeededVote('3', "2026-03-01") { BillNumber = 5334, RollCall = 1 },
            new SeededVote('4', "2026-03-02") { BillNumber = 5334, RollCall = 2 },
            new SeededVote('5', "2026-03-04") { BillNumber = 5334, RollCall = 3, Chamber = "Senate" });

        var page = await ParseAsync(await host.Client.GetAsync("/web/search?q=5334"));
        var pin = page.QuerySelector(".search-pin")!;

        Assert.Equal("Bill in the 119th Congress. Sponsored by Jimmy Panetta.", pin.QuerySelector("p.muted")!.TextContent);
        Assert.Equal("https://civic-lens.test/" + PublicationProtocol.RecordPagePath(new string('4', 64)),
            pin.QuerySelectorAll(".search-pin-actions a").Single(link => link.TextContent == "House vote on it").GetAttribute("href"));
        Assert.Contains("a Senate bill introduced by John Cornyn, is also in the list.", page.QuerySelector(".search-other-bills")!.TextContent);
        Assert.Equal("S. 5334", page.QuerySelector(".search-other-bills a")!.TextContent);
    }

    [Fact]
    public async Task SearchPutsAnOfficialWhoseWholeNameEqualsTheTextBeforeAnEarlierNameThatOnlyContainsItAsAWord()
    {
        var site = new FakeReleaseSite();
        site.Publish(1, PublishedSamples.Statement('a', "Rent cap", official: "p1"));
        await using var host = await StartAsync(site);
        await host.WaitForReleaseAsync();
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 1, PublishedAt);
        await OfficialsSeed.AddOfficialsAsync(host.OperationalDatabase,
            new SeededOfficial("p1", "Abe Ann Lee", new SeededTerm("House", "Representative", "2021-01-03") { State = "MA", District = 7, Party = "Democrat" }),
            new SeededOfficial("p2", "Ann Lee", new SeededTerm("House", "Representative", "2021-01-03") { State = "TX", District = 2, Party = "Republican" }));

        var page = await ParseAsync(await host.Client.GetAsync("/web/search?q=ann%20lee"));

        Assert.Equal(["Ann Lee", "Abe Ann Lee"], page.QuerySelectorAll(".search-official h2").Select(heading => heading.TextContent));
    }

    [Fact]
    public async Task TheSearchIssueFilterListsTheIssuesOfTheActiveReleaseByNameFromTheIssueStore()
    {
        var site = new FakeReleaseSite();
        site.Publish(1, PublishedSamples.Statement('a', "Rent cap", official: "p1"));
        await using var host = await StartAsync(site);
        await host.WaitForReleaseAsync();
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 1, PublishedAt);
        await ViewsSeed.ExecuteAsync(host.OperationalDatabase, "SET session_replication_role = replica;" +
            "INSERT INTO release_issues (release_number, issue_id, name, definition) VALUES (1, 'tax', 'Taxes', 'd'), (1, 'air', 'Air', 'd');");

        var page = await ParseAsync(await host.Client.GetAsync("/web/search?q=rent"));

        Assert.Equal(["air", "tax"], page.QuerySelectorAll("#search-issue option").Select(option => option.GetAttribute("value")).Where(value => !string.IsNullOrEmpty(value)));
    }

    [Fact]
    public async Task ASearchWithNoMatchGivesGuidanceAndABlankSearchRunsNoQuery()
    {
        var site = new FakeReleaseSite();
        site.Publish(1, PublishedSamples.Statement('a', "Rent cap", official: "p1"));
        await using var host = await StartAsync(site);
        await host.WaitForReleaseAsync();
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 1, PublishedAt);

        var none = await ParseAsync(await host.Client.GetAsync("/web/search?q=zzqxv"));
        var blank = await ParseAsync(await host.Client.GetAsync("/web/search"));

        Assert.Contains("No records match \"zzqxv\".", none.QuerySelector(".empty")!.TextContent);
        Assert.Null(blank.QuerySelector(".empty"));
        Assert.Null(blank.QuerySelector(".record-rows"));
    }
}

public sealed class SearchBillNumberTests
{
    [Theory]
    [InlineData("5334", null, 5334)]
    [InlineData("H.R. 5334", "hr", 5334)]
    [InlineData("hr5334", "hr", 5334)]
    [InlineData("S.Res. 12", "sres", 12)]
    [InlineData("S. 5334", "s", 5334)]
    [InlineData(" H. J. Res. 7 ", "hjres", 7)]
    public void AWholeQueryThatIsABillNumberGivesItsTypeAndNumber(string text, string? type, int number)
    {
        Assert.True(SearchBillNumber.TryParse(text, out var parsedType, out var parsedNumber));
        Assert.Equal(type, parsedType);
        Assert.Equal(number, parsedNumber);
    }

    [Theory]
    [InlineData("pressley")]
    [InlineData("rent 5334")]
    [InlineData("H.R. 5334 sanctions")]
    [InlineData("xyz 5334")]
    [InlineData("0")]
    [InlineData("1234567")]
    [InlineData("")]
    public void AnythingElseIsNotABillNumber(string text) => Assert.False(SearchBillNumber.TryParse(text, out _, out _));
}
