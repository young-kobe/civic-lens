using CivicLens.Publication.Contracts;
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

        Assert.Equal(["Question: On a", "Result: Passed a"], detail.RootElement.GetProperty("facts").EnumerateArray().Select(fact => fact.GetProperty("text").GetString()));
    }

    [Fact]
    public async Task ABillShowsTheLatestActionThatTheActiveReleasePinnedAndNullWhenTheReleasePinnedNone()
    {
        await using var host = await StartAsync();
        await BillsSeed.AddBillsAsync(host.OperationalDatabase, new SeededBill('a', "2026-03-02") { BillNumber = 101 }, new SeededBill('b', "2026-03-02") { BillNumber = 102 });
        await ViewsSeed.ExecuteAsync(host.OperationalDatabase, $"""
            SET session_replication_role = replica;
            {PublicRecordPageRows.PinnedStatus(1, new string('a', 64), "Received in the Senate.", "2026-02-13", new DateTimeOffset(2026, 2, 14, 8, 0, 0, TimeSpan.Zero).UtcTicks)}
            {PublicRecordPageRows.Gate(101, 2, 200, "Presented to the President.", "2026-03-20", 800)}
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
            {PublicRecordPageRows.Headline("statement", Statement, "Doe on rent")}
            {PublicRecordPageRows.Headline("action", actions[0], "Housing bill", "Model")}
            {PublicRecordPageRows.Headline("action", actions[1], "Tax bill", "Code")}
            {PublicRecordPageRows.Headline("vote", Vote, "Vote on housing")}
            {PublicRecordPageRows.Link(links[0], Statement, actions[0], "action", "DescribesOwnBill", "2026-05-02")}
            {PublicRecordPageRows.Link(links[1], Statement, actions[1], "action", "ClaimsCreditFor", "2026-05-01")}
            {PublicRecordPageRows.Link(links[2], Statement, Vote, "vote", "StatementAboutVote", null)}
            {PublicRecordPageRows.RelationshipRevision(links[0], 1, Statement, actions[0], "action", "DescribesOwnBill", "The statement names this bill.")}
            {PublicRecordPageRows.RelationshipRevision(links[0], 2, Statement, actions[0], "action", "DescribesOwnBill", "Unpublished draft reason.")}
            """);

        using var first = await GetAsync(host, $"/v1/records/statement/{Statement}/related?limit=2");
        var cursor = first.RootElement.GetProperty("nextCursor").GetString();
        using var second = await GetAsync(host, $"/v1/records/statement/{Statement}/related?limit=2&cursor={cursor}");
        using var fromAction = await GetAsync(host, $"/v1/records/action/{actions[0]}/related");

        Assert.Equal([$"Tax bill|action|claimsCreditFor|2026-05-01|code|{AiLabelWords.CodeWording}", $"Housing bill|action|describesOwnBill|2026-05-02|model|{AiLabelWords.ModelWording}"], Describe(first));
        Assert.Equal(["Vote on housing|vote|statementAboutVote|none|person|none"], Describe(second));
        Assert.Equal(JsonValueKind.Null, second.RootElement.GetProperty("nextCursor").ValueKind);
        Assert.Equal(["Doe on rent|statement|describesOwnBill|2026-05-02|person|none"], Describe(fromAction));
        Assert.Equal(links[1], first.RootElement.GetProperty("related")[0].GetProperty("linkRecordId").GetString());
        Assert.Equal("The statement names this bill.", first.RootElement.GetProperty("related")[1].GetProperty("reason").GetString());
        Assert.Equal("The statement names this bill.", fromAction.RootElement.GetProperty("related")[0].GetProperty("reason").GetString());
        Assert.Equal(JsonValueKind.Null, second.RootElement.GetProperty("related")[0].GetProperty("reason").ValueKind);
    }

    [Fact]
    public async Task RelatedRecordsGiveAnEmptyListFor404ForAnUnknownRecordAnd400ForAKindThatNoRelationshipCitesAnd409ForACursorOfAnotherRelease()
    {
        await using var host = await StartAsync($"""
            {PublicRecordPageRows.Link(new string('7', 64), Statement, Action, "action", "DescribesOwnBill", "2026-05-02")}
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
        Assert.Equal(["https://fr.example/9", "https://fr.example/5"], detail.RootElement.GetProperty("notices").EnumerateArray().Select(notice => notice.GetProperty("url").GetString()));
        Assert.DoesNotContain("bioguide", detail.RootElement.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AnExecutiveActionListsItsTypedTargetsWithARecordIdOnlyForAnExactMatchInTheActiveRelease()
    {
        await using var host = await StartAsync();
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 2, new DateTimeOffset(2026, 6, 3, 0, 0, 0, TimeSpan.Zero));
        await ViewsSeed.ExecuteAsync(host.OperationalDatabase, "SET session_replication_role = replica;\n" + ExecutiveActionTargetRows.Seed());

        using var detail = await GetAsync(host, $"/v1/executive-actions/{ExecutiveActionTargetRows.Source}");
        using var plain = await GetAsync(host, $"/v1/executive-actions/{ExecutiveActionTargetRows.LinkedOrder}");

        Assert.Equal([
            $"0|amends|executiveOrder|14100|EO 14100, January 3, 2025|{ExecutiveActionTargetRows.LinkedOrder}",
            "1|amends|executiveOrder|14101|EO 14101|none",
            "2|amends|executiveOrder|14102|EO 14102|none",
            $"3|revokes|proclamation|10001|Proc. 10001|{ExecutiveActionTargetRows.LinkedProclamation}",
            "4|revokes|executiveOrder|14000|EO 14000|none"
        ], detail.RootElement.GetProperty("targets").EnumerateArray().Select(target =>
            $"{target.GetProperty("position").GetInt32()}|{target.GetProperty("relation").GetString()}|{target.GetProperty("kind").GetString()}|{target.GetProperty("number").GetString()}|" +
            $"{target.GetProperty("reference").GetString()}|{(target.GetProperty("targetRecordId").ValueKind == JsonValueKind.Null ? "none" : target.GetProperty("targetRecordId").GetString())}"));
        Assert.Empty(plain.RootElement.GetProperty("targets").EnumerateArray());
    }

    [Fact]
    public async Task ATargetReferenceUsesItsExactUtf16SpanAfterSupplementaryCharactersAndRepeatedNumbers()
    {
        await using var host = await StartAsync();
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 2, DateTimeOffset.UnixEpoch.AddDays(1));
        await ViewsSeed.ExecuteAsync(host.OperationalDatabase, "SET session_replication_role = replica;\n" + ExecutiveActionTargetRows.Seed());
        var notes = "See: " + string.Concat(Enumerable.Repeat(char.ConvertFromUtf32(0x20000), 27))
            + "\nAmends: EO 14100, January 3, 2025; EO 14100, February 4, 2025";
        await ViewsSeed.ExecuteAsync(host.OperationalDatabase, $"""
            UPDATE executive_action_revisions SET disposition_notes = '{notes}' WHERE record_id = '{ExecutiveActionTargetRows.Source}';
            DELETE FROM executive_action_revision_disposition_targets WHERE record_id = '{ExecutiveActionTargetRows.Source}' AND position > 0;
            UPDATE executive_action_revision_disposition_targets SET reference_start = 68, reference_length = 25
             WHERE record_id = '{ExecutiveActionTargetRows.Source}' AND position = 0;
            """);

        using var detail = await GetAsync(host, $"/v1/executive-actions/{ExecutiveActionTargetRows.Source}");
        var target = Assert.Single(detail.RootElement.GetProperty("targets").EnumerateArray());

        Assert.Equal("EO 14100, January 3, 2025", target.GetProperty("reference").GetString());
        Assert.Equal(ExecutiveActionTargetRows.LinkedOrder, target.GetProperty("targetRecordId").GetString());
    }

    [Theory]
    [InlineData(100000, 1)]
    [InlineData(1, int.MaxValue)]
    public async Task ATargetOutsideItsNoteHasNoReference(int start, int length)
    {
        await using var host = await StartAsync();
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 2, DateTimeOffset.UnixEpoch.AddDays(1));
        await ViewsSeed.ExecuteAsync(host.OperationalDatabase, "SET session_replication_role = replica;\n" + ExecutiveActionTargetRows.Seed());
        await ViewsSeed.ExecuteAsync(host.OperationalDatabase, $"""
            UPDATE executive_action_revision_disposition_targets SET reference_start = {start}, reference_length = {length}
             WHERE record_id = '{ExecutiveActionTargetRows.Source}' AND position = 0;
            """);

        using var detail = await GetAsync(host, $"/v1/executive-actions/{ExecutiveActionTargetRows.Source}");

        Assert.Equal(JsonValueKind.Null, detail.RootElement.GetProperty("targets")[0].GetProperty("reference").ValueKind);
    }

    [Fact]
    public async Task ATargetWhoseKindDoesNotMatchItsReferenceHasNoReference()
    {
        await using var host = await StartAsync();
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 2, DateTimeOffset.UnixEpoch.AddDays(1));
        await ViewsSeed.ExecuteAsync(host.OperationalDatabase, "SET session_replication_role = replica;\n" + ExecutiveActionTargetRows.Seed());
        await ViewsSeed.ExecuteAsync(host.OperationalDatabase, $"""
            UPDATE executive_action_revision_disposition_targets SET kind = 'Proclamation'
             WHERE record_id = '{ExecutiveActionTargetRows.Source}' AND position = 0;
            """);

        using var detail = await GetAsync(host, $"/v1/executive-actions/{ExecutiveActionTargetRows.Source}");

        Assert.Equal(JsonValueKind.Null, detail.RootElement.GetProperty("targets")[0].GetProperty("reference").ValueKind);
    }

    [Fact]
    public async Task ATargetSpanThatSplitsASupplementaryCharacterHasNoReference()
    {
        await using var host = await StartAsync();
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 2, DateTimeOffset.UnixEpoch.AddDays(1));
        await ViewsSeed.ExecuteAsync(host.OperationalDatabase, "SET session_replication_role = replica;\n" + ExecutiveActionTargetRows.Seed());
        var notes = "Amends: EO 14100, " + char.ConvertFromUtf32(0x20000);
        await ViewsSeed.ExecuteAsync(host.OperationalDatabase, $"""
            UPDATE executive_action_revisions SET disposition_notes = '{notes}' WHERE record_id = '{ExecutiveActionTargetRows.Source}';
            UPDATE executive_action_revision_disposition_targets SET reference_start = 8, reference_length = 11
             WHERE record_id = '{ExecutiveActionTargetRows.Source}' AND position = 0;
            """);

        using var detail = await GetAsync(host, $"/v1/executive-actions/{ExecutiveActionTargetRows.Source}");

        Assert.Equal(JsonValueKind.Null, detail.RootElement.GetProperty("targets")[0].GetProperty("reference").ValueKind);
    }

    [Theory]
    [InlineData(100, HttpStatusCode.OK)]
    [InlineData(101, HttpStatusCode.InternalServerError)]
    public async Task TheTargetCountBoundaryReturnsCompleteDataOrAnOperationalFailure(int count, HttpStatusCode expected)
    {
        await using var host = await StartAsync();
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 2, DateTimeOffset.UnixEpoch.AddDays(1));
        await ViewsSeed.ExecuteAsync(host.OperationalDatabase, "SET session_replication_role = replica;\n" + ExecutiveActionTargetRows.Seed());
        await ViewsSeed.ExecuteAsync(host.OperationalDatabase, $"""
            SET session_replication_role = replica;
            DELETE FROM executive_action_revision_disposition_targets WHERE record_id = '{ExecutiveActionTargetRows.Source}';
            INSERT INTO executive_action_revision_disposition_targets (record_id, revision_number, position, relation, kind, number, reference_start, reference_length)
            SELECT '{ExecutiveActionTargetRows.Source}', 1, n, 'Amends', 'ExecutiveOrder', (20000 + n)::text, 0, 1 FROM generate_series(0, {count - 1}) n;
            """);

        using var response = await host.Client.GetAsync($"/v1/executive-actions/{ExecutiveActionTargetRows.Source}");
        Assert.Equal(expected, response.StatusCode);
        if (response.IsSuccessStatusCode)
        {
            using var detail = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(count, detail.RootElement.GetProperty("targets").GetArrayLength());
        }
    }

    private static string[] Describe(JsonDocument page) => [.. page.RootElement.GetProperty("related").EnumerateArray().Select(link =>
        $"{link.GetProperty("headline").GetString()}|{link.GetProperty("kind").GetString()}|{link.GetProperty("linkType").GetString()}|" +
        (link.GetProperty("statementDate").ValueKind == JsonValueKind.Null ? "none" : link.GetProperty("statementDate").GetString()) +
        $"|{link.GetProperty("textOrigin").GetString()}|{(link.GetProperty("label").ValueKind == JsonValueKind.Null ? "none" : link.GetProperty("label").GetString())}")];
}
