using System.Net;
using System.Text.Json;
using CivicLens.Tests.Infrastructure.Collection;
using CivicLens.Tests.PublicApi.Votes;

namespace CivicLens.Tests.PublicApi.Bills;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class BillEndpointsTests(PostgresCollection postgres)
{
    private static readonly SeededSponsorship[] ManySponsorships =
    [
        new("p1", "Sponsor", "2026-01-10"), new("p2", "Cosponsor", "2026-01-10"), new("p3", "Cosponsor", "2026-02-01", "2026-02-20", false),
        new("p4", "Cosponsor", "2026-02-02", null, false), new("p5", "Cosponsor", "2026-02-03", null, false)
    ];

    // The order is event date desc, then record ID. A, B and C share one event date, so only the record ID orders them.
    private static readonly SeededBill E = new('e', "2026-03-03")
    {
        BillType = "hres",
        BillNumber = 9,
        Issues = ["health"],
        Sponsorships = [new("p4", "Sponsor", "2026-03-03")]
    };
    private static readonly SeededBill A = new('a', "2026-03-02")
    {
        Issues = ["health", "tax"],
        UntrackedIssues = ["zoning"],
        Sponsorships = ManySponsorships
    };
    private static readonly SeededBill B = new('b', "2026-03-02")
    {
        BillType = "s",
        BillNumber = 3,
        PolicyAreaId = "tax",
        TextOrigin = "Person",
        Sponsorships = [new("p2", "Sponsor", "2026-03-02")]
    };
    private static readonly SeededBill C = new('c', "2026-03-02")
    {
        Congress = 118,
        PolicyAreaId = null,
        TextOrigin = "Code",
        Sponsorships = [new("p1", "Sponsor", "2026-03-02")]
    };
    private static readonly SeededBill F = new('f', "2026-02-20") { BillNumber = 1 };
    private static readonly SeededBill[] All = [F, C, B, A, E];
    private static readonly char[] ExpectedOrder = ['e', 'a', 'b', 'c', 'f'];

