using System.Net;
using System.Text.Json;
using CivicLens.Tests.Infrastructure.Collection;
using CivicLens.Tests.PublicApi.Officials;

namespace CivicLens.Tests.PublicApi.Votes;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class VoteMembersEndpointsTests(PostgresCollection postgres)
{
    private const int FullHouse = 120;
    private static readonly string[] HouseCasts = [.. Enumerable.Range(0, FullHouse).Select(index => index % 3 == 0 ? "Nay" : index % 3 == 1 ? "Yea" : "Not Voting")];
    private static readonly SeededVote House = new('a', "2026-03-02") { Casts = HouseCasts };
    private static readonly SeededVote Senate = new('c', "2026-03-02") { Chamber = "Senate", Casts = ["Yea", "Smith", "Nay"] };

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

    private static int[] Positions(JsonDocument page) =>
        [.. page.RootElement.GetProperty("members").EnumerateArray().Select(member => member.GetProperty("position").GetInt32())];

    private static string? NextCursor(JsonDocument page) =>
        page.RootElement.GetProperty("nextCursor") is { ValueKind: JsonValueKind.String } cursor ? cursor.GetString() : null;

    [Fact]
    public async Task AFullHouseListPagesInPositionOrderWithNoGapAndNoRepeatBecauseAReaderMustSeeEveryMemberOnce()
    {
        await using var host = await StartWithVotesAsync(House);
        var seen = new List<int>();
        var pages = 0;
        string? cursor = null;

        do
        {
            using var page = await GetAsync(host, $"/v1/votes/{House.RecordId}/members?limit=50" + (cursor is null ? "" : "&cursor=" + cursor));
            seen.AddRange(Positions(page));
            cursor = NextCursor(page);
            pages++;
        }
        while (cursor is not null);

        Assert.Equal(Enumerable.Range(0, FullHouse), seen);
        Assert.Equal(3, pages);
    }

    [Fact]
    public async Task AMemberRowCarriesThePositionTheOfficialIdAndTheCastAsStoredWithTheRelease()
    {
        await using var host = await StartWithVotesAsync(Senate);

        using var page = await GetAsync(host, $"/v1/votes/{Senate.RecordId}/members");

        var member = page.RootElement.GetProperty("members")[1];
        Assert.Equal(1, page.RootElement.GetProperty("release").GetInt32());
        Assert.Equal(1, member.GetProperty("position").GetInt32());
        Assert.Equal("p1", member.GetProperty("officialId").GetString());
        Assert.Equal("Smith", member.GetProperty("cast").GetString());
        Assert.False(member.TryGetProperty("bioguideId", out _));
    }

    [Fact]
    public async Task MemberRowsExposePinnedPartyAndReleaseTermCaucusAndLeaveMissingValuesNull()
    {
        await using var host = await StartWithVotesAsync(Senate);
        await OfficialsSeed.AddOfficialsAsync(host.OperationalDatabase,
            new SeededOfficial("p0", "Ann Able",
                new SeededTerm("Senator", "Senator", "2025-01-03") { Party = "Independent", Caucus = "Democrat", EndsOn = "2026-03-02" },
                new SeededTerm("Representative", "Representative", "2026-02-01") { Caucus = "Republican" }),
            new SeededOfficial("p1", "Ben Baker", new SeededTerm("Senator", "Senator", "2025-01-03") { Party = "Republican" }),
            new SeededOfficial("p2", "Cy Cole",
                new SeededTerm("Senator", "Senator", "2025-01-03") { Caucus = "Republican", EndsOn = "2026-03-01" },
                new SeededTerm("Senator", "Senator", "2026-03-03") { Caucus = "Republican" }));
        await ViewsSeed.ExecuteAsync(host.OperationalDatabase, $"UPDATE vote_member_list_entries SET party = CASE person_id WHEN 'p0' THEN 'Democrat' WHEN 'p1' THEN 'Republican' END WHERE list_id = '{Senate.RecordId}';");

        using var page = await GetAsync(host, $"/v1/votes/{Senate.RecordId}/members");

        var members = page.RootElement.GetProperty("members").EnumerateArray().ToArray();
        Assert.Equal(("Democrat", "Democrat"), (members[0].GetProperty("party").GetString(), members[0].GetProperty("caucus").GetString()));
        Assert.Equal("Republican", members[1].GetProperty("party").GetString());
        Assert.Equal(JsonValueKind.Null, members[1].GetProperty("caucus").ValueKind);
        Assert.Equal(JsonValueKind.Null, members[2].GetProperty("party").ValueKind);
        Assert.Equal(JsonValueKind.Null, members[2].GetProperty("caucus").ValueKind);
    }

