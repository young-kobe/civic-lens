using System.Net;
using System.Text.Json;
using CivicLens.Tests.Infrastructure.Collection;
using CivicLens.Tests.PublicApi.Votes;

namespace CivicLens.Tests.PublicApi.ZipDistricts;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class ZipDistrictEndpointsTests(PostgresCollection postgres)
{
    private const string Representative = "Representative";
    private const string Senator = "Senator";
    private const string Vacant = "vacant";

    private static readonly SeededSeat[] Seats =
    [
        new("al2", "Alice Two", Representative, "AL", 2),
        new("al1-old", "Olive Old", Representative, "AL", 1, "2023-01-03", "2024-01-02"),
        new("al-sen", "Sam Senator", Senator, "AL", null, Party: "Republican"),
        new("al-sen-old", "Sara Former", Senator, "AL", null, "2019-01-03", "2020-01-02"),
        new("ak0", "Ann Large", Representative, "AK", 0),
        new("ak-sen-b", "Bea Second", Senator, "AK", null, "2999-01-03", Party: null),
        new("ak-sen-a", "Al First", Senator, "AK", null, "2021-01-03"),
        new("ak-sen-c", "Cy Third", Senator, "AK", null, "2025-01-03"),
        new("dc0", "Dee Delegate", Representative, "DC", 0)
    ];