    private async Task<ApiHost> StartWithBillsAsync(params SeededBill[] bills)
    {
        var host = await ApiHost.StartAsync(postgres, new FakeReleaseSite());
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 1, new DateTimeOffset(2026, 6, 2, 0, 0, 0, TimeSpan.Zero));
        await BillsSeed.AddBillsAsync(host.OperationalDatabase, bills);
        return host;
    }

    private static async Task<JsonDocument> GetAsync(ApiHost host, string path, HttpStatusCode expected = HttpStatusCode.OK)
    {
        using var response = await host.Client.GetAsync(path);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == expected, $"{path} gave {(int)response.StatusCode}: {body}");
        return JsonDocument.Parse(body);
    }

    private static char[] Ids(JsonDocument page) =>
        [.. page.RootElement.GetProperty("bills").EnumerateArray().Select(bill => bill.GetProperty("recordId").GetString()![0])];

    private static int[] Positions(JsonDocument page) =>
        [.. page.RootElement.GetProperty("sponsorships").EnumerateArray().Select(item => item.GetProperty("position").GetInt32())];

    private static Task ActivateReleaseTwoAsync(ApiHost host) =>
        ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 2, new DateTimeOffset(2026, 6, 9, 0, 0, 0, TimeSpan.Zero));

    private static async Task AssertBadRequestAsync(ApiHost host, string path)
    {
        using var response = await host.Client.GetAsync(path);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("invalid request", problem.RootElement.GetProperty("title").GetString());
    }

    private static async Task AssertReleaseChangedAsync(ApiHost host, string path)
    {
        using var response = await host.Client.GetAsync(path);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("the release changed", problem.RootElement.GetProperty("title").GetString());
        Assert.Equal(2, problem.RootElement.GetProperty("release").GetInt32());
        Assert.Contains("active release is 2", problem.RootElement.GetProperty("detail").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task BillsComeNewestEventFirstAndATieOnTheEventDateFallsBackToTheRecordIdWithTheReleaseOfTheSameQuery()
    {
        await using var host = await StartWithBillsAsync(All);

        using var page = await GetAsync(host, "/v1/bills");

        Assert.Equal(ExpectedOrder, Ids(page));
        Assert.Equal(1, page.RootElement.GetProperty("release").GetInt32());
        Assert.Equal(JsonValueKind.Null, page.RootElement.GetProperty("nextCursor").ValueKind);
    }

    [Fact]
    public async Task ABillRowShowsTheViewFieldsInTheWordingOfTheReleaseFiles()
    {
        await using var host = await StartWithBillsAsync(A);

        using var page = await GetAsync(host, "/v1/bills");

        var bill = Assert.Single(page.RootElement.GetProperty("bills").EnumerateArray());
        Assert.Equal(new string('a', 64), bill.GetProperty("recordId").GetString());
        Assert.Equal(1, bill.GetProperty("revisionNumber").GetInt32());
        Assert.Equal(119, bill.GetProperty("congress").GetInt32());
        Assert.Equal("hr", bill.GetProperty("billType").GetString());
        Assert.Equal(7, bill.GetProperty("billNumber").GetInt32());
        Assert.Equal("A housing bill", bill.GetProperty("billTitle").GetString());
        Assert.Equal($"Headline {A.RecordId}", bill.GetProperty("headline").GetString());
        Assert.Equal($"Summary {A.RecordId}", bill.GetProperty("summary").GetString());
        Assert.Equal("model", bill.GetProperty("textOrigin").GetString());
        Assert.Equal("An AI summary of the official record", bill.GetProperty("label").GetString());
        Assert.Equal("Health", bill.GetProperty("policyArea").GetString());
        Assert.Equal("health", bill.GetProperty("policyAreaId").GetString());
        Assert.Equal("2026-03-02", bill.GetProperty("eventDate").GetString());
        Assert.Equal(1, bill.GetProperty("sponsorCount").GetInt32());
        Assert.Equal(4, bill.GetProperty("cosponsorCount").GetInt32());
    }

    [Fact]
    public async Task APersonTextHasNoLabelAndABillWithNoPolicyAreaLeavesItNullBecauseTheViewShowsOnlyWhatTheRevisionHolds()
    {
        await using var host = await StartWithBillsAsync(B, C);

        using var page = await GetAsync(host, "/v1/bills");

        var person = page.RootElement.GetProperty("bills")[0];
        var code = page.RootElement.GetProperty("bills")[1];
        Assert.Equal("person", person.GetProperty("textOrigin").GetString());
        Assert.Equal(JsonValueKind.Null, person.GetProperty("label").ValueKind);
        Assert.Equal("Written automatically from Congress.gov data, with no AI", code.GetProperty("label").GetString());
        Assert.Equal(JsonValueKind.Null, code.GetProperty("policyArea").ValueKind);
        Assert.Equal(JsonValueKind.Null, code.GetProperty("policyAreaId").ValueKind);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(50)]
    public async Task FollowingTheCursorsGivesEveryBillOnceInTheSameOrderAtAnyPageSizeEvenWhenAPageEndsInsideATie(int limit)
    {
        await using var host = await StartWithBillsAsync(All);
        var seen = new List<char>();
        string? cursor = null;
        var pages = 0;

        do
        {
            using var page = await GetAsync(host, $"/v1/bills?limit={limit}" + (cursor is null ? "" : "&cursor=" + cursor));
            var ids = Ids(page);
            Assert.InRange(ids.Length, 1, limit);
            seen.AddRange(ids);
            cursor = page.RootElement.GetProperty("nextCursor").GetString();
            Assert.Equal(1, page.RootElement.GetProperty("release").GetInt32());
            pages++;
        }
        while (cursor is not null && pages < 20);

        Assert.Equal(ExpectedOrder, seen);
        Assert.Equal((5 + limit - 1) / limit, pages);
    }

    [Fact]
    public async Task TheDefaultPageHolds20BillsAndThePageSizeCapIs50()
    {
        var bills = Enumerable.Range(0, 55).Select(i => new SeededBill('a', "2026-01-01") { RecordId = i.ToString("x64"), BillNumber = i + 1 }).ToArray();
        await using var host = await StartWithBillsAsync(bills);

        using var first = await GetAsync(host, "/v1/bills");
        using var capped = await GetAsync(host, "/v1/bills?limit=50");

        Assert.Equal(20, first.RootElement.GetProperty("bills").GetArrayLength());
        Assert.NotEqual(JsonValueKind.Null, first.RootElement.GetProperty("nextCursor").ValueKind);
        Assert.Equal(50, capped.RootElement.GetProperty("bills").GetArrayLength());
        Assert.NotEqual(JsonValueKind.Null, capped.RootElement.GetProperty("nextCursor").ValueKind);
    }

    [Theory]
    [InlineData("congress=118", "c")]
    [InlineData("congress=119", "eabf")]
    [InlineData("billType=hr", "acf")]
    [InlineData("billType=HR&billNumber=7", "ac")]
    [InlineData("billType=hr&billNumber=7&congress=118", "c")]
    [InlineData("billNumber=3", "b")]
    [InlineData("sponsor=p1", "acf")]
    [InlineData("sponsor=p2", "abf")]
    [InlineData("sponsor=p4", "ea")]
    [InlineData("sponsor=nobody", "")]
    [InlineData("issue=health", "ea")]
    [InlineData("issue=tax", "a")]
    [InlineData("issue=zoning", "")]
    [InlineData("policyArea=health", "eaf")]
    [InlineData("policyArea=tax", "b")]
    [InlineData("from=2026-03-02&to=2026-03-02", "abc")]
    [InlineData("from=2026-03-03", "e")]
    [InlineData("to=2026-02-28", "f")]
    [InlineData("from=2026-03-02&to=2026-03-02&sponsor=p1&congress=119", "a")]
    public async Task EveryFilterNarrowsTheListAndKeepsTheOrder(string query, string expected)
    {
        await using var host = await StartWithBillsAsync(All);

        using var page = await GetAsync(host, "/v1/bills?" + query);

        Assert.Equal(expected.ToCharArray(), Ids(page));
        Assert.Equal(1, page.RootElement.GetProperty("release").GetInt32());
    }

    [Fact]
    public async Task AFilterAndACursorWorkTogetherBecauseTheCursorMovesOnlyThroughTheFilteredRows()
    {
        await using var host = await StartWithBillsAsync(All);
        using var first = await GetAsync(host, "/v1/bills?sponsor=p1&limit=1");
        var cursor = first.RootElement.GetProperty("nextCursor").GetString();

        using var second = await GetAsync(host, $"/v1/bills?sponsor=p1&limit=5&cursor={cursor}");

        Assert.Equal(['a'], Ids(first));
        Assert.Equal(['c', 'f'], Ids(second));
    }

    [Theory]
    [InlineData("congress=0")]
    [InlineData("congress=x")]
    [InlineData("billNumber=0")]
    [InlineData("from=2026-13-01")]
    [InlineData("to=yesterday")]
    [InlineData("from=2026-03-02&to=2026-03-01")]
    [InlineData("limit=0")]
    [InlineData("limit=51")]
    [InlineData("limit=x")]
    [InlineData("congress=1&congress=2")]
    [InlineData("issue=a&issue=b")]
    [InlineData("cursor=nonsense")]
    [InlineData("cursor=b.1.20260302")]
    [InlineData("cursor=b.0.20260302.aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("cursor=b.1.20261302.aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("cursor=b.1.20260302.not-an-id")]
    [InlineData("cursor=v.1.20260302.aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public async Task BadParametersAndForgedCursorsAreRefusedWith400AsProblemJsonBecauseNoQueryMayRunOnThem(string query)
    {
        await using var host = await StartWithBillsAsync(All);

        await AssertBadRequestAsync(host, "/v1/bills?" + query);
    }

    [Theory]
    [InlineData("issue")]
    [InlineData("sponsor")]
    [InlineData("policyArea")]
    public async Task AFilterValueLongerThanTheFilterLimitIsRefusedWith400BecauseNoRecordCanCarryIt(string filter)
    {
        await using var host = await StartWithBillsAsync(All);

        await AssertBadRequestAsync(host, $"/v1/bills?{filter}={new string('x', 257)}");
    }

    [Fact]
    public async Task ACursorWithAWellFormedKeyOnlyMovesTheWindowBecauseEveryRowIsPublicAnyway()
    {
        await using var host = await StartWithBillsAsync(All);
        var forged = $"b.1.20260302.{new string('b', 64)}";

        using var page = await GetAsync(host, "/v1/bills?cursor=" + forged);

        Assert.Equal(['c', 'f'], Ids(page));
    }

    [Fact]
    public async Task ACursorOfAnOlderReleaseAnswers409WithTheActiveReleaseAndANewWalkReadsTheNewRelease()
    {
        await using var host = await StartWithBillsAsync(All);
        using var first = await GetAsync(host, "/v1/bills?limit=2");
        var cursor = first.RootElement.GetProperty("nextCursor").GetString();
        await ActivateReleaseTwoAsync(host);

        await AssertReleaseChangedAsync(host, "/v1/bills?limit=2&cursor=" + cursor);
        using var restart = await GetAsync(host, "/v1/bills?limit=2");

        Assert.Equal(2, restart.RootElement.GetProperty("release").GetInt32());
        Assert.Equal(['e', 'a'], Ids(restart));
    }

    [Fact]
    public async Task ARecordThatLeftTheActiveReleaseIsNeitherListedNorFound()
    {
        var retired = new SeededBill('9', "2026-03-05") { LastRelease = 1 };
        await using var host = await StartWithBillsAsync(E, retired);
        await ActivateReleaseTwoAsync(host);

        using var page = await GetAsync(host, "/v1/bills");
        using var missing = await host.Client.GetAsync($"/v1/bills/{retired.RecordId}");
        using var noSponsorships = await host.Client.GetAsync($"/v1/bills/{retired.RecordId}/sponsorships");

        Assert.Equal(['e'], Ids(page));
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, noSponsorships.StatusCode);
    }

    [Fact]
    public async Task WithNoActiveReleaseTheListAnswers503AndALookupAnswers404()
    {
        await using var host = await ApiHost.StartAsync(postgres, new FakeReleaseSite());
        var id = new string('e', 64);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await host.Client.GetAsync("/v1/bills")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await host.Client.GetAsync($"/v1/bills/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await host.Client.GetAsync($"/v1/bills/{id}/sponsorships")).StatusCode);
    }

    [Fact]
    public async Task AFilterThatMatchesNoBillGivesAnEmptyListThatNamesTheReleaseBecauseTheEmptyPageIsStillAnAnswer()
    {
        await using var host = await StartWithBillsAsync(E);

        using var page = await GetAsync(host, "/v1/bills?congress=100");

        Assert.Empty(page.RootElement.GetProperty("bills").EnumerateArray());
        Assert.Equal(1, page.RootElement.GetProperty("release").GetInt32());
        Assert.Equal(JsonValueKind.Null, page.RootElement.GetProperty("nextCursor").ValueKind);
    }

    [Fact]
    public async Task OneBillShowsItsRowAndItsTrackedIssuesInOrderAndNeverAnUntrackedOne()
    {
        await using var host = await StartWithBillsAsync(All);

        using var withIssues = await GetAsync(host, $"/v1/bills/{A.RecordId}");
        using var withoutIssues = await GetAsync(host, $"/v1/bills/{B.RecordId}");

        Assert.Equal(1, withIssues.RootElement.GetProperty("release").GetInt32());
        Assert.Equal(A.RecordId, withIssues.RootElement.GetProperty("bill").GetProperty("recordId").GetString());
        Assert.Equal(["health", "tax"], withIssues.RootElement.GetProperty("issueIds").EnumerateArray().Select(issue => issue.GetString()));
        Assert.Empty(withoutIssues.RootElement.GetProperty("issueIds").EnumerateArray());
    }

    [Theory]
    [InlineData("0000000000000000000000000000000000000000000000000000000000000000")]
    [InlineData("nonsense")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public async Task AnUnknownOrMalformedRecordAnswers404AsProblemJsonOnTheDetailAndTheSponsorshipRoute(string recordId)
    {
        await using var host = await StartWithBillsAsync(All);

        foreach (var path in new[] { "/v1/bills/" + recordId, $"/v1/bills/{recordId}/sponsorships" })
        {
            using var response = await host.Client.GetAsync(path);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal("record not found", problem.RootElement.GetProperty("title").GetString());
        }
    }

    [Fact]
    public async Task ARecordOfAnotherKindIsNotABillBecauseTheViewsShowActionsOnly()
    {
        await using var host = await StartWithBillsAsync(A);
        await ViewsSeed.AddVotesAsync(host.OperationalDatabase, new SeededVote('d', "2026-03-02"));

        using var response = await host.Client.GetAsync($"/v1/bills/{new string('d', 64)}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task SponsorshipsComeInThePositionOrderOfTheReleaseWithTheOfficialIdAndNoBioguideId()
    {
        await using var host = await StartWithBillsAsync(A);

        using var page = await GetAsync(host, $"/v1/bills/{A.RecordId}/sponsorships");

        Assert.Equal([0, 1, 2, 3, 4], Positions(page));
        Assert.Equal(1, page.RootElement.GetProperty("release").GetInt32());
        Assert.Equal(JsonValueKind.Null, page.RootElement.GetProperty("nextCursor").ValueKind);
        var first = page.RootElement.GetProperty("sponsorships")[0];
        var withdrawn = page.RootElement.GetProperty("sponsorships")[2];
        Assert.Equal(["position", "officialId", "role", "isOriginal", "sponsoredOn", "withdrawnOn"], first.EnumerateObject().Select(property => property.Name));
        Assert.Equal(("p1", "sponsor", true, "2026-01-10", JsonValueKind.Null),
            (first.GetProperty("officialId").GetString(), first.GetProperty("role").GetString(), first.GetProperty("isOriginal").GetBoolean(),
                first.GetProperty("sponsoredOn").GetString(), first.GetProperty("withdrawnOn").ValueKind));
        Assert.Equal(("p3", "cosponsor", false, "2026-02-20"),
            (withdrawn.GetProperty("officialId").GetString(), withdrawn.GetProperty("role").GetString(), withdrawn.GetProperty("isOriginal").GetBoolean(),
                withdrawn.GetProperty("withdrawnOn").GetString()));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(5)]
    public async Task FollowingTheSponsorshipCursorsGivesEveryPositionOnceAtAnyPageSize(int limit)
    {
        await using var host = await StartWithBillsAsync(A);
        var seen = new List<int>();
        string? cursor = null;

        do
        {
            using var page = await GetAsync(host, $"/v1/bills/{A.RecordId}/sponsorships?limit={limit}" + (cursor is null ? "" : "&cursor=" + cursor));
            seen.AddRange(Positions(page));
            cursor = page.RootElement.GetProperty("nextCursor").GetString();
        }
        while (cursor is not null && seen.Count < 20);

        Assert.Equal([0, 1, 2, 3, 4], seen);
    }

    [Theory]
    [InlineData("role=sponsor", new[] { 0 })]
    [InlineData("role=cosponsor", new[] { 1, 2, 3, 4 })]
    [InlineData("role=cosponsor&limit=2", new[] { 1, 2 })]
    public async Task TheRoleFilterNarrowsTheSponsorshipsAndKeepsTheirOrder(string query, int[] expected)
    {
        await using var host = await StartWithBillsAsync(A);

        using var page = await GetAsync(host, $"/v1/bills/{A.RecordId}/sponsorships?{query}");

        Assert.Equal(expected, Positions(page));
    }

    [Fact]
    public async Task TheRoleFilterAndTheCursorWorkTogetherBecauseTheCursorMovesOnlyThroughTheFilteredRows()
    {
        await using var host = await StartWithBillsAsync(A);
        using var first = await GetAsync(host, $"/v1/bills/{A.RecordId}/sponsorships?role=cosponsor&limit=2");
        var cursor = first.RootElement.GetProperty("nextCursor").GetString();

        using var second = await GetAsync(host, $"/v1/bills/{A.RecordId}/sponsorships?role=cosponsor&limit=5&cursor={cursor}");

        Assert.Equal([3, 4], Positions(second));
    }

    [Fact]
    public async Task ASponsorshipRoleThatNoRowHoldsGivesAnEmptyPageBecauseTheBillStillExists()
    {
        var cosponsorOnly = new SeededBill('d', "2026-03-02") { Sponsorships = [new("p1", "Cosponsor", "2026-01-10")] };
        await using var host = await StartWithBillsAsync(cosponsorOnly);

        using var page = await GetAsync(host, $"/v1/bills/{cosponsorOnly.RecordId}/sponsorships?role=sponsor");

        Assert.Empty(page.RootElement.GetProperty("sponsorships").EnumerateArray());
        Assert.Equal(1, page.RootElement.GetProperty("release").GetInt32());
    }

    [Theory]
    [InlineData("role=chair")]
    [InlineData("role=Sponsor")]
    [InlineData("role=sponsor&role=cosponsor")]
    [InlineData("limit=0")]
    [InlineData("limit=51")]
    [InlineData("limit=x")]
    [InlineData("cursor=nonsense")]
    [InlineData("cursor=p.0.1")]
    [InlineData("cursor=p.1.x")]
    [InlineData("cursor=p.1")]
    [InlineData("cursor=b.1.20260302.aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public async Task BadSponsorshipParametersAndForgedCursorsAreRefusedWith400AsProblemJson(string query)
    {
        await using var host = await StartWithBillsAsync(A);

        await AssertBadRequestAsync(host, $"/v1/bills/{A.RecordId}/sponsorships?{query}");
    }

    [Fact]
    public async Task ASponsorshipCursorOfAnOlderReleaseAnswers409WithTheActiveReleaseBecauseAWalkMustNotMixTwoReleases()
    {
        await using var host = await StartWithBillsAsync(A);
        using var first = await GetAsync(host, $"/v1/bills/{A.RecordId}/sponsorships?limit=2");
        var cursor = first.RootElement.GetProperty("nextCursor").GetString();
        await ActivateReleaseTwoAsync(host);

        await AssertReleaseChangedAsync(host, $"/v1/bills/{A.RecordId}/sponsorships?limit=2&cursor={cursor}");
        using var restart = await GetAsync(host, $"/v1/bills/{A.RecordId}/sponsorships?limit=2");

        Assert.Equal(2, restart.RootElement.GetProperty("release").GetInt32());
        Assert.Equal([0, 1], Positions(restart));
    }

    [Fact]
    public async Task BillAnswersAllowAnyOriginSetNoCookieAndSetNoCacheHeaderBecauseTheOriginServesEveryRequest()
    {
        await using var host = await StartWithBillsAsync(A);

        foreach (var path in new[] { "/v1/bills", $"/v1/bills/{A.RecordId}", $"/v1/bills/{A.RecordId}/sponsorships" })
        {
            using var response = await host.Client.GetAsync(path);
            Assert.Equal("*", Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
            Assert.False(response.Headers.Contains("Set-Cookie"));
            Assert.False(response.Headers.Contains("Cache-Control"));
        }
    }
}
