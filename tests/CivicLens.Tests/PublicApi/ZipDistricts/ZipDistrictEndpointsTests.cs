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
    public async Task SeatHoldersCarryStoredPhotosAndLatestAttendanceInOneAnswerAcrossMoreThanThreeDistinctHolders()
    {
        await using var host = await StartAsync(withNames: false);
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 2, new DateTimeOffset(2026, 6, 9, 0, 0, 0, TimeSpan.Zero));
        await ZipSeed.AddRecordAsync(host.OperationalDatabase, 2);
        await ZipSeed.AddNamesAsync(host.OperationalDatabase, 2,
            new SeededSeat("p0", "Pat House One", Representative, "AL", 2),
            new SeededSeat("p1", "Pat House Two", Representative, "AL", 1),
            new SeededSeat("p2", "Sam Senate One", Senator, "AL", null),
            new SeededSeat("p3", "Sam Senate Two", Senator, "AL", null));
        var photo = "photos/" + new string('b', 64) + ".jpg";
        await ViewsSeed.ExecuteAsync(host.OperationalDatabase, $"UPDATE release_officials SET photo_path = '{photo}' WHERE release_number = 2 AND official_id = 'p0';");
        await ViewsSeed.AddVotesAsync(host.OperationalDatabase,
            new SeededVote('b', "2026-01-01") { Congress = 119, Casts = ["Not Voting"] },
            new SeededVote('c', "2026-02-01") { Congress = 120, Casts = ["Not Voting", "Yea"] },
            new SeededVote('d', "2026-02-02") { Congress = 120, Chamber = "Senate", Casts = ["Yea", "Not Voting", "Nay"] },
            new SeededVote('e', "2026-03-01") { Congress = 121, FirstRelease = 3 },
            new SeededVote('f', "2026-04-01") { Congress = 122, LastRelease = 1 });
        await ViewsSeed.ExecuteAsync(host.OperationalDatabase, "UPDATE vote_member_list_entries SET person_id = 'other' WHERE person_id = 'p1';");

        var answer = await GetAsync(host, "/v1/zip-districts/36009");

        var districts = answer.GetProperty("districts");
        Assert.Equal("p0", districts[0].GetProperty("representative").GetProperty("officialId").GetString());
        Assert.Equal(photo, districts[0].GetProperty("representative").GetProperty("photoPath").GetString());
        Assert.Equal(2, answer.GetProperty("release").GetInt32());
        Assert.Equal(119, answer.GetProperty("congress").GetInt32());
        var representativeFigures = districts[0].GetProperty("representative").GetProperty("missedVotes");
        Assert.Equal(["house", "senate"], representativeFigures.EnumerateArray().Select(figure => figure.GetProperty("chamber").GetString()));
        var houseFigure = representativeFigures[0];
        Assert.Equal(("house", 120, 1, 1), (houseFigure.GetProperty("chamber").GetString(), houseFigure.GetProperty("congress").GetInt32(), houseFigure.GetProperty("missed").GetInt32(), houseFigure.GetProperty("held").GetInt32()));
        Assert.Contains("Not Voting", houseFigure.GetProperty("scope").GetString());
        Assert.Equal("2026-02-01", houseFigure.GetProperty("firstVotedOn").GetString());
        Assert.Equal("2026-02-01", houseFigure.GetProperty("lastVotedOn").GetString());
        var senators = answer.GetProperty("states")[0].GetProperty("senators");
        Assert.Equal("p2", senators[0].GetProperty("officialId").GetString());
        var senateFigure = Assert.Single(senators[0].GetProperty("missedVotes").EnumerateArray());
        Assert.Equal(("senate", 120, 0, 1), (senateFigure.GetProperty("chamber").GetString(), senateFigure.GetProperty("congress").GetInt32(), senateFigure.GetProperty("missed").GetInt32(), senateFigure.GetProperty("held").GetInt32()));
        Assert.Equal(JsonValueKind.Null, districts[1].GetProperty("representative").GetProperty("photoPath").ValueKind);
        Assert.Empty(districts[1].GetProperty("representative").GetProperty("missedVotes").EnumerateArray());
        Assert.Empty(senators[1].GetProperty("missedVotes").EnumerateArray());
    }

    [Fact]
    public async Task SeatHoldersCarryPartyLinesFromEachHoldersLatestAttendanceCongress()
    {
        await using var host = await StartAsync(withNames: false);
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 2, new DateTimeOffset(2026, 6, 9, 0, 0, 0, TimeSpan.Zero));
        await ZipSeed.AddRecordAsync(host.OperationalDatabase, 2);
        await ZipSeed.AddNamesAsync(host.OperationalDatabase, 2,
            new SeededSeat("p0", "Pat House One", Representative, "AL", 2, Party: "Democratic"),
            new SeededSeat("p1", "Pat House Two", Representative, "AL", 1),
            new SeededSeat("p2", "Sam Senate One", Senator, "AL", null),
            new SeededSeat("p3", "Sam Senate Two", Senator, "AL", null));
        await ViewsSeed.AddVotesAsync(host.OperationalDatabase,
            new SeededVote('1', "2024-01-01") { Congress = 118, Casts = ["Yea", "Yea", "Nay", "Nay"] },
            new SeededVote('2', "2024-01-02") { Congress = 118, Casts = ["Yea", "Yea", "Nay", "Nay"] },
            new SeededVote('3', "2024-01-03") { Congress = 118, Casts = ["Yea", "Yea", "Nay", "Nay"] },
            new SeededVote('4', "2026-02-01") { Congress = 120, Casts = ["Yea", "Present", "Nay", "Yea"] },
            new SeededVote('6', "2026-02-02") { Congress = 120, Casts = ["Yea", "Present", "Nay", "Yea"] },
            new SeededVote('5', "2026-02-02") { Congress = 120, Chamber = "Senate", Casts = ["Nay", "Present", "Nay", "Yea"] },
            new SeededVote('8', "2026-03-01") { Congress = 121, FirstRelease = 3, Casts = ["Yea"] },
            new SeededVote('9', "2026-04-01") { Congress = 122, LastRelease = 1, Casts = ["Yea"] });
        await ViewsSeed.ExecuteAsync(host.OperationalDatabase, $"""
            SET session_replication_role = replica;
            UPDATE vote_member_list_entries SET party = CASE person_id
                WHEN 'p0' THEN 'Democrat' WHEN 'p1' THEN 'Republican' WHEN 'p2' THEN 'Democrat' ELSE 'Republican' END;
            UPDATE vote_member_list_entries SET party = 'Republican' WHERE list_id = '{new string('4', 64)}' AND person_id = 'p0';
            UPDATE vote_member_list_entries SET person_id = 'untracked-' || person_id
             WHERE person_id = 'p3' OR (person_id = 'p2' AND list_id <> '{new string('5', 64)}');
            UPDATE vote_revisions SET columns_written = true, alignment = 'Split', democrat_majority = 'Yea', republican_majority = 'Nay'
             WHERE record_id IN ('{new string('1', 64)}', '{new string('2', 64)}', '{new string('3', 64)}', '{new string('4', 64)}', '{new string('5', 64)}', '{new string('6', 64)}');
            """);

        var answer = await GetAsync(host, "/v1/zip-districts/36009");
        var house = answer.GetProperty("districts");
        var first = house[0].GetProperty("representative");
        var firstLines = first.GetProperty("partyLines").EnumerateArray().ToArray();
        Assert.Equal(["house Democrat", "house Republican", "senate Democrat"], firstLines.Select(line => $"{line.GetProperty("chamber").GetString()} {line.GetProperty("party").GetString()}"));
        Assert.All(firstLines, line => Assert.Equal(120, line.GetProperty("congress").GetInt32()));
        Assert.Equal((1, 0, 1), (firstLines[0].GetProperty("withParty").GetInt32(), firstLines[0].GetProperty("againstParty").GetInt32(), firstLines[0].GetProperty("cast").GetInt32()));
        Assert.Equal((0, 1, 1), (firstLines[1].GetProperty("withParty").GetInt32(), firstLines[1].GetProperty("againstParty").GetInt32(), firstLines[1].GetProperty("cast").GetInt32()));
        Assert.Equal((0, 1, 1), (firstLines[2].GetProperty("withParty").GetInt32(), firstLines[2].GetProperty("againstParty").GetInt32(), firstLines[2].GetProperty("cast").GetInt32()));
        Assert.Contains("Congress 120", firstLines[0].GetProperty("scope").GetString(), StringComparison.Ordinal);
        Assert.NotEmpty(house[1].GetProperty("representative").GetProperty("missedVotes").EnumerateArray());
        Assert.Empty(house[1].GetProperty("representative").GetProperty("partyLines").EnumerateArray());
        var senators = answer.GetProperty("states")[0].GetProperty("senators");
        var senateLine = Assert.Single(senators[0].GetProperty("partyLines").EnumerateArray());
        Assert.Equal(("senate", "Democrat", 120), (senateLine.GetProperty("chamber").GetString(), senateLine.GetProperty("party").GetString(), senateLine.GetProperty("congress").GetInt32()));
        Assert.Empty(senators[1].GetProperty("partyLines").EnumerateArray());
        Assert.Empty(senators[1].GetProperty("missedVotes").EnumerateArray());

        using var fragment = await host.Client.GetAsync("/v1/zip-districts/36009/members");
        Assert.Equal(HttpStatusCode.OK, fragment.StatusCode);
        Assert.True(fragment.Headers.CacheControl is { Private: true, NoStore: true, Public: false });
    }

    [Fact]
    public async Task PhotosFollowTheActiveReleaseAndMissingPhotosStayNull()
    {
        await using var host = await StartAsync();
        var photo = "photos/" + new string('b', 64) + ".jpg";
        await ViewsSeed.ExecuteAsync(host.OperationalDatabase, $"UPDATE release_officials SET photo_path = '{photo}' WHERE release_number = 1 AND official_id = 'al2';");
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 2, new DateTimeOffset(2026, 6, 9, 0, 0, 0, TimeSpan.Zero));
        await ZipSeed.AddRecordAsync(host.OperationalDatabase, 2);
        await ZipSeed.AddNamesAsync(host.OperationalDatabase, 2, new SeededSeat("al2", "Alice Two", Representative, "AL", 2));
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 1, new DateTimeOffset(2026, 6, 2, 0, 0, 0, TimeSpan.Zero));

        var first = await GetAsync(host, "/v1/zip-districts/36009");
        Assert.Equal(1, first.GetProperty("release").GetInt32());
        Assert.Equal(photo, first.GetProperty("districts")[0].GetProperty("representative").GetProperty("photoPath").GetString());

        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 2, new DateTimeOffset(2026, 6, 9, 0, 0, 0, TimeSpan.Zero));
        var second = await GetAsync(host, "/v1/zip-districts/36009");
        Assert.Equal(2, second.GetProperty("release").GetInt32());
        Assert.Equal(JsonValueKind.Null, second.GetProperty("districts")[0].GetProperty("representative").GetProperty("photoPath").ValueKind);
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
    public async Task EachSeatHolderCarriesTheEndDateOfTheCurrentTermOrNullWhenTheReleaseHasNoEndDate()
    {
        await using var host = await StartAsync();
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 2, new DateTimeOffset(2026, 6, 9, 0, 0, 0, TimeSpan.Zero));
        await ZipSeed.AddRecordAsync(host.OperationalDatabase, 2);
        await ZipSeed.AddNamesAsync(host.OperationalDatabase, 2,
            new SeededSeat("al2", "Alice Two", Representative, "AL", 2, End: "2999-01-03"),
            new SeededSeat("al1", "Al One", Representative, "AL", 1),
            new SeededSeat("sen-a", "Ann Ending", Senator, "AL", null, "2021-01-03", "2999-01-03"),
            new SeededSeat("sen-b", "Bo Open", Senator, "AL", null, "2025-01-03"));

        var answer = await GetAsync(host, "/v1/zip-districts/36009");

        var districts = answer.GetProperty("districts");
        Assert.Equal("2999-01-03", districts[0].GetProperty("representative").GetProperty("termEndsOn").GetString());
        Assert.Equal(JsonValueKind.Null, districts[1].GetProperty("representative").GetProperty("termEndsOn").ValueKind);
        var senators = answer.GetProperty("states")[0].GetProperty("senators");
        Assert.Equal(["2999-01-03", null], senators.EnumerateArray().Select(senator => senator.GetProperty("termEndsOn").GetString()));
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
            new SeededSeat("b-new", "Nia New", Senator, "AL", null, today, "2998-01-03"),
            new SeededSeat("c-reelected", "Rae Reelected", Senator, "AL", null, "2019-01-03", today));
        await ZipSeed.AddTermAsync(host.OperationalDatabase, 2, new SeededSeat("c-reelected", "Rae Reelected", Senator, "AL", null, today, "2999-01-03"));

        var answer = await GetAsync(host, "/v1/zip-districts/36009");

        var senators = answer.GetProperty("states")[0].GetProperty("senators");
        Assert.Equal(["Nia New", "Rae Reelected"], Names(senators));
        Assert.Equal(["2998-01-03", "2999-01-03"], senators.EnumerateArray().Select(senator => senator.GetProperty("termEndsOn").GetString()));
    }

    [Fact]
    public async Task ARepresentativeWhoWasReelectedShowsTheEndDateOfTheNewTermAndNeverReadsATermThatHasNotBegun()
    {
        await using var host = await StartAsync();
        var today = await ZipSeed.TodayAsync(host.OperationalDatabase);
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 2, new DateTimeOffset(2026, 6, 9, 0, 0, 0, TimeSpan.Zero));
        await ZipSeed.AddRecordAsync(host.OperationalDatabase, 2);
        await ZipSeed.AddNamesAsync(host.OperationalDatabase, 2, new SeededSeat("al2", "Alice Two", Representative, "AL", 2, "2019-01-03", today));
        await ZipSeed.AddTermAsync(host.OperationalDatabase, 2, new SeededSeat("al2", "Alice Two", Representative, "AL", 2, today, "2999-01-03"));
        await ZipSeed.AddTermAsync(host.OperationalDatabase, 2, new SeededSeat("al2", "Alice Two", Representative, "AL", 2, "2998-01-03", "2999-12-31"));

        var answer = await GetAsync(host, "/v1/zip-districts/36009");

        Assert.Equal("2999-01-03", answer.GetProperty("districts")[0].GetProperty("representative").GetProperty("termEndsOn").GetString());
    }

    [Fact]
    public async Task ASenatorWhoHasATermThatHasNotBegunShowsTheEndDateOfTheCurrentTerm()
    {
        await using var host = await StartAsync();
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 2, new DateTimeOffset(2026, 6, 9, 0, 0, 0, TimeSpan.Zero));
        await ZipSeed.AddRecordAsync(host.OperationalDatabase, 2);
        await ZipSeed.AddNamesAsync(host.OperationalDatabase, 2, new SeededSeat("sen", "Sue Senator", Senator, "AL", null, "2021-01-03", "2999-01-03"));
        await ZipSeed.AddTermAsync(host.OperationalDatabase, 2, new SeededSeat("sen", "Sue Senator", Senator, "AL", null, "2998-01-03", "2999-12-31"));

        var answer = await GetAsync(host, "/v1/zip-districts/36009");

        Assert.Equal("2999-01-03", answer.GetProperty("states")[0].GetProperty("senators")[0].GetProperty("termEndsOn").GetString());
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
