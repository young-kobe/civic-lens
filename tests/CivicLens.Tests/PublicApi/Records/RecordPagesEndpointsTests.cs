using System.Net;
using System.Text.Json;
using CivicLens.Tests.Fixtures;
using CivicLens.Tests.Infrastructure.Collection;
using CivicLens.Tests.PublicApi.Bills;
using CivicLens.Tests.PublicApi.Votes;

namespace CivicLens.Tests.PublicApi.Records;

/// <summary>The routes that serve the bill page, the vote page, the related records panel, and the executive action page.</summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class RecordPagesEndpointsTests(PostgresCollection postgres)
{
    private static readonly string Statement = new('5', 64);
    private static readonly string Action = new('a', 64);
    private static readonly string Vote = new('1', 64);
    private static readonly string ExecutiveActionId = new('e', 64);

    private async Task<ApiHost> StartAsync(string? sql = null)
    {
        var host = await ApiHost.StartAsync(postgres, new FakeReleaseSite());
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 1, new DateTimeOffset(2026, 6, 2, 0, 0, 0, TimeSpan.Zero));
        if (sql is not null) await ViewsSeed.ExecuteAsync(host.OperationalDatabase, "SET session_replication_role = replica;\n" + sql);
        return host;
    }

    private static async Task<JsonDocument> GetAsync(ApiHost host, string path, HttpStatusCode expected = HttpStatusCode.OK)
    {
        using var response = await host.Client.GetAsync(path);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == expected, $"{path} gave {(int)response.StatusCode}: {body}");
        return JsonDocument.Parse(body);
    }

    [Fact]
    public async Task EveryVoteOnABillIsListedAcrossPagesWithItsResultAndCountsAndNoVoteOfAnotherBillOrCongress()
    {
        await using var host = await StartAsync();
        await ViewsSeed.AddVotesAsync(host.OperationalDatabase,
            new SeededVote('a', "2026-03-05") { RollCall = 1, Kind = "Rule", Casts = ["Yea", "Nay"] },
            new SeededVote('b', "2026-03-04") { RollCall = 2, Kind = "Passage", Casts = ["Yea", "Yea", "Nay", "Present"] },
            new SeededVote('c', "2026-03-03") { Chamber = "Senate", RollCall = 3, Kind = "Cloture", Casts = ["Yea", "Nay", "Not Voting"] },
            new SeededVote('d', "2026-03-02") { Chamber = "Senate", RollCall = 4, Kind = "Passage", Casts = ["Aye", "No"] },
            new SeededVote('e', "2026-03-01") { RollCall = 5, Kind = "Amendment", Casts = ["Yea"] },
            new SeededVote('f', "2026-03-06") { RollCall = 6, Congress = 118 },
            new SeededVote('1', "2026-03-07") { RollCall = 7, BillNumber = 8 },
            new SeededVote('2', "2026-03-08") { RollCall = 8, BillType = "s" });

        var seen = new List<JsonElement>();
        string? cursor = null;
        var pages = 0;
        do
        {
            using var page = await GetAsync(host, "/v1/votes?billType=HR&billNumber=1&congress=119&limit=2" + (cursor is null ? "" : "&cursor=" + cursor));
            seen.AddRange(page.RootElement.GetProperty("votes").EnumerateArray().Select(vote => vote.Clone()));
            cursor = page.RootElement.GetProperty("nextCursor").GetString();
            pages++;
        } while (cursor is not null);

        Assert.Equal(3, pages);
        Assert.Equal(["a", "b", "c", "d", "e"], seen.Select(vote => vote.GetProperty("recordId").GetString()![..1]));
        Assert.Equal(["Passed a", "Passed b", "Passed c", "Passed d", "Passed e"], seen.Select(vote => vote.GetProperty("result").GetString()));
        Assert.Equal(["1|1|0|0", "2|1|1|0", "1|1|0|1", "1|1|0|0", "1|0|0|0"], seen.Select(vote =>
            $"{vote.GetProperty("yea").GetInt32()}|{vote.GetProperty("nay").GetInt32()}|{vote.GetProperty("present").GetInt32()}|{vote.GetProperty("notVoting").GetInt32()}"));
    }

    [Fact]
    public async Task AVoteShowsItsFactTextsInPositionOrder()
    {
        await using var host = await StartAsync();
        await ViewsSeed.AddVotesAsync(host.OperationalDatabase, new SeededVote('a', "2026-03-05"));

        using var detail = await GetAsync(host, $"/v1/votes/{new string('a', 64)}");

        Assert.Equal(["Question: On a", "Result: Passed a"], detail.RootElement.GetProperty("facts").EnumerateArray().Select(fact => fact.GetString()));
    }

    [Fact]
    public async Task ABillShowsItsLatestActionFromTheNewestGateRowAndNullWhenNoGateHoldsOne()
    {
        await using var host = await StartAsync();
        await BillsSeed.AddBillsAsync(host.OperationalDatabase, new SeededBill('a', "2026-03-02") { BillNumber = 101 }, new SeededBill('b', "2026-03-02") { BillNumber = 102 });
        await ViewsSeed.ExecuteAsync(host.OperationalDatabase, $"""
            SET session_replication_role = replica;
            {PublicRecordPageRows.Gate(101, 1, 100, "Referred to the Committee on Ways and Means.", "2026-01-12", 500)}
            {PublicRecordPageRows.Gate(101, 2, 200, "Received in the Senate.", "2026-02-13", new DateTimeOffset(2026, 2, 14, 8, 0, 0, TimeSpan.Zero).UtcTicks)}
            {PublicRecordPageRows.Gate(102, 1, 100, null, null, 700)}
            """);

        using var withStatus = await GetAsync(host, $"/v1/bills/{new string('a', 64)}");
        using var withoutStatus = await GetAsync(host, $"/v1/bills/{new string('b', 64)}");

        var latest = withStatus.RootElement.GetProperty("latestAction");
        Assert.Equal("Received in the Senate.", latest.GetProperty("text").GetString());
        Assert.Equal("2026-02-13", latest.GetProperty("date").GetString());
        Assert.Equal(new DateTimeOffset(2026, 2, 14, 8, 0, 0, TimeSpan.Zero), latest.GetProperty("readAtUtc").GetDateTimeOffset());
        Assert.Equal(JsonValueKind.Null, withoutStatus.RootElement.GetProperty("latestAction").ValueKind);
    }

    [Fact]
    public async Task RelatedRecordsComeOldestStatementFirstUndatedLastAndPageByCursor()
    {
        string[] links = [new('7', 64), new('8', 64), new('9', 64)];
        string[] actions = [new('a', 64), new('b', 64)];
        await using var host = await StartAsync($"""
            {PublicRecordPageRows.Link(links[0], Statement, "Doe on rent", actions[0], "action", "Housing bill", "DescribesOwnBill", "2026-05-02")}
            {PublicRecordPageRows.Link(links[1], Statement, "Doe on rent", actions[1], "action", "Tax bill", "ClaimsCreditFor", "2026-05-01")}
            {PublicRecordPageRows.Link(links[2], Statement, "Doe on rent", Vote, "vote", "Vote on housing", "StatementAboutVote", null)}
            """);

        using var first = await GetAsync(host, $"/v1/records/statement/{Statement}/related?limit=2");
        var cursor = first.RootElement.GetProperty("nextCursor").GetString();
        using var second = await GetAsync(host, $"/v1/records/statement/{Statement}/related?limit=2&cursor={cursor}");
        using var fromAction = await GetAsync(host, $"/v1/records/action/{actions[0]}/related");

        Assert.Equal(["Tax bill|action|claimsCreditFor|2026-05-01", "Housing bill|action|describesOwnBill|2026-05-02"], Describe(first));
        Assert.Equal(["Vote on housing|vote|statementAboutVote|none"], Describe(second));
        Assert.Equal(JsonValueKind.Null, second.RootElement.GetProperty("nextCursor").ValueKind);
        Assert.Equal(["Doe on rent|statement|describesOwnBill|2026-05-02"], Describe(fromAction));
        Assert.Equal(links[1], first.RootElement.GetProperty("related")[0].GetProperty("linkRecordId").GetString());
    }

    [Fact]
    public async Task RelatedRecordsGiveAnEmptyListFor404ForAnUnknownRecordAnd400ForAKindThatNoRelationshipCitesAnd409ForACursorOfAnotherRelease()
    {
        await using var host = await StartAsync($"""
            {PublicRecordPageRows.Link(new string('7', 64), Statement, "Doe on rent", Action, "action", "Housing bill", "DescribesOwnBill", "2026-05-02")}
            {PublicRecordPageRows.Entry("action", new string('b', 64), "Lonely bill")}
            INSERT INTO publication_record_states (kind, record_id, first_release, revision_number, last_release) VALUES ('action', '{new string('b', 64)}', 1, 1, NULL);
            """);

        using var empty = await GetAsync(host, $"/v1/records/action/{new string('b', 64)}/related");
        await GetAsync(host, $"/v1/records/action/{new string('c', 64)}/related", HttpStatusCode.NotFound);
        await GetAsync(host, $"/v1/records/statement/{new string('b', 64)}/related", HttpStatusCode.NotFound);
        await GetAsync(host, $"/v1/records/brief/{Statement}/related", HttpStatusCode.BadRequest);
        await GetAsync(host, $"/v1/records/statement/{Statement}/related?cursor=r.9.n.{new string('7', 64)}", HttpStatusCode.Conflict);

        Assert.Empty(empty.RootElement.GetProperty("related").EnumerateArray());
    }

    [Fact]
    public async Task AnExecutiveActionShowsItsFactsAndNoticesAndNoBioguideAndIsNotFoundWhenNotActive()
    {
        await using var host = await StartAsync(PublicRecordPageRows.ExecutiveAction(ExecutiveActionId));

        using var detail = await GetAsync(host, $"/v1/executive-actions/{ExecutiveActionId}");
        await GetAsync(host, $"/v1/executive-actions/{new string('f', 64)}", HttpStatusCode.NotFound);

        var action = detail.RootElement.GetProperty("executiveAction");
        Assert.Equal(1, detail.RootElement.GetProperty("release").GetInt32());
        Assert.Equal(2, action.GetProperty("revisionNumber").GetInt32());
        Assert.Equal("Title two", action.GetProperty("headline").GetString());
        Assert.Equal("executiveOrder", action.GetProperty("kind").GetString());
        Assert.Equal("president", action.GetProperty("officialId").GetString());
        Assert.Equal("2026-01-20", action.GetProperty("signedOn").GetString());
        Assert.Equal(["2026-00009", "2026-00005"], detail.RootElement.GetProperty("notices").EnumerateArray().Select(notice => notice.GetProperty("documentNumber").GetString()));
        Assert.DoesNotContain("bioguide", detail.RootElement.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    private static string[] Describe(JsonDocument page) => [.. page.RootElement.GetProperty("related").EnumerateArray().Select(link =>
        $"{link.GetProperty("headline").GetString()}|{link.GetProperty("kind").GetString()}|{link.GetProperty("linkType").GetString()}|" +
        (link.GetProperty("statementDate").ValueKind == JsonValueKind.Null ? "none" : link.GetProperty("statementDate").GetString()))];
}
