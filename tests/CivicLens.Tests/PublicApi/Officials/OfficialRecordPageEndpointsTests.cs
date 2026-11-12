using System.Globalization;
using System.Net;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using CivicLens.PublicWeb.Publication;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.Fixtures;
using CivicLens.Tests.Infrastructure.Collection;
using CivicLens.Tests.PublicApi.Bills;
using CivicLens.Tests.PublicApi.Home;
using CivicLens.Tests.PublicApi.Statements;
using CivicLens.Tests.PublicApi.Votes;

namespace CivicLens.Tests.PublicApi.Officials;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class OfficialRecordPageEndpointsTests(PostgresCollection postgres)
{
    private static readonly PublicBaseUrl BaseUrl = PublicBaseUrl.Parse("https://civic-lens.test", "test")!;

    private static readonly SeededOfficial[] Officials =
    [
        new("p0", "Ann Able", new SeededTerm("Representative", "Representative", "2021-01-03") { State = "MA", District = 7, Party = "Democrat", EndsOn = "2023-01-03" },
            new SeededTerm("Senator", "Senator", "2023-01-03") { State = "MA", Party = "Democrat" }),
        new("p1", "Ben Baker", new SeededTerm("Representative", "Representative", "2025-01-03") { State = "TX", District = 3, Party = "Republican" }),
        new("president", "Eve East", new SeededTerm("President", "Executive", "2025-01-20") { Party = "Republican" }),
        new("bare", "Cy Cole")
    ];

    private async Task<ApiHost> StartAsync()
    {
        var host = await ApiHost.StartAsync(postgres, new FakeReleaseSite(), options => options with { PublicBaseUrl = BaseUrl });
        var database = host.OperationalDatabase;
        await ViewsSeed.ActivateReleaseAsync(database, 1, new DateTimeOffset(2026, 6, 2, 0, 0, 0, TimeSpan.Zero));
        await OfficialsSeed.AddOfficialsAsync(database, Officials);
        return host;
    }

    private static string Id(int number) => number.ToString("x64", CultureInfo.InvariantCulture);

    // Twenty-seven votes of p0 on 27 days, so the list needs a second page. The newest vote (index 26) comes first.
    private static async Task AddManyVotesAsync(ApiHost host) =>
        await ViewsSeed.AddVotesAsync(host.OperationalDatabase, [.. Enumerable.Range(0, 27).Select(number => new SeededVote("0123456789abcdef"[number % 16],
            new DateOnly(2026, 1, 1).AddDays(number).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
        { Casts = ["Yea", "Nay"], PersonIds = ["p0", "p1"], RollCall = number + 1, RecordId = Id(number) })]);

    private static async Task<IDocument> GetPageAsync(ApiHost host, string path, HttpStatusCode expected = HttpStatusCode.OK)
    {
        using var response = await host.Client.GetAsync(path);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == expected, $"{path} gave {(int)response.StatusCode}: {body}");
        return await new HtmlParser().ParseDocumentAsync(body);
    }

    private static string[] Tabs(IDocument page) => [.. page.QuerySelectorAll(".segmented-item").Select(tab => tab.TextContent.Trim())];

    [Fact]
    public async Task TheVotesTabListsTheVotesOfTheOfficialNewestFirstAsSharedSingleMemberRowsWithTheirCast()
    {
        await using var host = await StartAsync();
        await ViewsSeed.AddVotesAsync(host.OperationalDatabase,
            new SeededVote('a', "2026-02-01") { Casts = ["Nay", "Yea"], PersonIds = ["p0", "p1"], RollCall = 1 },
            new SeededVote('b', "2026-02-03") { Casts = ["Yea"], PersonIds = ["p1"], RollCall = 2 },
            new SeededVote('c', "2026-02-05") { Casts = ["Yea", "Nay"], PersonIds = ["p0", "p1"], RollCall = 3 });

        var page = await GetPageAsync(host, "/web/officials/p0/record.html");

        Assert.Equal("All records of Ann Able", page.QuerySelector("h1")!.TextContent);
        Assert.Equal(["Votes2"], Tabs(page));
        Assert.Contains("This list holds 2 votes of Ann Able in this release", page.QuerySelector(".records-lead p")!.TextContent);
        var rows = page.QuerySelectorAll(".shared-vote-row").ToArray();
        Assert.Equal([BaseUrl.Join($"records/{new string('c', 64)}.html"), BaseUrl.Join($"records/{new string('a', 64)}.html")],
            rows.Select(row => row.QuerySelector("a")!.GetAttribute("href")!).ToArray());
        Assert.Equal(["Yes (Yea)", "No (Nay)"], rows.Select(row => row.QuerySelector(".shared-vote-cast")!.TextContent.Trim().Replace("Ann Able", "").Trim()).ToArray());
        Assert.Equal(BaseUrl.Join("officials/p0.html"), page.QuerySelector(".page-header-meta a")!.GetAttribute("href"));
    }

