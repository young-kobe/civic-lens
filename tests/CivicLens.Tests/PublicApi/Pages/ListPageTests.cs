using System.Net;
using AngleSharp.Html.Parser;
using CivicLens.PublicApi;
using CivicLens.PublicApi.Officials;
using CivicLens.PublicApi.Pages;
using CivicLens.PublicApi.Search;
using CivicLens.PublicWeb.Publication;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.Infrastructure.Collection;
using CivicLens.Tests.PublicApi.Bills;
using CivicLens.Tests.PublicApi.Officials;
using CivicLens.Tests.PublicApi.Votes;
using CivicLens.Tests.PublicWeb;

namespace CivicLens.Tests.PublicApi.Pages;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class ListPageTests(PostgresCollection postgres)
{
    private static readonly PublicBaseUrl BaseUrl = PublicBaseUrl.Parse("https://civic-lens.test", "test")!;
    private static readonly DateTimeOffset PublishedAt = new(2026, 6, 9, 12, 0, 0, TimeSpan.Zero);

    private Task<ApiHost> StartAsync(Func<TypedReleaseSite, Task>? publish = null) =>
        ApiHost.StartAsync(postgres, options => options with { PublicBaseUrl = BaseUrl }, publish: publish);

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

        var first = await host.Client.GetAsync("/officials.html?chamber=senate");
        var firstPage = await ParseAsync(first);
        var next = firstPage.QuerySelector(".pager a[rel=next]")!.GetAttribute("href")!;
        var second = await ParseAsync(await host.Client.GetAsync("/officials.html" + next));

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(PageCache.CacheControl, string.Join(", ", first.Headers.GetValues("Cache-Control")));
        Assert.StartsWith("W/\"r1-", first.Headers.ETag!.ToString());
        Assert.Equal(OfficialsPageReader.PageSize, firstPage.QuerySelectorAll(".list-row").Length);
        Assert.StartsWith("?chamber=senate&cursor=", next);
        Assert.Equal(5, second.QuerySelectorAll(".list-row").Length);
        Assert.Null(second.QuerySelector(".pager a[rel=next]"));
        Assert.Null(firstPage.QuerySelector(".pager a[rel=prev]"));
        var previous = second.QuerySelector(".pager a[rel=prev]")!.GetAttribute("href")!;
        Assert.StartsWith("?chamber=senate&before=", previous);
        var back = await ParseAsync(await host.Client.GetAsync("/officials.html" + previous));
        Assert.Equal(firstPage.QuerySelectorAll(".list-row").Select(row => row.TextContent), back.QuerySelectorAll(".list-row").Select(row => row.TextContent));
        Assert.Null(back.QuerySelector(".pager a[rel=prev]"));
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
        var stale = new OfficialCursor(99, "Sen 01", "Sen 01", "p01").Encode();