    [Theory]
    [InlineData("Nay")]
    [InlineData("nay")]
    [InlineData("NAY")]
    public async Task TheCastFilterMatchesTheStoredWordInAnyCaseBecauseTheViewComparesPlainText(string cast)
    {
        await using var host = await StartWithVotesAsync(House);

        using var page = await GetAsync(host, $"/v1/votes/{House.RecordId}/members?limit=10&cast={cast}");

        Assert.Equal(Enumerable.Range(0, 10).Select(index => index * 3), Positions(page));
        Assert.NotNull(NextCursor(page));
    }

    [Fact]
    public async Task AMultiWordCastWordMatchesInAnyCaseBecauseTheStoredFormHasACapitalLetterOnEachWord()
    {
        await using var host = await StartWithVotesAsync(House);

        using var page = await GetAsync(host, $"/v1/votes/{House.RecordId}/members?limit=50&cast=not%20voting");

        Assert.Equal(Enumerable.Range(0, 40).Select(index => index * 3 + 2), Positions(page));
    }

    [Fact]
    public async Task ACandidateNameFiltersByItsOwnTextBecauseAnElectionVoteStoresNamesAsCasts()
    {
        await using var host = await StartWithVotesAsync(Senate);

        using var page = await GetAsync(host, $"/v1/votes/{Senate.RecordId}/members?cast=Smith");

        Assert.Equal([1], Positions(page));
    }

    [Fact]
    public async Task ACastThatNoMemberCastGivesAnEmptyPageNotAnErrorBecauseTheVoteExists()
    {
        await using var host = await StartWithVotesAsync(House);

        using var page = await GetAsync(host, $"/v1/votes/{House.RecordId}/members?cast=maybe");

        Assert.Empty(Positions(page));
        Assert.Null(NextCursor(page));
        Assert.Equal(1, page.RootElement.GetProperty("release").GetInt32());
    }

    [Fact]
    public async Task TheCastFilterStaysOnEveryPageOfACursorWalkBecauseALaterPageMustNotShowOtherCasts()
    {
        await using var host = await StartWithVotesAsync(House);
        var seen = new List<int>();
        string? cursor = null;

        do
        {
            using var page = await GetAsync(host, $"/v1/votes/{House.RecordId}/members?limit=15&cast=nay" + (cursor is null ? "" : "&cursor=" + cursor));
            seen.AddRange(Positions(page));
            cursor = NextCursor(page);
        }
        while (cursor is not null);

        Assert.Equal(Enumerable.Range(0, 40).Select(index => index * 3), seen);
    }

    [Fact]
    public async Task AVoteOutsideTheActiveReleaseAnswers404LikeTheVoteRouteBecauseNoMemberOfItIsPublic()
    {
        var retired = new SeededVote('9', "2026-03-05") { LastRelease = 1 };
        await using var host = await StartWithVotesAsync(Senate, retired);
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 2, new DateTimeOffset(2026, 6, 9, 0, 0, 0, TimeSpan.Zero));

        using var retiredMembers = await host.Client.GetAsync($"/v1/votes/{retired.RecordId}/members");
        using var retiredVote = await host.Client.GetAsync($"/v1/votes/{retired.RecordId}");
        using var unknown = await host.Client.GetAsync($"/v1/votes/{new string('e', 64)}/members");
        using var malformed = await host.Client.GetAsync("/v1/votes/not-a-record/members");

