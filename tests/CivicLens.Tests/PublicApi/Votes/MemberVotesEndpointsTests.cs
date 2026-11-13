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
        var host = await ApiHost.StartAsync(postgres);
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

    private static JsonElement Votes(JsonElement answer, int official = 0) => answer.GetProperty("officials")[official].GetProperty("votes");

    private static string[] Ids(JsonElement answer, int official = 0) =>
        [.. Votes(answer, official).EnumerateArray().Select(vote => vote.GetProperty("recordId").GetString()!)];

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
            new SeededVote('b', "2026-03-02") { Chamber = "Senate", RollCall = 2, Casts = ["Aye", "No", "Not Voting"], Result = "Agreed to" },
            new SeededVote('c', "2026-03-03") { RollCall = 3, Casts = ["Nay", "Yea", "Yea"] }
        };
        await using var host = await StartWithVotesAsync(votes);

        var first = await GetAsync(host, "/v1/member-votes?official=p0");
        var second = await GetAsync(host, "/v1/member-votes?official=p1");

        Assert.Equal([("Nay", "No (Nay)"), ("Aye", "Yes (Aye)"), ("Yea", "Yes (Yea)")], Casts(first));
        Assert.Equal([("Yea", "Yes (Yea)"), ("No", "No"), ("Nay", "No (Nay)")], Casts(second));
        var vote = Votes(first)[1];
        Assert.Equal(("senate", 119, 1, 2, "passage", "hr", 1, "Headline b", "An AI summary of the official record", "Agreed to"),
            (vote.GetProperty("chamber").GetString(), vote.GetProperty("congress").GetInt32(), vote.GetProperty("session").GetInt32(), vote.GetProperty("rollCall").GetInt32(),
             vote.GetProperty("kind").GetString(), vote.GetProperty("billType").GetString(), vote.GetProperty("billNumber").GetInt32(), vote.GetProperty("headline").GetString(),
             vote.GetProperty("label").GetString(), vote.GetProperty("result").GetString()));
    }

    [Fact]
    public async Task MemberVoteExposesKnownSideAndStoredPartyMajorityWhileUnknownValuesRemainNull()
    {
        var vote = new SeededVote('a', "2026-03-01") { Casts = ["Aye", "No", "Present"] };
        var tiedVote = new SeededVote('b', "2026-03-02") { Casts = ["Aye", "No", "Present"] };
        await using var host = await StartWithVotesAsync(vote, tiedVote);
        await ViewsSeed.ExecuteAsync(host.OperationalDatabase, $"""
            UPDATE vote_member_list_entries SET party = CASE person_id WHEN 'p0' THEN 'Democrat' WHEN 'p1' THEN 'Republican' END WHERE list_id IN ('{vote.RecordId}', '{tiedVote.RecordId}');
            UPDATE vote_revisions SET democrat_majority = CASE record_id WHEN '{vote.RecordId}' THEN 'Yea' END,
                republican_majority = CASE record_id WHEN '{vote.RecordId}' THEN 'Yea' ELSE 'Nay' END, columns_written = true
             WHERE record_id IN ('{vote.RecordId}', '{tiedVote.RecordId}');
            """);

        var answer = await GetAsync(host, "/v1/member-votes?official=p0&official=p1&official=p2");

        var p0 = Votes(answer, 0)[0];
        var p1 = Votes(answer, 1)[0];
        var p2 = Votes(answer, 2)[0];
        var p0Earlier = Votes(answer, 0)[1];
        var p1Earlier = Votes(answer, 1)[1];
        Assert.Equal(("Yea", "Yea"), (p0Earlier.GetProperty("side").GetString(), p0Earlier.GetProperty("partyMajorityCast").GetString()));
        Assert.Equal(("Nay", "Yea"), (p1Earlier.GetProperty("side").GetString(), p1Earlier.GetProperty("partyMajorityCast").GetString()));
        Assert.Equal("Nay", p1.GetProperty("partyMajorityCast").GetString());
        Assert.Equal(JsonValueKind.Null, p0.GetProperty("partyMajorityCast").ValueKind);
        Assert.Equal(JsonValueKind.Null, p2.GetProperty("side").ValueKind);
        Assert.Equal(JsonValueKind.Null, p2.GetProperty("partyMajorityCast").ValueKind);
    }

    [Fact]
    public async Task EachVoteSendsPlainWordsForKindResultAndCastNextToTheSourceWordsFromTheOneTable()
    {
        var votes = new[]
        {
            new SeededVote('a', "2026-03-01") { RollCall = 1, Kind = "Suspension", Result = "Failed" },
            new SeededVote('b', "2026-03-02") { RollCall = 2, Kind = "Passage", Result = "Agreed to" },
            new SeededVote('c', "2026-03-03") { RollCall = 3, Kind = "Nomination", Result = "Nomination Rejected" },
            new SeededVote('d', "2026-03-04") { RollCall = 4, Kind = "Passage", Result = "Point of Order Sustained" }
        };
        await using var host = await StartWithVotesAsync(votes);

        var answer = await GetAsync(host, "/v1/member-votes?official=p0");

        var rows = Votes(answer).EnumerateArray().Select(vote => (
            vote.GetProperty("kind").GetString(), vote.GetProperty("kindWords").GetString(),
            vote.GetProperty("result").GetString(), vote.GetProperty("resultWords").GetString(),
            vote.GetProperty("cast").GetString(), vote.GetProperty("castWords").GetString()));
        Assert.Equal(
            [("passage", "Vote to pass or agree to", "Point of Order Sustained", "Point of Order Sustained", "Yea", "Yes (Yea)"),
             ("nomination", "Vote to confirm", "Nomination Rejected", "Not confirmed (Nomination Rejected)", "Yea", "Yes (Yea)"),
             ("passage", "Vote to pass or agree to", "Agreed to", "Passed (Agreed to)", "Yea", "Yes (Yea)"),
             ("suspension", "Vote to pass with limited debate", "Failed", "Failed", "Yea", "Yes (Yea)")],
            rows);
    }

    private static (string, string)[] Casts(JsonElement answer) =>
        [.. Votes(answer).EnumerateArray().Select(vote => (vote.GetProperty("cast").GetString()!, vote.GetProperty("castWords").GetString()!))];

    [Fact]
    public async Task AnOfficialWithNoCastGetsAnEmptyListAndAVoteOutsideTheActiveReleaseIsLeftOut()
    {
        await using var host = await StartWithVotesAsync(new SeededVote('a', "2026-03-01"), new SeededVote('b', "2026-03-02") { FirstRelease = 2 });

        var answer = await GetAsync(host, "/v1/member-votes?official=nobody");

        Assert.Empty(Votes(answer).EnumerateArray());
        Assert.Equal("nobody", answer.GetProperty("officials")[0].GetProperty("officialId").GetString());
        Assert.Equal(["a".PadRight(64, 'a')], Ids(await GetAsync(host, "/v1/member-votes?official=p0")));
    }

    [Fact]
    public async Task AVoteWhereTheOfficialHasNoCastIsNotListedWhileAnotherVoteWithTheOfficialIs()
    {
        var votes = new[]
        {
            new SeededVote('a', "2026-03-02") { RollCall = 2, Casts = ["Yea"] },
            new SeededVote('b', "2026-03-01") { RollCall = 1, Casts = ["Yea", "Nay", "Present"] }
        };
        await using var host = await StartWithVotesAsync(votes);

        var answer = await GetAsync(host, "/v1/member-votes?official=p2");

        Assert.Equal([votes[1].RecordId], Ids(answer));
        Assert.Equal([("Present", "Present")], Casts(answer));
    }

    [Fact]
    public async Task TheAnswerNamesItsReleaseAndSendsNoCacheHeaderLikeTheOtherVoteRoutes()
    {
        await using var host = await StartWithVotesAsync(new SeededVote('a', "2026-03-01"));

        using var response = await host.Client.GetAsync("/v1/member-votes?official=p0");

        Assert.Null(response.Headers.CacheControl);
        Assert.Equal(1, JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("release").GetInt32());
    }

    [Theory]
    [InlineData("/v1/member-votes")]
    [InlineData("/v1/member-votes?official=")]
    [InlineData("/v1/member-votes?official=p0&official=")]
    [InlineData("/v1/member-votes?official=p0&official=p0")]
    [InlineData("/v1/member-votes?official=p0&official=p1&official=p2&official=p3")]
    [InlineData("/v1/member-votes?official=p0&issue=a&issue=b")]
    public async Task AMissingEmptyDuplicateOrFourthOfficialAndARepeatedIssueAreRefusedWithNoCacheHeader(string path)
    {
        await using var host = await StartWithVotesAsync();

        using var response = await host.Client.GetAsync(path);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(response.Headers.CacheControl);
    }

    [Fact]
    public async Task NoActiveReleaseGivesServiceUnavailable()
    {
        await using var host = await ApiHost.StartAsync(postgres);

        await GetAsync(host, "/v1/member-votes?official=p0", HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task ThreeOfficialsGetOneListEachInTheOrderOfTheRequestAndAnOfficialWithNoCastKeepsAnEmptyEntry()
    {
        var votes = new[]
        {
            new SeededVote('a', "2026-03-01") { RollCall = 1, Casts = ["Yea", "Nay", "Present"] },
            new SeededVote('b', "2026-03-02") { RollCall = 2, Casts = ["Nay"] },
            new SeededVote('c', "2026-03-03") { RollCall = 3, Casts = ["Yea", "Yea"] }
        };
        await using var host = await StartWithVotesAsync(votes);

        var answer = await GetAsync(host, "/v1/member-votes?official=p2&official=nobody&official=p0");

        Assert.Equal(["p2", "nobody", "p0"], answer.GetProperty("officials").EnumerateArray().Select(entry => entry.GetProperty("officialId").GetString()));
        Assert.Equal([votes[0].RecordId], Ids(answer, 0));
        Assert.Empty(Ids(answer, 1));
        Assert.Equal([votes[2].RecordId, votes[1].RecordId, votes[0].RecordId], Ids(answer, 2));
    }

    [Fact]
    public async Task EachOfficialGetsItsOwnNewestTenSoABusyOfficialDoesNotCrowdOutAnotherWithOlderVotes()
    {
        var recent = Enumerable.Range(0, MemberVoteLimit + 2).Select(index => new SeededVote(Hex[index], $"2026-03-{index + 2:00}") { RollCall = index + 1, Casts = ["Yea"] });
        var old = new SeededVote('f', "2026-01-01") { RollCall = 50, Casts = ["Yea", "Nay"] };
        await using var host = await StartWithVotesAsync([.. recent, old]);

        var answer = await GetAsync(host, "/v1/member-votes?official=p0&official=p1");

        Assert.Equal(MemberVoteLimit, Ids(answer, 0).Length);
        Assert.DoesNotContain(old.RecordId, Ids(answer, 0));
        Assert.Equal([old.RecordId], Ids(answer, 1));
    }

    [Fact]
    public async Task TheIssueFilterKeepsOnlyVotesTaggedWithTheTrackedIssueAndNeverUsesAnUntrackedTag()
    {
        var votes = new[]
        {
            new SeededVote('a', "2026-03-01") { RollCall = 1, Issues = ["health"] },
            new SeededVote('b', "2026-03-02") { RollCall = 2, Issues = ["tax"] },
            new SeededVote('c', "2026-03-03") { RollCall = 3, Issues = ["health", "tax"] },
            new SeededVote('d', "2026-03-04") { RollCall = 4, UntrackedIssues = ["health"] },
            new SeededVote('e', "2026-03-05") { RollCall = 5, Casts = ["Yea", "Yea"], Issues = ["health"] },
            new SeededVote('f', "2026-03-06") { RollCall = 6, Issues = ["health"], Kind = "Rule" }
        };
        await using var host = await StartWithVotesAsync(votes);

        var answer = await GetAsync(host, "/v1/member-votes?official=p0&official=p2&issue=health");

        Assert.Equal([votes[4].RecordId, votes[2].RecordId, votes[0].RecordId], Ids(answer, 0));
        Assert.Equal([votes[2].RecordId, votes[0].RecordId], Ids(answer, 1));
        Assert.Empty(Ids(await GetAsync(host, "/v1/member-votes?official=p0&issue=unknown")));
    }

    [Fact]
    public async Task TheIssueFilterUsesTheIssuesOfTheActiveRevisionOnly()
    {
        var retagged = new SeededVote('a', "2026-03-01") { ActiveRevision = 2, EarlierIssues = ["health"] };
        var tagged = new SeededVote('b', "2026-03-02") { RollCall = 2, ActiveRevision = 2, EarlierIssues = ["tax"], Issues = ["health"] };
        await using var host = await StartWithVotesAsync(retagged, tagged);

        var answer = await GetAsync(host, "/v1/member-votes?official=p0&issue=health");

        Assert.Equal([tagged.RecordId], Ids(answer));
    }
}