    [Fact]
    public async Task TheListPagesForwardByTwentyFiveWithOneCursorAndTheLastPageHasNoNextLink()
    {
        await using var host = await StartAsync();
        await AddManyVotesAsync(host);

        var first = await GetPageAsync(host, "/web/officials/p0/record.html");
        var next = first.QuerySelector(".pager a[rel=next]")!.GetAttribute("href")!;
        var second = await GetPageAsync(host, next.Replace("https://civic-lens.test/", "/web/"));

        Assert.Equal(25, first.QuerySelectorAll(".shared-vote-row").Length);
        Assert.Contains("kind=votes&cursor=r.1.", next);
        Assert.Equal(2, second.QuerySelectorAll(".shared-vote-row").Length);
        Assert.Null(second.QuerySelector(".pager a[rel=next]"));
        var seen = first.QuerySelectorAll(".shared-vote-row h3 a").Concat(second.QuerySelectorAll(".shared-vote-row h3 a")).Select(link => link.GetAttribute("href")!).ToArray();
        Assert.Equal(27, seen.Distinct().Count());
        Assert.Equal(BaseUrl.Join($"records/{Id(26)}.html"), seen[0]);
        Assert.Equal(BaseUrl.Join($"records/{Id(0)}.html"), seen[^1]);
    }

    [Fact]
    public async Task ACursorOfAnotherReleaseRedirectsToTheFirstPageOfTheSameKind()
    {
        await using var host = await StartAsync();
        await AddManyVotesAsync(host);

        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = host.Client.BaseAddress };
        using var response = await client.GetAsync($"/web/officials/p0/record.html?kind=votes&cursor=r.7.20260110.{Id(3)}");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/web/officials/p0/record.html?kind=votes", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task TheBillsTabListsOnlyTheBillsTheOfficialSponsoredWithTheStatusBadgeAndTheBillLineNote()
    {
        await using var host = await StartAsync();
        await BillsSeed.AddBillsAsync(host.OperationalDatabase,
            new SeededBill('1', "2026-03-02") { BillNumber = 1, Sponsorships = [new("p0", "Sponsor", "2026-01-10")] },
            new SeededBill('2', "2026-03-03") { BillNumber = 2, BillLine = "Pays landlords.", Sponsorships = [new("p0", "Sponsor", "2026-03-03"), new("p1", "Cosponsor", "2026-03-03")] },
            new SeededBill('3', "2026-03-04") { BillNumber = 3, Sponsorships = [new("p1", "Sponsor", "2026-03-04")] });

        var page = await GetPageAsync(host, "/web/officials/p0/record.html?kind=bills");

        Assert.Equal(["Bills2"], Tabs(page));
        Assert.Equal([BaseUrl.Join($"records/{new string('2', 64)}.html"), BaseUrl.Join($"records/{new string('1', 64)}.html")],
            page.QuerySelectorAll(".record-row > div > a").Select(link => link.GetAttribute("href")!).ToArray());
        Assert.Contains("Pays landlords.", page.Body!.TextContent);
        Assert.Contains("Introduced 3 Mar 2026", page.QuerySelector(".record-row")!.TextContent);
        Assert.Contains("2 bills and resolutions that Ann Able sponsored in this release", page.QuerySelector(".records-lead p")!.TextContent);
    }

    [Fact]
    public async Task TheStatementsTabListsTheStatementsThatNameTheOfficialAsASpeakerWithTheLabelOfTheViewAndTheirDates()
    {
        await using var host = await StartAsync();
        await StatementsSeed.AddAsync(host.OperationalDatabase, new SeededStatement('5', "2026-02-10", ["p0"], [], "Analysis", "Code"),
            new SeededStatement('6', null, ["p0"], [], "Person", "Person"), new SeededStatement('7', "2026-02-12", ["p1"], [], "Person", "Person"));

        var page = await GetPageAsync(host, "/web/officials/p0/record.html?kind=statements");

        Assert.Equal(["Statements2"], Tabs(page));
        var rows = page.QuerySelectorAll(".record-row").ToArray();
        Assert.Equal([BaseUrl.Join($"records/{new string('5', 64)}.html"), BaseUrl.Join($"records/{new string('6', 64)}.html")],
            rows.Select(row => row.QuerySelector("a")!.GetAttribute("href")!).ToArray());
        Assert.Contains("Said on 10 Feb 2026", rows[0].TextContent);
        Assert.Contains("Date not stated", rows[1].TextContent);
    }