        Assert.Equal(HttpStatusCode.NotFound, retiredMembers.StatusCode);
        Assert.Equal(await retiredVote.Content.ReadAsStringAsync(), await retiredMembers.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, malformed.StatusCode);
    }

    [Fact]
    public async Task ACursorOfAnOlderReleaseAnswers409WithTheActiveReleaseBecauseAWalkMustNotMixTwoMemberLists()
    {
        await using var host = await StartWithVotesAsync(House);
        using var first = await GetAsync(host, $"/v1/votes/{House.RecordId}/members?limit=50");
        var cursor = NextCursor(first);
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 2, new DateTimeOffset(2026, 6, 9, 0, 0, 0, TimeSpan.Zero));

        using var response = await host.Client.GetAsync($"/v1/votes/{House.RecordId}/members?limit=50&cursor={cursor}");
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        using var restart = await GetAsync(host, $"/v1/votes/{House.RecordId}/members?limit=50");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("the release changed", problem.RootElement.GetProperty("title").GetString());
        Assert.Equal(2, problem.RootElement.GetProperty("release").GetInt32());
        Assert.Equal(2, restart.RootElement.GetProperty("release").GetInt32());
        Assert.Equal(Enumerable.Range(0, 50), Positions(restart));
    }

    [Theory]
    [InlineData("limit=0")]
    [InlineData("limit=51")]
    [InlineData("cursor=garbage")]
    [InlineData("cursor=v.1.20260302.0.1.5.aaaa")]
    [InlineData("cursor=m.0.3")]
    public async Task AnInvalidLimitOrCursorAnswers400BecauseTheBoundsAreTheSameAsTheVoteList(string query)
    {
        await using var host = await StartWithVotesAsync(Senate);

        using var response = await host.Client.GetAsync($"/v1/votes/{Senate.RecordId}/members?{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task TheOfficialFilterListsOnlyTheAskedMembersInPositionOrderWhateverTheOrderOfTheQuery()
    {
        await using var host = await StartWithVotesAsync(House);

        using var page = await GetAsync(host, $"/v1/votes/{House.RecordId}/members?official=p100&official=p7&official=p63");

        Assert.Equal([7, 63, 100], Positions(page));
        Assert.Equal(["Yea", "Nay", "Yea"], page.RootElement.GetProperty("members").EnumerateArray().Select(member => member.GetProperty("cast").GetString()));
        Assert.Null(NextCursor(page));
    }

    [Fact]
    public async Task TheOfficialFilterMeetsTheCastFilterAndAnUnknownOfficialGivesAnEmptyPage()
    {
        await using var host = await StartWithVotesAsync(House);

        using var nays = await GetAsync(host, $"/v1/votes/{House.RecordId}/members?official=p3&official=p4&cast=nay");
        using var nobody = await GetAsync(host, $"/v1/votes/{House.RecordId}/members?official=nobody");

        Assert.Equal([3], Positions(nays));
        Assert.Empty(Positions(nobody));
        Assert.Equal(1, nobody.RootElement.GetProperty("release").GetInt32());
    }

    [Fact]
    public async Task TheOfficialFilterStaysOnALaterPageOfACursorWalk()
    {
        await using var host = await StartWithVotesAsync(House);

        using var first = await GetAsync(host, $"/v1/votes/{House.RecordId}/members?limit=1&official=p5&official=p9");
        using var second = await GetAsync(host, $"/v1/votes/{House.RecordId}/members?limit=1&official=p5&official=p9&cursor={NextCursor(first)}");

        Assert.Equal([5], Positions(first));
        Assert.Equal([9], Positions(second));
        Assert.Null(NextCursor(second));
    }

    [Theory]
    [InlineData("official=")]
    [InlineData("official=p1&official=p1")]
    [InlineData("official=p1&official=p2&official=p3&official=p4")]
    public async Task AnEmptyDuplicateOrFourthOfficialAnswers400WithNoCacheHeaderBecauseTheFilterIsForTheThreeMembersOfOneReader(string query)
    {
        await using var host = await StartWithVotesAsync(Senate);

        using var response = await host.Client.GetAsync($"/v1/votes/{Senate.RecordId}/members?{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(response.Headers.CacheControl);
    }
}
