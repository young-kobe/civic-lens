using System.Net;
using System.Text.Json;
using CivicLens.Tests.Infrastructure.Collection;

namespace CivicLens.Tests.PublicApi.Votes;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class VoteEndpointsTests(PostgresCollection postgres)
{
    // The order is date desc, chamber, session desc, roll call desc, record ID. G and H share every key but the record ID.
    private static readonly SeededVote E = new('e', "2026-03-03") { Issues = ["health"] };
    private static readonly SeededVote D = new('d', "2026-03-02") { Session = 2, Kind = "Rule", BillType = "HRES", BillNumber = 7 };
    private static readonly SeededVote B = new('b', "2026-03-02") { RollCall = 9, BillNumber = 2, Issues = ["health", "tax"], UntrackedIssues = ["zoning"] };
    private static readonly SeededVote A = new('a', "2026-03-02") { RollCall = 5 };
    private static readonly SeededVote C = new('c', "2026-03-02")
    {
        Chamber = "Senate",
        Kind = "Cloture",
        BillType = "S",
        BillNumber = 3,
        TextOrigin = "Person",
        Casts = ["Yea", "Smith"]
    };
    private static readonly SeededVote G = new('1', "2026-03-01") { RollCall = 3, BillType = null, BillNumber = null, Kind = "Other" };
    private static readonly SeededVote H = new('2', "2026-03-01") { RollCall = 3, BillType = null, BillNumber = null, Kind = "Other" };
    private static readonly SeededVote F = new('f', "2026-02-20") { Congress = 118, RollCall = 2, Kind = "Amendment" };
    private static readonly SeededVote[] All = [H, G, F, A, B, C, D, E];
    private static readonly char[] ExpectedOrder = ['e', 'd', 'b', 'a', 'c', '1', '2', 'f'];

    private async Task<ApiHost> StartWithVotesAsync(params SeededVote[] votes)
    {
        var host = await ApiHost.StartAsync(postgres, new FakeReleaseSite());
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 1, new DateTimeOffset(2026, 6, 2, 0, 0, 0, TimeSpan.Zero));
        await ViewsSeed.AddVotesAsync(host.OperationalDatabase, votes);
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
        [.. page.RootElement.GetProperty("votes").EnumerateArray().Select(vote => vote.GetProperty("recordId").GetString()![0])];

    [Fact]
    public async Task VotesComeNewestFirstThenByChamberSessionRollCallAndRecordIdWithTheReleaseOfTheSameQuery()
    {
        await using var host = await StartWithVotesAsync(All);

        using var page = await GetAsync(host, "/v1/votes");

        Assert.Equal(ExpectedOrder, Ids(page));
        Assert.Equal(1, page.RootElement.GetProperty("release").GetInt32());
        Assert.Equal(JsonValueKind.Null, page.RootElement.GetProperty("nextCursor").ValueKind);
    }

    [Fact]
    public async Task AVoteRowShowsTheViewFieldsInTheWordingOfTheReleaseFiles()
    {
        await using var host = await StartWithVotesAsync(E);

        using var page = await GetAsync(host, "/v1/votes");

        var vote = Assert.Single(page.RootElement.GetProperty("votes").EnumerateArray());
        Assert.Equal(new string('e', 64), vote.GetProperty("recordId").GetString());
        Assert.Equal(1, vote.GetProperty("revisionNumber").GetInt32());
        Assert.Equal("house", vote.GetProperty("chamber").GetString());
        Assert.Equal(119, vote.GetProperty("congress").GetInt32());
        Assert.Equal(1, vote.GetProperty("session").GetInt32());
        Assert.Equal(1, vote.GetProperty("rollCall").GetInt32());
        Assert.Equal("2026-03-03", vote.GetProperty("votedOn").GetString());
        Assert.Equal("passage", vote.GetProperty("kind").GetString());
        Assert.Equal("HR", vote.GetProperty("billType").GetString());
        Assert.Equal(1, vote.GetProperty("billNumber").GetInt32());
        Assert.Equal("Headline e", vote.GetProperty("headline").GetString());
        Assert.Equal("Summary e", vote.GetProperty("summary").GetString());
        Assert.Equal("model", vote.GetProperty("textOrigin").GetString());
        Assert.Equal("An AI summary of the official record", vote.GetProperty("label").GetString());
        Assert.Equal("On e", vote.GetProperty("question").GetString());
        Assert.Equal("Passed e", vote.GetProperty("result").GetString());
        Assert.Equal((2, 1, 0, 0), (vote.GetProperty("yea").GetInt32(), vote.GetProperty("nay").GetInt32(),
            vote.GetProperty("present").GetInt32(), vote.GetProperty("notVoting").GetInt32()));
    }