        var response = await new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = host.Client.BaseAddress }
            .GetAsync($"/officials.html?party=democrat&cursor={stale}");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(BaseUrl.Join("officials.html?party=democrat"), response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task AnInvalidOfficialsFilterAnswers400AndAMissingReleaseAnswers503()
    {
        await using var host = await StartAsync();

        var none = await host.Client.GetAsync("/officials.html");
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 1, PublishedAt);
        var invalid = await host.Client.GetAsync("/officials.html?chamber=moon");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, none.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    [Theory]
    [InlineData("chamber=moon", "chamber")]
    [InlineData("chamber=moon&chamber=senate", "chamber")]
    [InlineData("name=first&name=second", "name")]
    [InlineData("cursor=forged", null)]
    [InlineData("before=forged", null)]
    [InlineData("cursor=forged&before=forged", null)]
    public async Task ARefusedOfficialsQueryShowsTheFormWithoutRowsOrAMisleadingNoMatchClaimAndCannotBeCached(string query, string? field)
    {
        await using var host = await StartAsync();
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 1, PublishedAt);
        await OfficialsSeed.AddOfficialsAsync(host.OperationalDatabase, Senator(1));

        using var request = new HttpRequestMessage(HttpMethod.Get, "/officials.html?" + query);
        request.Headers.TryAddWithoutValidation("If-None-Match", "*");
        using var response = await host.Client.SendAsync(request);
        var page = await ParseAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal(PageCache.FailureCacheControl, response.Headers.CacheControl!.ToString());
        Assert.Null(response.Headers.ETag);
        Assert.NotNull(page.QuerySelector("form.lists-filter"));
        Assert.Null(page.QuerySelector(".list-rows, .list-status, .pager, .empty"));
        Assert.StartsWith("1 official has a page.", page.QuerySelector(".page-header-lede")!.TextContent);
        if (field is not null) Assert.Equal("true", page.QuerySelector($"[name={field}]")!.GetAttribute("aria-invalid"));
        if (field == "name") Assert.Equal("first", page.QuerySelector("[name=name]")!.GetAttribute("value"));
        if (field == "chamber") Assert.Equal("moon", page.QuerySelector("[name=chamber] option[selected]")!.GetAttribute("value"));

        using var api = await host.Client.GetAsync("/v1/officials?" + query.Replace("before", "cursor", StringComparison.Ordinal));
        Assert.Equal(HttpStatusCode.BadRequest, api.StatusCode);
        Assert.Equal("application/problem+json", api.Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task ARefusedOfficialsQueryWithoutAnActiveReleaseUsesTheSameUnavailablePageAsOtherLists()
    {
        await using var host = await StartAsync();
        using var response = await host.Client.GetAsync("/officials.html?chamber=moon");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal(PageCache.FailureCacheControl, response.Headers.CacheControl!.ToString());
        Assert.Null(response.Headers.ETag);
        Assert.Contains(PublishedServerErrorPage.Title, (await ParseAsync(response)).QuerySelector("main")!.TextContent);
    }

    [Fact]
    public async Task TheBoardsPageRendersFourPartyPanelsWithTheReleaseETag()
    {
        await using var host = await StartAsync();
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 3, PublishedAt);

        var response = await host.Client.GetAsync("/boards.html");
        var page = await ParseAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("W/\"r3-", response.Headers.ETag!.ToString());
        Assert.Equal(4, page.QuerySelectorAll(".board-panel").Length);
        Assert.Equal("Compare the parties", page.QuerySelector("h1")!.TextContent);
    }

    [Fact]
    public async Task SearchShowsTheOfficialFirstPinsTheBillNumberAndNeverEntersASharedCache()
    {
        await using var host = await StartAsync(site => site.PublishAsync(1, PublishedSamples.Statement('a', "H.R. 5334 debate", official: "p1")));
        await host.WaitForReleaseAsync();
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 1, PublishedAt);
        await OfficialsSeed.AddOfficialsAsync(host.OperationalDatabase,
            new SeededOfficial("p1", "Ayanna Pressley", new SeededTerm("House", "Representative", "2021-01-03") { State = "MA", District = 7, Party = "Democrat" }));
        await BillsSeed.AddBillsAsync(host.OperationalDatabase, new SeededBill('1', "2026-03-03") { BillNumber = 5334, BillLine = "H.R. 5334, the Sanctions Act" });

        var byNumber = await host.Client.GetAsync("/search.html?q=5334");
        var byName = await host.Client.GetAsync("/search.html?q=Pressley");
        var number = await ParseAsync(byNumber);
        var name = await ParseAsync(byName);

        Assert.True(byNumber.Headers.CacheControl!.NoStore);
        Assert.True(byNumber.Headers.CacheControl.Private);
        Assert.Null(byNumber.Headers.ETag);
        Assert.Equal("H.R. 5334, the Sanctions Act", number.QuerySelector(".search-pin h2")!.TextContent);
        Assert.Contains("H.R. 5334 debate", number.QuerySelector(".record-rows")!.TextContent);
        Assert.Equal("1 result for \"5334\".", number.QuerySelector(".list-status")!.TextContent);
        Assert.Null(number.QuerySelector(".search-official"));
        Assert.Equal("Ayanna Pressley", name.QuerySelector(".search-official h2")!.TextContent);
        Assert.Null(name.QuerySelector(".search-pin"));
        Assert.Null((await ParseAsync(await host.Client.GetAsync("/search.html?q=Pr"))).QuerySelector(".search-official"));
        Assert.Equal("Ayanna Pressley", (await ParseAsync(await host.Client.GetAsync("/search.html?q=ayanna%20pre"))).QuerySelector(".search-official h2")!.TextContent);
    }

    [Fact]
    public async Task ThePreviousLinkOfTheSecondSearchPageGivesTheFirstPageAgainWithItsOfficialAndNoPreviousLink()
    {
        await using var host = await StartAsync(site => site.PublishAsync(1,
            PublishedSamples.Statement('a', "Pressley on rent", official: "p1"), PublishedSamples.Statement('b', "Pressley on wages", official: "p1")));
        await host.WaitForReleaseAsync();
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 1, PublishedAt);
        await OfficialsSeed.AddOfficialsAsync(host.OperationalDatabase,
            new SeededOfficial("p1", "Ayanna Pressley", new SeededTerm("House", "Representative", "2021-01-03") { State = "MA", District = 7, Party = "Democrat" }));

        var first = await ParseAsync(await host.Client.GetAsync("/search.html?q=Pressley&limit=1"));
        var second = await ParseAsync(await host.Client.GetAsync("/search.html" + first.QuerySelector(".pager a[rel=next]")!.GetAttribute("href")));
        var previous = second.QuerySelector(".pager a[rel=prev]")!.GetAttribute("href")!;
        var back = await ParseAsync(await host.Client.GetAsync("/search.html" + previous));

        Assert.Null(first.QuerySelector(".pager a[rel=prev]"));
        Assert.Null(second.QuerySelector(".search-official"));
        Assert.Null(second.QuerySelector(".pager a[rel=next]"));
        Assert.StartsWith("?q=Pressley&limit=1&before=", previous);
        Assert.Equal(first.QuerySelector(".record-rows")!.TextContent, back.QuerySelector(".record-rows")!.TextContent);
        Assert.Equal("Ayanna Pressley", back.QuerySelector(".search-official h2")!.TextContent);
        Assert.Null(back.QuerySelector(".pager a[rel=prev]"));
    }

    [Fact]
    public async Task ASearchRowShowsTheLabelThatTheIngestCopiedFromTheViewAsTheChipOfItsOrigin()
    {
        await using var host = await StartAsync(site => site.PublishAsync(1, PublishedSamples.Action("S. 5334 names a clinic", "A short account.", "Clinic act", "Xq the source text."),
            PublishedSamples.Statement('a', "H.R. 5334 debate", official: "p1")));
        await host.WaitForReleaseAsync();
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 1, PublishedAt);

        var page = await ParseAsync(await host.Client.GetAsync("/search.html?q=clinic%20OR%20debate"));
        var model = Assert.Single(page.QuerySelectorAll(".record-row"), row => row.TextContent.Contains("names a clinic", StringComparison.Ordinal));
        var person = Assert.Single(page.QuerySelectorAll(".record-row"), row => row.TextContent.Contains("debate", StringComparison.Ordinal));

        Assert.Equal("model", model.QuerySelector(".ai-label")!.GetAttribute("data-origin"));
        AiLabelAssert.IsChip(model.QuerySelector("a + .ai-label")!, AiLabelWords.ModelChip, AiLabelWords.ModelWording);
        // A person text has no label column, so its row shows no chip.
        Assert.Null(person.QuerySelector(".ai-label"));
    }

    [Fact]
    public async Task TheBillPinNamesTheSponsorLinksTheNewestHouseVoteAndNamesOnlyTheOtherBillThatIsAResult()
    {
        await using var host = await StartAsync(site => site.PublishAsync(1, PublishedSamples.Statement('a', "H.R. 5334 debate", official: "p1"), PublishedSamples.Action("S. 5334 names a clinic", "A short account.", "Clinic act", "Xq the source text.",
            billType: "s", billNumber: 5334, sponsor: "p3", sponsoredOn: new DateOnly(2026, 1, 10))));
        await host.WaitForReleaseAsync();
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 1, PublishedAt);
        await OfficialsSeed.AddOfficialsAsync(host.OperationalDatabase,
            new SeededOfficial("p1", "Jimmy Panetta", new SeededTerm("House", "Representative", "2021-01-03") { State = "CA", District = 19, Party = "Democrat" }),
            new SeededOfficial("p3", "John Cornyn", new SeededTerm("Senate", "Senator", "2021-01-03") { State = "TX", Party = "Republican" }));
        await BillsSeed.AddBillsAsync(host.OperationalDatabase,
            new SeededBill('1', "2026-03-03") { BillNumber = 5334, BillLine = "H.R. 5334, the Sanctions Act" },
            new SeededBill('6', "2026-01-15") { BillType = "hjres", BillNumber = 5334, Sponsorships = [new("p3", "Sponsor", "2026-01-10")] });
        await ViewsSeed.AddVotesAsync(host.OperationalDatabase,
            new SeededVote('3', "2026-03-01") { BillNumber = 5334, RollCall = 1 },
            new SeededVote('4', "2026-03-02") { BillNumber = 5334, RollCall = 2 },
            new SeededVote('5', "2026-03-04") { BillNumber = 5334, RollCall = 3, Chamber = "Senate" });

        var page = await ParseAsync(await host.Client.GetAsync("/search.html?q=5334"));
        var filtered = await ParseAsync(await host.Client.GetAsync("/search.html?q=5334&kind=vote"));
        var pin = page.QuerySelector(".search-pin")!;

        Assert.Equal("Bill in the 119th Congress. Sponsored by Jimmy Panetta.", pin.QuerySelector("p.muted")!.TextContent);
        var vote = Assert.Single(pin.QuerySelectorAll(".search-pin-actions a"), link => link.TextContent.EndsWith("vote on it", StringComparison.Ordinal));
        Assert.Equal("House vote on it", vote.TextContent);
        Assert.Equal("https://civic-lens.test/" + PublicationProtocol.RecordPagePath(new string('4', 64)), vote.GetAttribute("href"));
        var others = page.QuerySelector(".search-other-bills")!;
        Assert.Contains("a Senate bill introduced by John Cornyn, is also in the list.", others.TextContent);
        Assert.Equal("S. 5334", Assert.Single(others.QuerySelectorAll("a")).TextContent);
        Assert.Null(filtered.QuerySelector(".search-pin"));
        Assert.Null(filtered.QuerySelector(".search-other-bills"));
    }

    [Fact]
    public async Task TheBillPinLinksTheNewestSenateVoteWhenTheBillHasNoHouseVote()
    {
        await using var host = await StartAsync(site => site.PublishAsync(1, PublishedSamples.Statement('a', "Rent cap", official: "p1")));
        await host.WaitForReleaseAsync();
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 1, PublishedAt);
        await OfficialsSeed.AddOfficialsAsync(host.OperationalDatabase,
            new SeededOfficial("p1", "John Cornyn", new SeededTerm("Senate", "Senator", "2021-01-03") { State = "TX", Party = "Republican" }));
        await BillsSeed.AddBillsAsync(host.OperationalDatabase,
            new SeededBill('1', "2026-03-03") { BillType = "s", BillNumber = 77, Sponsorships = [new("p1", "Sponsor", "2026-01-10")] });
        await ViewsSeed.AddVotesAsync(host.OperationalDatabase,
            new SeededVote('3', "2026-03-04") { BillType = "s", BillNumber = 77, RollCall = 1, Chamber = "Senate" },
            new SeededVote('4', "2026-03-05") { BillType = "s", BillNumber = 77, RollCall = 2, Chamber = "Senate" });

        var pin = (await ParseAsync(await host.Client.GetAsync("/search.html?q=S.%2077"))).QuerySelector(".search-pin")!;

        var vote = Assert.Single(pin.QuerySelectorAll(".search-pin-actions a"), link => link.TextContent.EndsWith("vote on it", StringComparison.Ordinal));
        Assert.Equal("Senate vote on it", vote.TextContent);
        Assert.Equal("https://civic-lens.test/" + PublicationProtocol.RecordPagePath(new string('4', 64)), vote.GetAttribute("href"));
    }

    [Fact]
    public async Task SearchShowsThreeOfficialsAndLinksTheOfficialsListWhenAFourthMatches()
    {
        await using var host = await StartAsync(site => site.PublishAsync(1, PublishedSamples.Statement('a', "Rent cap", official: "p1")));
        await host.WaitForReleaseAsync();
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 1, PublishedAt);
        await OfficialsSeed.AddOfficialsAsync(host.OperationalDatabase, [.. new[] { "Ann", "Bo", "Cy", "Di" }.Select((first, index) =>
            new SeededOfficial($"p{index}", $"{first} Leeds", new SeededTerm("House", "Representative", "2021-01-03") { State = "MA", District = index + 1, Party = "Democrat" }))]);

        var four = await ParseAsync(await host.Client.GetAsync("/search.html?q=leeds"));
        var one = await ParseAsync(await host.Client.GetAsync("/search.html?q=ann%20leeds"));

        Assert.Equal(3, four.QuerySelectorAll(".search-official").Length);
        var more = four.QuerySelector(".search-more-officials")!;
        Assert.Equal("More officials match. See all of them in the officials list.", more.TextContent);
        Assert.Equal("https://civic-lens.test/" + PublicationProtocol.OfficialsPagePath + "?name=leeds", more.QuerySelector("a")!.GetAttribute("href"));
        var list = await ParseAsync(await host.Client.GetAsync("/officials.html?name=leeds"));
        Assert.Equal(4, list.QuerySelectorAll(".list-row").Length);
        Assert.Single(one.QuerySelectorAll(".search-official"));
        Assert.Null(one.QuerySelector(".search-more-officials"));
    }

    [Fact]
    public async Task SearchPutsAnOfficialWhoseWholeNameEqualsTheTextBeforeAnEarlierNameThatOnlyContainsItAsAWord()
    {
        await using var host = await StartAsync(site => site.PublishAsync(1, PublishedSamples.Statement('a', "Rent cap", official: "p1")));
        await host.WaitForReleaseAsync();
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 1, PublishedAt);
        await OfficialsSeed.AddOfficialsAsync(host.OperationalDatabase,
            new SeededOfficial("p1", "Abe Ann Lee", new SeededTerm("House", "Representative", "2021-01-03") { State = "MA", District = 7, Party = "Democrat" }),
            new SeededOfficial("p2", "Ann Lee", new SeededTerm("House", "Representative", "2021-01-03") { State = "TX", District = 2, Party = "Republican" }));

        var page = await ParseAsync(await host.Client.GetAsync("/search.html?q=ann%20lee"));

        Assert.Equal(["Ann Lee", "Abe Ann Lee"], page.QuerySelectorAll(".search-official h2").Select(heading => heading.TextContent));
    }

    [Fact]
    public async Task TheSearchIssueFilterListsTheIssuesOfTheActiveReleaseByNameFromTheIssueStore()
    {
        await using var host = await StartAsync(site => site.PublishAsync(1, PublishedSamples.Statement('a', "Rent cap", official: "p1")));
        await host.WaitForReleaseAsync();
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 1, PublishedAt);
        await ViewsSeed.ExecuteAsync(host.OperationalDatabase, "SET session_replication_role = replica;" +
            "INSERT INTO release_issues (release_number, issue_id, name, definition) VALUES (1, 'tax', 'Taxes', 'd'), (1, 'air', 'Air', 'd');");

        var page = await ParseAsync(await host.Client.GetAsync("/search.html?q=rent"));

        Assert.Equal(["air", "tax"], page.QuerySelectorAll("#search-issue option").Select(option => option.GetAttribute("value")).Where(value => !string.IsNullOrEmpty(value)));
    }

    [Fact]
    public async Task ASearchWithNoMatchGivesGuidanceAndABlankSearchRunsNoQuery()
    {
        await using var host = await StartAsync(site => site.PublishAsync(1, PublishedSamples.Statement('a', "Rent cap", official: "p1")));
        await host.WaitForReleaseAsync();
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 1, PublishedAt);

        var none = await ParseAsync(await host.Client.GetAsync("/search.html?q=zzqxv"));
        var blank = await ParseAsync(await host.Client.GetAsync("/search.html"));

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
