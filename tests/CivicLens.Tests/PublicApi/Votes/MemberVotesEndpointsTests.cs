using System.Net;
using System.Text.Json;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.Infrastructure.Collection;

namespace CivicLens.Tests.PublicApi.Votes;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class MemberVotesEndpointsTests(PostgresCollection postgres)
{
    private async Task<ApiHost> StartWithVotesAsync(params SeededVote[] votes)
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

    private static string[] Ids(JsonElement answer) =>
        [.. answer.GetProperty("votes").EnumerateArray().Select(vote => vote.GetProperty("recordId").GetString()!)];

    [Fact]
    public async Task OnlySubstantiveKindsAreListedSoARuleVoteNewerThanAPassageVoteIsLeftOutAndTheRuleHasOneOwner()
    {
        var kinds = Enum.GetValues<PublishedVoteKind>();
        var votes = kinds.Select((kind, index) => new SeededVote(Hex[index], $"2026-03-{index + 1:00}") { Kind = kind.ToString(), RollCall = index + 1 }).ToArray();
        await using var host = await StartWithVotesAsync(votes);

        var answer = await GetAsync(host, "/v1/member-votes?official=p0");

        var expected = votes.Where(vote => Enum.Parse<PublishedVoteKind>(vote.Kind).IsSubstantive()).Reverse().Select(vote => vote.RecordId);
        Assert.Equal(expected, Ids(answer));
        Assert.Contains(votes, vote => !Enum.Parse<PublishedVoteKind>(vote.Kind).IsSubstantive() && vote.VotedOn.CompareTo("2026-03-06") > 0);
    }

    [Fact]
    public async Task VotesComeNewestFirstByDateThenLaterRollCallAndStopAtTheDocumentedLimit()
    {
        var votes = Enumerable.Range(0, MemberVoteLimit + 2).Select(index => new SeededVote(Hex[index], $"2026-03-{index / 2 + 1:00}") { RollCall = index + 1 }).ToArray();
        await using var host = await StartWithVotesAsync(votes);

        var answer = await GetAsync(host, "/v1/member-votes?official=p0");

        var expected = votes.OrderByDescending(vote => vote.VotedOn).ThenByDescending(vote => vote.RollCall).Take(MemberVoteLimit).Select(vote => vote.RecordId);
        Assert.Equal(expected, Ids(answer));
    }

    private const int MemberVoteLimit = 10;
    private const string Hex = "0123456789abcdef";

    [Fact]
    public async Task EachVoteCarriesTheCastOfTheAskedOfficialOnlyInSourceWordAndInPlainFirstWords()
    {
        var votes = new[]
        {
            new SeededVote('a', "2026-03-01") { RollCall = 1, Casts = ["Yea", "Nay", "Present"] },
            new SeededVote('b', "2026-03-02") { Chamber = "Senate", RollCall = 2, Casts = ["Aye", "No", "Not Voting"] },
            new SeededVote('c', "2026-03-03") { RollCall = 3, Casts = ["Nay", "Yea", "Yea"] }
        };
        await using var host = await StartWithVotesAsync(votes);

        var first = await GetAsync(host, "/v1/member-votes?official=p0");
        var second = await GetAsync(host, "/v1/member-votes?official=p1");

        Assert.Equal([("Nay", "No (Nay)"), ("Aye", "Yes (Aye)"), ("Yea", "Yes (Yea)")], Casts(first));
        Assert.Equal([("Yea", "Yes (Yea)"), ("No", "No"), ("Nay", "No (Nay)")], Casts(second));
        var vote = first.GetProperty("votes")[1];
        Assert.Equal(("senate", 119, 1, 2, "passage", "hr", 1, "Headline b", "An AI summary of the official record", "Passed b"),
            (vote.GetProperty("chamber").GetString(), vote.GetProperty("congress").GetInt32(), vote.GetProperty("session").GetInt32(), vote.GetProperty("rollCall").GetInt32(),
             vote.GetProperty("kind").GetString(), vote.GetProperty("billType").GetString(), vote.GetProperty("billNumber").GetInt32(), vote.GetProperty("headline").GetString(),
             vote.GetProperty("label").GetString(), vote.GetProperty("result").GetString()));
    }

    private static (string, string)[] Casts(JsonElement answer) =>
        [.. answer.GetProperty("votes").EnumerateArray().Select(vote => (vote.GetProperty("cast").GetString()!, vote.GetProperty("castWord").GetString()!))];

    [Fact]
    public async Task AnOfficialWithNoCastGetsAnEmptyListAndAVoteOutsideTheActiveReleaseIsLeftOut()
    {
        await using var host = await StartWithVotesAsync(new SeededVote('a', "2026-03-01"), new SeededVote('b', "2026-03-02") { FirstRelease = 2 });

        var answer = await GetAsync(host, "/v1/member-votes?official=nobody");

        Assert.Empty(answer.GetProperty("votes").EnumerateArray());
        Assert.Equal("nobody", answer.GetProperty("officialId").GetString());
        Assert.Equal(["a".PadRight(64, 'a')], Ids(await GetAsync(host, "/v1/member-votes?official=p0")));
    }

    [Fact]
    public async Task TheAnswerNamesTheReleaseAndIsCachedForOneHour()
    {
        await using var host = await StartWithVotesAsync(new SeededVote('a', "2026-03-01"));

        using var response = await host.Client.GetAsync("/v1/member-votes?official=p0");

        Assert.Equal("public, max-age=3600", response.Headers.CacheControl?.ToString());
        Assert.Equal(1, JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("release").GetInt32());
    }

    [Theory]
    [InlineData("/v1/member-votes")]
    [InlineData("/v1/member-votes?official=")]
    [InlineData("/v1/member-votes?official=p0&official=p1")]
    public async Task AMissingBlankOrRepeatedOfficialIsRefused(string path)
    {
        await using var host = await StartWithVotesAsync();

        await GetAsync(host, path, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task NoActiveReleaseGivesServiceUnavailable()
    {
        await using var host = await ApiHost.StartAsync(postgres, new FakeReleaseSite());

        await GetAsync(host, "/v1/member-votes?official=p0", HttpStatusCode.ServiceUnavailable);
    }
}
