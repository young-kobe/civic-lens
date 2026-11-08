using System.Net;
using System.Text.Json;
using CivicLens.Tests.Infrastructure.Collection;

namespace CivicLens.Tests.PublicApi.Votes;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class SharedMemberVotesEndpointsTests(PostgresCollection postgres)
{
    private async Task<ApiHost> StartAsync(params SeededVote[] votes)
    {
        var host = await ApiHost.StartAsync(postgres, new FakeReleaseSite());
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 1, new DateTimeOffset(2026, 6, 2, 0, 0, 0, TimeSpan.Zero));
        await ViewsSeed.AddVotesAsync(host.OperationalDatabase, votes);
        return host;
    }

    private static async Task<JsonElement> GetAsync(ApiHost host, string path, HttpStatusCode expected = HttpStatusCode.OK)
    {
        using var response = await host.Client.GetAsync(path);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == expected, $"{path} gave {(int)response.StatusCode}: {body}");
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    private static JsonElement Votes(JsonElement answer) => answer.GetProperty("votes");

    private static string[] Ids(JsonElement answer) =>
        [.. Votes(answer).EnumerateArray().Select(vote => vote.GetProperty("recordId").GetString()!)];

    [Fact]
    public async Task SharedSetSelectsTenPerChamberAndReturnsEveryAskedCastInTheSameOrdering()
    {
        var votes = Enumerable.Range(0, 12).SelectMany(index => new[]
        {
            new SeededVote('a', $"2026-03-{index + 1:00}")
            {
                RecordId = index.ToString("x64"), RollCall = index + 1, Result = "Agreed to", Casts = ["Yea", "Nay"]
            },
            new SeededVote('b', $"2026-04-{index + 1:00}")
            {
                RecordId = (index + 100).ToString("x64"), Chamber = "Senate", RollCall = index + 1, Result = "Agreed to", Casts = ["Aye", "No"]
            }
        }).ToArray();
        await using var host = await StartAsync(votes);

        var answer = await GetAsync(host, "/v1/member-votes/shared?official=p1&official=p0&official=missing");

        Assert.Equal(["p1", "p0", "missing"], answer.GetProperty("officialIds").EnumerateArray().Select(id => id.GetString()));
        Assert.Equal(Enumerable.Range(102, 10).Reverse().Concat(Enumerable.Range(2, 10).Reverse()).Select(id => id.ToString("x64")), Ids(answer));
        Assert.Equal(10, Votes(answer).EnumerateArray().Count(vote => vote.GetProperty("chamber").GetString() == "house"));
        Assert.Equal(10, Votes(answer).EnumerateArray().Count(vote => vote.GetProperty("chamber").GetString() == "senate"));
        Assert.Equal(["p1", "p0"], Votes(answer)[0].GetProperty("casts").EnumerateArray().Select(cast => cast.GetProperty("officialId").GetString()));
        Assert.Equal("No", Votes(answer)[0].GetProperty("casts")[0].GetProperty("cast").GetString());
        Assert.Equal("Aye", Votes(answer)[0].GetProperty("casts")[1].GetProperty("cast").GetString());
        Assert.Equal("Agreed to", Votes(answer)[0].GetProperty("result").GetString());
        Assert.Equal("Passed (Agreed to)", Votes(answer)[0].GetProperty("resultWords").GetString());
    }

    [Fact]
    public async Task SharedSetDoesNotApplyThePerOfficialTenVoteCapAcrossChambers()
    {
        var votes = Enumerable.Range(0, 10).SelectMany(index => new[]
        {
            new SeededVote('a', $"2026-03-{index + 1:00}") { RecordId = index.ToString("x64"), RollCall = index + 1, Casts = ["Yea"] },
            new SeededVote('b', $"2026-03-{index + 1:00}") { RecordId = (index + 100).ToString("x64"), Chamber = "Senate", RollCall = index + 1, Casts = ["Aye"] }
        }).ToArray();
        await using var host = await StartAsync(votes);

        var shared = await GetAsync(host, "/v1/member-votes/shared?official=p0");
        var individual = await GetAsync(host, "/v1/member-votes?official=p0");

        Assert.Equal(20, Ids(shared).Length);
        var individualIds = individual.GetProperty("officials")[0].GetProperty("votes").EnumerateArray().Select(vote => vote.GetProperty("recordId").GetString()).ToArray();
        Assert.Equal(10, individualIds.Length);
        Assert.Contains(Ids(shared), id => !individualIds.Contains(id));
    }

    [Fact]
    public async Task IssueFilterIsAppliedBeforeEachChamberLimitAndUsesCurrentTrackedSubstantiveRevisions()
    {
        var votes = Enumerable.Range(0, 11).Select(index => new SeededVote((char)('a' + index), $"2026-03-{index + 1:00}")
        {
            RecordId = (index + 1).ToString("x64"),
            RollCall = index + 1,
            Issues = index == 0 ? ["health"] : ["tax"]
        }).ToArray();
        votes = [.. votes, new SeededVote('c', "2026-04-01")
        {
            RecordId = 20.ToString("x64"), RollCall = 20, Kind = "Rule", Issues = ["health"]
        }, new SeededVote('d', "2026-04-02")
        {
            RecordId = 21.ToString("x64"), RollCall = 21, ActiveRevision = 2, EarlierIssues = ["health"], Issues = ["tax"]
        }, new SeededVote('e', "2026-04-03")
        {
            RecordId = 22.ToString("x64"), RollCall = 22, FirstRelease = 2, Issues = ["health"]
        }, new SeededVote('f', "2026-04-04")
        {
            RecordId = 23.ToString("x64"), RollCall = 23, UntrackedIssues = ["health"]
        }];
        await using var host = await StartAsync(votes);

        var answer = await GetAsync(host, "/v1/member-votes/shared?official=p0&issue=health");

        Assert.Equal([votes[0].RecordId], Ids(answer));
    }

    [Theory]
    [InlineData("/v1/member-votes/shared")]
    [InlineData("/v1/member-votes/shared?official=p0&official=p0")]
    [InlineData("/v1/member-votes/shared?official=")]
    [InlineData("/v1/member-votes/shared?official=p0&issue=a&issue=b")]
    public async Task SharedRouteUsesTheExistingOfficialRequestValidation(string path)
    {
        await using var host = await StartAsync();

        await GetAsync(host, path, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task SharedRouteAcceptsSixteenOfficialsAndRejectsSeventeenWhileLegacyRouteKeepsItsThreeOfficialCap()
    {
        await using var host = await StartAsync(new SeededVote('a', "2026-03-01"));
        var sixteen = string.Join("&", Enumerable.Range(0, 16).Select(index => $"official=p{index}"));

        var four = await GetAsync(host, "/v1/member-votes/shared?official=p0&official=p1&official=p2&official=p3");
        Assert.Equal(4, four.GetProperty("officialIds").GetArrayLength());
        var answer = await GetAsync(host, $"/v1/member-votes/shared?{sixteen}");
        Assert.Equal(16, answer.GetProperty("officialIds").GetArrayLength());
        await GetAsync(host, $"/v1/member-votes/shared?{sixteen}&official=p16", HttpStatusCode.BadRequest);
        await GetAsync(host, "/v1/member-votes?official=p0&official=p1&official=p2&official=p3", HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task SharedRowsRemainWhenNoRequestedOfficialHasACastAndEmptyResultsStayValid()
    {
        await using var host = await StartAsync(new SeededVote('a', "2026-03-01"));

        var answer = await GetAsync(host, "/v1/member-votes/shared?official=unknown");
        Assert.Single(Votes(answer).EnumerateArray());
        Assert.Empty(Votes(answer)[0].GetProperty("casts").EnumerateArray());
        var empty = await GetAsync(host, "/v1/member-votes/shared?official=unknown&issue=missing");
        Assert.Empty(Votes(empty).EnumerateArray());
    }

    [Fact]
    public async Task SharedSelectionFollowsWithdrawalsAndRollback()
    {
        var withdrawn = new SeededVote('a', "2026-03-02") { LastRelease = 1 };
        var kept = new SeededVote('b', "2026-03-01");
        await using var host = await StartAsync(withdrawn, kept);
        Assert.Equal([withdrawn.RecordId, kept.RecordId], Ids(await GetAsync(host, "/v1/member-votes/shared?official=p0")));

        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 2, new DateTimeOffset(2026, 6, 3, 0, 0, 0, TimeSpan.Zero));
        var current = await GetAsync(host, "/v1/member-votes/shared?official=p0");
        Assert.Equal(2, current.GetProperty("release").GetInt32());
        Assert.Equal([kept.RecordId], Ids(current));

        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 1, new DateTimeOffset(2026, 6, 2, 0, 0, 0, TimeSpan.Zero));
        var rollback = await GetAsync(host, "/v1/member-votes/shared?official=p0");
        Assert.Equal(1, rollback.GetProperty("release").GetInt32());
        Assert.Equal([withdrawn.RecordId, kept.RecordId], Ids(rollback));
    }

    [Fact]
    public async Task AReleaseWithNoRecordsRetainsItsIdentityAndEmptySharedVotes()
    {
        await using var host = await StartAsync();
        await ViewsSeed.ExecuteAsync(host.OperationalDatabase, "UPDATE publication_releases SET record_count = 0 WHERE release_number = 1");

        var shared = await GetAsync(host, "/v1/member-votes/shared?official=unknown");
        Assert.Equal(1, shared.GetProperty("release").GetInt32());
        Assert.Equal(["unknown"], shared.GetProperty("officialIds").EnumerateArray().Select(id => id.GetString()));
        Assert.Empty(Votes(shared).EnumerateArray());

        var individual = await GetAsync(host, "/v1/member-votes?official=unknown");
        Assert.Equal(1, individual.GetProperty("release").GetInt32());
        Assert.Empty(individual.GetProperty("officials")[0].GetProperty("votes").EnumerateArray());
    }

    [Fact]
    public async Task NoActiveReleaseAnswersServiceUnavailable()
    {
        await using var host = await ApiHost.StartAsync(postgres, new FakeReleaseSite());

        await GetAsync(host, "/v1/member-votes/shared?official=p0", HttpStatusCode.ServiceUnavailable);
    }
}