    [Fact]
    public async Task APersonTextHasNoLabelAndACandidateNameInTheCastsLeavesTheCountsNullBecauseTheViewGivesNoTotalsThen()
    {
        await using var host = await StartWithVotesAsync(C);

        using var page = await GetAsync(host, "/v1/votes");

        var vote = Assert.Single(page.RootElement.GetProperty("votes").EnumerateArray());
        Assert.Equal("person", vote.GetProperty("textOrigin").GetString());
        foreach (var name in new[] { "label", "yea", "nay", "present", "notVoting" })
            Assert.Equal(JsonValueKind.Null, vote.GetProperty(name).ValueKind);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(8)]
    [InlineData(50)]
    public async Task FollowingTheCursorsGivesEveryVoteOnceInTheSameOrderAtAnyPageSize(int limit)
    {
        await using var host = await StartWithVotesAsync(All);
        var seen = new List<char>();
        string? cursor = null;
        var pages = 0;

        do
        {
            using var page = await GetAsync(host, $"/v1/votes?limit={limit}" + (cursor is null ? "" : "&cursor=" + cursor));
            var ids = Ids(page);
            Assert.InRange(ids.Length, 1, limit);
            seen.AddRange(ids);
            cursor = page.RootElement.GetProperty("nextCursor").GetString();
            Assert.Equal(1, page.RootElement.GetProperty("release").GetInt32());
            pages++;
        }
        while (cursor is not null && pages < 20);

        Assert.Equal(ExpectedOrder, seen);
        Assert.Equal((8 + limit - 1) / limit, pages);
    }

    [Fact]
    public async Task TheDefaultPageHolds20VotesAndThePageSizeCapIs50()
    {
        var votes = Enumerable.Range(0, 55).Select(i => new SeededVote('a', "2026-01-01") { RecordId = i.ToString("x64"), RollCall = i + 1 }).ToArray();
        await using var host = await StartWithVotesAsync(votes);

        using var first = await GetAsync(host, "/v1/votes");
        using var capped = await GetAsync(host, "/v1/votes?limit=50");

        Assert.Equal(20, first.RootElement.GetProperty("votes").GetArrayLength());
        Assert.NotEqual(JsonValueKind.Null, first.RootElement.GetProperty("nextCursor").ValueKind);
        Assert.Equal(50, capped.RootElement.GetProperty("votes").GetArrayLength());
        Assert.NotEqual(JsonValueKind.Null, capped.RootElement.GetProperty("nextCursor").ValueKind);
    }