    [Fact]
    public async Task TheExecutiveActionsTabListsTheActionsThePresidentSignedNewestFirstWithTheirKind()
    {
        await using var host = await StartAsync();
        await HomeSeed.AddExecutiveActionsAsync(host.OperationalDatabase,
            new SeededExecutiveAction('e', "ExecutiveOrder", "2026-05-20", "2026-05-21") { Title = "Newer order" },
            new SeededExecutiveAction('9', "Proclamation", "2026-05-18", "2026-05-19") { Title = "A proclamation" });
        await ViewsSeed.ExecuteAsync(host.OperationalDatabase, string.Join('\n', new[] { ('e', "ExecutiveOrder"), ('9', "Proclamation") }.Select(action => $"""
            SET session_replication_role = replica;
            INSERT INTO published_entries (kind, record_id, revision_number, executive_action_record_id, executive_action_revision_number, headline, document_type)
              VALUES ('executiveAction', '{new string(action.Item1, 64)}', 1, '{new string(action.Item1, 64)}', 1, 'H', '{action.Item2}');
            INSERT INTO published_entry_officials (kind, record_id, revision_number, "position", official_id)
              VALUES ('executiveAction', '{new string(action.Item1, 64)}', 1, 0, 'president');
            """)));

        var page = await GetPageAsync(host, "/web/officials/president/record.html");

        Assert.Equal(["Executive actions2"], Tabs(page));
        Assert.Equal(["Newer order", "A proclamation"], page.QuerySelectorAll(".record-row > div > a > span:first-child").Select(title => title.TextContent).ToArray());
        Assert.Contains("Executive order, signed 20 May 2026", page.QuerySelector(".record-row")!.TextContent);
    }

    [Fact]
    public async Task TheDefaultTabIsTheFirstKindWithRecordsAndAKindWithNoRecordsFallsBackToIt()
    {
        await using var host = await StartAsync();
        await StatementsSeed.AddAsync(host.OperationalDatabase, new SeededStatement('5', "2026-02-10", ["p0"], [], "Analysis", "Code"));

        var plain = await GetPageAsync(host, "/web/officials/p0/record.html");
        var asked = await GetPageAsync(host, "/web/officials/p0/record.html?kind=bills");

        Assert.Equal("Statements1", Tabs(plain).Single());
        Assert.Equal("true", plain.QuerySelector(".segmented-item")!.GetAttribute("aria-current") ?? "true");
        Assert.Equal(Tabs(plain), Tabs(asked));
        Assert.Single(asked.QuerySelectorAll(".record-row"));
    }

    [Fact]
    public async Task AnOfficialWithNoRecordsAnswers200WithTheEmptyStateAndNoTabsAndStillShowsTheOffices()
    {
        await using var host = await StartAsync();

        var page = await GetPageAsync(host, "/web/officials/p1/record.html");

        Assert.Contains("No records yet", page.QuerySelector(".empty")!.TextContent);
        Assert.Empty(page.QuerySelectorAll(".segmented-item"));
        Assert.Null(page.QuerySelector(".pager"));
        Assert.Contains("Offices held (1 term)", page.QuerySelector("#offices summary")!.TextContent);
    }

    [Fact]
    public async Task OfficesHeldListsEachDatedTermOldestFirstWithItsPartyAndAnOfficialWithNoTermHasNoFold()
    {
        await using var host = await StartAsync();

        var held = await GetPageAsync(host, "/web/officials/p0/record.html");
        var none = await GetPageAsync(host, "/web/officials/bare/record.html");

        Assert.Equal("Offices held (2 terms)", held.QuerySelector("#offices summary")!.TextContent);
        Assert.Equal(["Representative for MA, district 7, Democrat, 3 Jan 2021 to 3 Jan 2023", "Senator for MA, Democrat, 3 Jan 2023 to Present"],
            held.QuerySelectorAll("#offices li").Select(item => item.TextContent).ToArray());
        Assert.Null(none.QuerySelector("#offices"));
        Assert.Equal("All records of Cy Cole", none.QuerySelector("h1")!.TextContent);
    }

    [Theory]
    [InlineData("/web/officials/nobody/record.html", HttpStatusCode.NotFound)]
    [InlineData("/web/officials/bad%20id/record.html", HttpStatusCode.NotFound)]
    [InlineData("/web/officials/p0/record.html?kind=letters", HttpStatusCode.BadRequest)]
    [InlineData("/web/officials/p0/record.html?cursor=nonsense", HttpStatusCode.BadRequest)]
    public async Task AnUnknownOfficialAnswers404AndABadKindOrCursorAnswers400(string path, HttpStatusCode expected)
    {
        await using var host = await StartAsync();

        using var response = await host.Client.GetAsync(path);

        Assert.Equal(expected, response.StatusCode);
    }
}