    private async Task<ApiHost> StartAsync(bool withNames = true)
    {
        var host = await ApiHost.StartAsync(postgres, new FakeReleaseSite());
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 1, new DateTimeOffset(2026, 6, 2, 0, 0, 0, TimeSpan.Zero));
        await ZipSeed.AddRecordAsync(host.OperationalDatabase, 1);
        await ZipSeed.AdmitDistrictsAsync(host.OperationalDatabase);
        if (withNames) await ZipSeed.AddNamesAsync(host.OperationalDatabase, 1, Seats);
        return host;
    }

    private static async Task<JsonElement> GetAsync(ApiHost host, string path, HttpStatusCode expected = HttpStatusCode.OK)
    {
        using var response = await host.Client.GetAsync(path);
        Assert.Equal(expected, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    private static string[] Names(JsonElement array) =>
        [.. array.EnumerateArray().Select(holder => holder.ValueKind == JsonValueKind.Null ? Vacant : holder.GetProperty("name").GetString()!)];

    [Fact]
    public async Task AZctaInOneDistrictAnswersItsRepresentativeAndTheTwoSenatorsOfItsStateInTermOrderAndSkipsATermThatHasNotBegun()
    {
        await using var host = await StartAsync();

        var answer = await GetAsync(host, "/v1/zip-districts/99501");

        Assert.Equal(1, answer.GetProperty("release").GetInt32());
        Assert.Equal(119, answer.GetProperty("congress").GetInt32());
        Assert.Equal("99501", answer.GetProperty("zcta").GetString());
        var district = Assert.Single(answer.GetProperty("districts").EnumerateArray());
        Assert.Equal(("AK", 0, "Ann Large"), (district.GetProperty("state").GetString(), district.GetProperty("district").GetInt32(), district.GetProperty("representative").GetProperty("name").GetString()));
        var state = Assert.Single(answer.GetProperty("states").EnumerateArray());
        Assert.Equal("AK", state.GetProperty("state").GetString());
        Assert.Equal(["Al First", "Cy Third"], Names(state.GetProperty("senators")));
    }

    [Fact]
    public async Task ASplitZctaListsEveryDistrictLargestLandFirstWithTheSenatorsOfTheStateOnce()
    {
        await using var host = await StartAsync();

        var answer = await GetAsync(host, "/v1/zip-districts/36009");

        Assert.Equal([2, 1], answer.GetProperty("districts").EnumerateArray().Select(district => district.GetProperty("district").GetInt32()));
        Assert.Equal("Alice Two", answer.GetProperty("districts")[0].GetProperty("representative").GetProperty("name").GetString());
        Assert.Equal("Sam Senator", answer.GetProperty("states")[0].GetProperty("senators")[0].GetProperty("name").GetString());
        Assert.Equal("Republican", answer.GetProperty("states")[0].GetProperty("senators")[0].GetProperty("party").GetString());
        Assert.Single(answer.GetProperty("states").EnumerateArray());
    }

    [Fact]
    public async Task DistrictsWithTheSameLandKeepTheOrderOfStateAndDistrict()
    {
        await using var host = await StartAsync();

        var answer = await GetAsync(host, "/v1/zip-districts/12345");

        Assert.Equal([1, 2], answer.GetProperty("districts").EnumerateArray().Select(district => district.GetProperty("district").GetInt32()));
    }

    [Fact]
    public async Task ASeatWithNoCurrentHolderIsNullBecauseAnEndedTermNeverFillsASeat()
    {
        await using var host = await StartAsync();

        var split = await GetAsync(host, "/v1/zip-districts/36009");
        var noRepresentative = await GetAsync(host, "/v1/zip-districts/94505");

        Assert.Equal(JsonValueKind.Null, split.GetProperty("districts")[1].GetProperty("representative").ValueKind);
        Assert.Equal(JsonValueKind.Null, noRepresentative.GetProperty("districts")[0].GetProperty("representative").ValueKind);
        Assert.Equal(["Sam Senator", Vacant], Names(split.GetProperty("states")[0].GetProperty("senators")));
        Assert.Equal([Vacant, Vacant], Names(noRepresentative.GetProperty("states")[0].GetProperty("senators")));
    }

    [Fact]
    public async Task OnTheDayATermEndsTheNextTermHasStartedSoTheNewSenatorTakesTheSeatAndAReelectedSenatorHoldsOneSeat()
    {
        await using var host = await StartAsync();
        var today = await ZipSeed.TodayAsync(host.OperationalDatabase);
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 2, new DateTimeOffset(2026, 6, 9, 0, 0, 0, TimeSpan.Zero));
        await ZipSeed.AddRecordAsync(host.OperationalDatabase, 2);
        await ZipSeed.AddNamesAsync(host.OperationalDatabase, 2,
            new SeededSeat("a-departing", "Dana Departing", Senator, "AL", null, "2019-01-03", today),
            new SeededSeat("b-new", "Nia New", Senator, "AL", null, today),
            new SeededSeat("c-reelected", "Rae Reelected", Senator, "AL", null, "2019-01-03", today));
        await ZipSeed.AddTermAsync(host.OperationalDatabase, 2, new SeededSeat("c-reelected", "Rae Reelected", Senator, "AL", null, today));

        var answer = await GetAsync(host, "/v1/zip-districts/36009");

        Assert.Equal(["Nia New", "Rae Reelected"], Names(answer.GetProperty("states")[0].GetProperty("senators")));
    }

    [Fact]
    public async Task AnAppointeeWhoStartedLaterButEndsTodayDoesNotTakeTheSeatOfTheSittingSenatorFromTheWinnerOfTheSpecialElection()
    {
        await using var host = await StartAsync();
        var today = await ZipSeed.TodayAsync(host.OperationalDatabase);
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 2, new DateTimeOffset(2026, 6, 9, 0, 0, 0, TimeSpan.Zero));
        await ZipSeed.AddRecordAsync(host.OperationalDatabase, 2);
        await ZipSeed.AddNamesAsync(host.OperationalDatabase, 2,
            new SeededSeat("a-sitting", "Sid Sitting", Senator, "AL", null, "2025-01-03"),
            new SeededSeat("b-appointee", "Ari Appointee", Senator, "AL", null, "2025-01-21", today),
            new SeededSeat("c-winner", "Wes Winner", Senator, "AL", null, today));

        var answer = await GetAsync(host, "/v1/zip-districts/36009");

        Assert.Equal(["Sid Sitting", "Wes Winner"], Names(answer.GetProperty("states")[0].GetProperty("senators")));
    }

    [Fact]
    public async Task ATerritoryDelegateIsTheDistrictZeroSeatAndTheTerritoryHasNoSenateSeats()
    {
        await using var host = await StartAsync();

        var answer = await GetAsync(host, "/v1/zip-districts/20001");

        Assert.Equal("Dee Delegate", answer.GetProperty("districts")[0].GetProperty("representative").GetProperty("name").GetString());
        Assert.Equal("DC", answer.GetProperty("states")[0].GetProperty("state").GetString());
        Assert.Empty(answer.GetProperty("states")[0].GetProperty("senators").EnumerateArray());
    }

    [Fact]
    public async Task TheNamesFollowTheActiveReleaseSoANewReleaseShowsItsOwnSeatHolders()
    {
        await using var host = await StartAsync();
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 2, new DateTimeOffset(2026, 6, 9, 0, 0, 0, TimeSpan.Zero));
        await ZipSeed.AddRecordAsync(host.OperationalDatabase, 2);
        await ZipSeed.AddNamesAsync(host.OperationalDatabase, 2, new SeededSeat("al2-new", "Alma Newer", Representative, "AL", 2));

        var answer = await GetAsync(host, "/v1/zip-districts/36009");

        Assert.Equal(2, answer.GetProperty("release").GetInt32());
        Assert.Equal("Alma Newer", answer.GetProperty("districts")[0].GetProperty("representative").GetProperty("name").GetString());
    }

    [Fact]
    public async Task TheCongressFilterSelectsTheRowsOfThatCongressAndAnUnknownCongressIsNotFound()
    {
        await using var host = await StartAsync();

        var named = await GetAsync(host, "/v1/zip-districts/99501?congress=119");
        var other = await GetAsync(host, "/v1/zip-districts/99501?congress=118", HttpStatusCode.NotFound);

        Assert.Equal(119, named.GetProperty("congress").GetInt32());
        Assert.Equal("zip area not found", other.GetProperty("title").GetString());
    }

    [Theory]
    [InlineData("/v1/zip-districts/1234")]
    [InlineData("/v1/zip-districts/123456")]
    [InlineData("/v1/zip-districts/abcde")]
    [InlineData("/v1/zip-districts/%EF%BC%91%EF%BC%92%EF%BC%93%EF%BC%94%EF%BC%95")]
    [InlineData("/v1/zip-districts/99501?congress=1&congress=2")]
    [InlineData("/v1/zip-districts/99501?congress=0")]
    [InlineData("/v1/zip-districts/99501?congress=x")]
    public async Task ABadZctaOrCongressIsRefusedWith400AsProblemJson(string path)
    {
        await using var host = await StartAsync();

        using var response = await host.Client.GetAsync(path);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("invalid request", problem.RootElement.GetProperty("title").GetString());
    }

    [Fact]
    public async Task AZctaWithNoRowAnswers404AsProblemJson()
    {
        await using var host = await StartAsync();

        using var response = await host.Client.GetAsync("/v1/zip-districts/00000");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("zip area not found", problem.RootElement.GetProperty("title").GetString());
    }

    [Fact]
    public async Task WithNoActiveReleaseTheRouteAnswers503()
    {
        await using var host = await ApiHost.StartAsync(postgres, new FakeReleaseSite());
        await ZipSeed.AdmitDistrictsAsync(host.OperationalDatabase);

        var problem = await GetAsync(host, "/v1/zip-districts/99501", HttpStatusCode.ServiceUnavailable);

        Assert.Equal("no release is active yet", problem.GetProperty("title").GetString());
    }

    [Fact]
    public async Task AnActiveReleaseWithNoNameRowsAnswers503SoNoSeatReadsAsVacantByMistake()
    {
        await using var host = await StartAsync(withNames: false);

        var problem = await GetAsync(host, "/v1/zip-districts/99501", HttpStatusCode.ServiceUnavailable);

        Assert.Equal("the active release has no official names yet", problem.GetProperty("title").GetString());
    }

    [Fact]
    public async Task AnAnswerCanBeCachedForOneHourAndAnErrorCarriesNoCacheHeader()
    {
        await using var host = await StartAsync();

        using var found = await host.Client.GetAsync("/v1/zip-districts/99501");
        using var missing = await host.Client.GetAsync("/v1/zip-districts/00000");
        using var invalid = await host.Client.GetAsync("/v1/zip-districts/1");

        Assert.Equal("public, max-age=3600", Assert.Single(found.Headers.GetValues("Cache-Control")));
        Assert.Equal("*", Assert.Single(found.Headers.GetValues("Access-Control-Allow-Origin")));
        Assert.False(missing.Headers.Contains("Cache-Control"));
        Assert.False(invalid.Headers.Contains("Cache-Control"));
    }
}