    [Theory]
    [InlineData("chamber=senate", "c")]
    [InlineData("chamber=house&congress=118", "f")]
    [InlineData("congress=119&chamber=senate", "c")]
    [InlineData("session=2", "d")]
    [InlineData("kind=cloture", "c")]
    [InlineData("kind=previousQuestion", "")]
    [InlineData("billType=HR&billNumber=1", "eaf")]
    [InlineData("billType=hr&billNumber=1", "eaf")]
    [InlineData("billNumber=3", "c")]
    [InlineData("issue=health", "eb")]
    [InlineData("issue=tax", "b")]
    [InlineData("issue=zoning", "")]
    [InlineData("issue=health&chamber=house&kind=passage", "eb")]
    [InlineData("from=2026-03-02&to=2026-03-02", "dbac")]
    [InlineData("from=2026-03-03", "e")]
    [InlineData("to=2026-02-28", "f")]
    [InlineData("from=2026-03-02&to=2026-03-01", "")]
    public async Task EveryFilterNarrowsTheListAndKeepsTheOrder(string query, string expected)
    {
        await using var host = await StartWithVotesAsync(All);

        using var page = await GetAsync(host, "/v1/votes?" + query);

        Assert.Equal(expected.ToCharArray(), Ids(page));
        Assert.Equal(1, page.RootElement.GetProperty("release").GetInt32());
    }

    [Fact]
    public async Task AFilterAndACursorWorkTogetherBecauseTheCursorMovesOnlyThroughTheFilteredRows()
    {
        await using var host = await StartWithVotesAsync(All);
        using var first = await GetAsync(host, "/v1/votes?billType=HR&billNumber=1&limit=1");
        var cursor = first.RootElement.GetProperty("nextCursor").GetString();

        using var second = await GetAsync(host, $"/v1/votes?billType=HR&billNumber=1&limit=5&cursor={cursor}");

        Assert.Equal(['e'], Ids(first));
        Assert.Equal(['a', 'f'], Ids(second));
    }

    [Theory]
    [InlineData("chamber=lords")]
    [InlineData("chamber=House")]
    [InlineData("congress=0")]
    [InlineData("congress=x")]
    [InlineData("session=3")]
    [InlineData("kind=nonsense")]
    [InlineData("billNumber=0")]
    [InlineData("from=2026-13-01")]
    [InlineData("to=yesterday")]
    [InlineData("limit=0")]
    [InlineData("limit=51")]
    [InlineData("limit=x")]
    [InlineData("congress=1&congress=2")]
    [InlineData("cursor=nonsense")]
    [InlineData("cursor=v.1.20260302.0.5.5.abc")]
    [InlineData("cursor=v.0.20260302.0.1.5.aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("cursor=v.1.20261302.0.1.5.aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("cursor=v.1.20260302.7.1.5.aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("cursor=v.1.20260302.0.1.5.not-an-id")]
    [InlineData("cursor=s.1.aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public async Task BadParametersAndForgedCursorsAreRefusedWith400AsProblemJson(string query)
    {
        await using var host = await StartWithVotesAsync(All);

        using var response = await host.Client.GetAsync("/v1/votes?" + query);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("invalid request", problem.RootElement.GetProperty("title").GetString());
    }

    [Fact]
    public async Task ACursorWithAWellFormedKeyOnlyMovesTheWindowBecauseEveryRowIsPublicAnyway()
    {
        await using var host = await StartWithVotesAsync(All);
        var forged = $"v.1.20260302.0.1.5.{new string('0', 64)}";

        using var page = await GetAsync(host, "/v1/votes?cursor=" + forged);

        Assert.Equal(['a', 'c', '1', '2', 'f'], Ids(page));
    }

    [Fact]
    public async Task ACursorOfAnOlderReleaseAnswers409WithTheActiveReleaseAndANewWalkReadsTheNewRelease()
    {
        await using var host = await StartWithVotesAsync(All);
        using var first = await GetAsync(host, "/v1/votes?limit=2");
        var cursor = first.RootElement.GetProperty("nextCursor").GetString();
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 2, new DateTimeOffset(2026, 6, 9, 0, 0, 0, TimeSpan.Zero));

        using var response = await host.Client.GetAsync("/v1/votes?limit=2&cursor=" + cursor);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        using var restart = await GetAsync(host, "/v1/votes?limit=2");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("the release changed", problem.RootElement.GetProperty("title").GetString());
        Assert.Equal(2, problem.RootElement.GetProperty("release").GetInt32());
        Assert.Contains("active release is 2", problem.RootElement.GetProperty("detail").GetString(), StringComparison.Ordinal);
        Assert.Equal(2, restart.RootElement.GetProperty("release").GetInt32());
        Assert.Equal(['e', 'd'], Ids(restart));
    }

    [Fact]
    public async Task ARecordThatLeftTheActiveReleaseIsNeitherListedNorFound()
    {
        var retired = new SeededVote('9', "2026-03-05") { LastRelease = 1 };
        await using var host = await StartWithVotesAsync(E, retired);
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 2, new DateTimeOffset(2026, 6, 9, 0, 0, 0, TimeSpan.Zero));

        using var page = await GetAsync(host, "/v1/votes");
        using var missing = await host.Client.GetAsync($"/v1/votes/{retired.RecordId}");

        Assert.Equal(['e'], Ids(page));
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task WithNoActiveReleaseTheListAnswers503AndALookupAnswers404()
    {
        await using var host = await ApiHost.StartAsync(postgres, new FakeReleaseSite());

        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await host.Client.GetAsync("/v1/votes")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await host.Client.GetAsync($"/v1/votes/{new string('e', 64)}")).StatusCode);
    }

    [Fact]
    public async Task AFilterThatMatchesNoVoteGivesAnEmptyListThatNamesTheReleaseBecauseTheEmptyPageIsStillAnAnswer()
    {
        await using var host = await StartWithVotesAsync(E);

        using var page = await GetAsync(host, "/v1/votes?kind=cloture");

        Assert.Empty(page.RootElement.GetProperty("votes").EnumerateArray());
        Assert.Equal(1, page.RootElement.GetProperty("release").GetInt32());
        Assert.Equal(JsonValueKind.Null, page.RootElement.GetProperty("nextCursor").ValueKind);
    }

    [Fact]
    public async Task OneVoteShowsItsRowAndItsTrackedIssuesInOrderAndNeverAnUntrackedOne()
    {
        await using var host = await StartWithVotesAsync(All);

        using var withIssues = await GetAsync(host, $"/v1/votes/{B.RecordId}");
        using var withoutIssues = await GetAsync(host, $"/v1/votes/{A.RecordId}");

        Assert.Equal(1, withIssues.RootElement.GetProperty("release").GetInt32());
        Assert.Equal(B.RecordId, withIssues.RootElement.GetProperty("vote").GetProperty("recordId").GetString());
        Assert.Equal(9, withIssues.RootElement.GetProperty("vote").GetProperty("rollCall").GetInt32());
        Assert.Equal(["health", "tax"], withIssues.RootElement.GetProperty("issueIds").EnumerateArray().Select(issue => issue.GetString()));
        Assert.Empty(withoutIssues.RootElement.GetProperty("issueIds").EnumerateArray());
    }

    [Theory]
    [InlineData("0000000000000000000000000000000000000000000000000000000000000000")]
    [InlineData("nonsense")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public async Task AnUnknownOrMalformedRecordAnswers404AsProblemJson(string recordId)
    {
        await using var host = await StartWithVotesAsync(All);

        using var response = await host.Client.GetAsync("/v1/votes/" + recordId);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("record not found", problem.RootElement.GetProperty("title").GetString());
    }

    [Fact]
    public async Task VoteAnswersAllowAnyOriginSetNoCookieAndSetNoCacheHeaderBecauseTheOriginServesEveryRequest()
    {
        await using var host = await StartWithVotesAsync(E);

        foreach (var path in new[] { "/v1/votes", $"/v1/votes/{E.RecordId}" })
        {
            using var response = await host.Client.GetAsync(path);
            Assert.Equal("*", Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
            Assert.False(response.Headers.Contains("Set-Cookie"));
            Assert.False(response.Headers.Contains("Cache-Control"));
        }
    }
}
