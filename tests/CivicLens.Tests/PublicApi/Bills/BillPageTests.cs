using System.Net;
using AngleSharp.Html.Parser;
using CivicLens.PublicApi;
using CivicLens.PublicApi.Pages;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.Infrastructure.Collection;
using CivicLens.Tests.PublicApi.Officials;
using CivicLens.Tests.PublicApi.Records;
using CivicLens.Tests.PublicApi.Votes;
using Npgsql;

namespace CivicLens.Tests.PublicApi.Bills;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class BillPageTests(PostgresCollection postgres)
{
    private static readonly PublicBaseUrl BaseUrl = PublicBaseUrl.Parse("https://civic-lens.test", "test")!;
    private static readonly DateTimeOffset PublishedAt = new(2026, 6, 2, 0, 0, 0, TimeSpan.Zero);
    private static readonly string BillId = new('a', 64);
    private static readonly SeededBill Bill = new('a', "2026-03-02")
    {
        BillNumber = 7,
        Issues = ["health"],
        Sponsorships =
        [
            new("p1", "Sponsor", "2026-03-02"), new("p2", "Cosponsor", "2026-03-02"), new("p3", "Cosponsor", "2026-04-01", null, false),
            new("p4", "Cosponsor", "2026-04-02", "2026-04-20", false)
        ]
    };

    private async Task<ApiHost> StartAsync(SeededOfficial? thirdMember = null)
    {
        var host = await ApiHost.StartAsync(postgres, options => options with { PublicBaseUrl = BaseUrl });
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 1, PublishedAt);
        await BillsSeed.AddBillsAsync(host.OperationalDatabase, Bill);
        await OfficialsSeed.AddOfficialsAsync(host.OperationalDatabase, Official("p1", "Pat One", "Democrat"), PartySwitcher(), thirdMember ?? Official("p3", "Bob Three", "Republican"),
            Official("p4", "Cy Four", "Republican"));
        await ViewsSeed.ExecuteAsync(host.OperationalDatabase, """
            INSERT INTO release_issues (release_number, issue_id, name, definition) VALUES (1, 'health', 'Health care', 'Care.');
            """);
        await SeedHistoryAsync(host.OperationalDatabase);
        await ViewsSeed.AddVotesAsync(host.OperationalDatabase, new SeededVote('e', "2026-03-03") { BillNumber = 7 }, new SeededVote('f', "2026-03-04") { BillNumber = 8 });
        await CorrectionHistorySeed.BindEmptyAsync(host.OperationalDatabase);
        return host;
    }

    private static SeededOfficial Official(string id, string name, string party) =>
        new(id, name, new SeededTerm("House", "Representative", "2020-01-01") { Party = party, State = "MA", District = 1 });

    /// <summary>Ann Two joined as a Democrat and held the seat as a Republican on the publication day.</summary>
    private static SeededOfficial PartySwitcher() => new("p2", "Ann Two",
        new SeededTerm("House", "Representative", "2020-01-01") { Party = "Democrat", State = "MA", District = 1, EndsOn = "2026-03-10" },
        new SeededTerm("House", "Representative", "2026-03-11") { Party = "Republican", State = "MA", District = 1 });

    [Fact]
    public async Task TheServerPageShowsTheBillFromOnePublishedReleaseWithItsStepsBackersAndVotes()
    {
        await using var host = await StartAsync();

        var response = await host.Client.GetAsync($"{ServerPageEndpoints.Prefix}/records/{BillId}.html");
        var page = await new HtmlParser().ParseDocumentAsync(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("\"r1-", response.Headers.ETag!.Tag);
        Assert.StartsWith("H.R. 7, ", page.QuerySelector("main h1")!.TextContent);
        Assert.Equal($"https://civic-lens.test/records/{BillId}.html", page.QuerySelector("link[rel=canonical]")!.GetAttribute("href"));
        Assert.Equal(["Introduced in the House", "Passed the House", "Presented to the President", "Became law"], page.QuerySelectorAll(".bill-stepper b").Select(step => step.TextContent));
        Assert.Equal("2 Mar 2026, by Pat One", page.QuerySelector(".bill-stepper li")!.QuerySelector(".bill-step-detail")!.TextContent);
        Assert.Contains("Sponsored by Pat One, a Democrat.", page.QuerySelector(".bill-head .bill-lede")!.TextContent);
        Assert.Contains("This site tracks 2 House members who cosponsored it: 1 on the day it was introduced and 1 who joined later.", page.QuerySelector("#bill-backers-title")!.ParentElement!.TextContent);
        Assert.DoesNotContain("Cy Four", page.QuerySelector("#bill-backers-title")!.ParentElement!.TextContent);
        Assert.Equal(["1 Democrats", "1 Republicans"], page.QuerySelectorAll("[aria-labelledby=bill-backers-title] .party-bar-legend li").Select(item => System.Text.RegularExpressions.Regex.Replace(item.TextContent, @"\s+", " ").Trim()));
        Assert.Equal("Bill on Congress.gov", page.QuerySelector("details#source a")!.TextContent);
        Assert.Equal("https://www.congress.gov/bill/119th-congress/house-bill/7", page.QuerySelector("details#source a")!.GetAttribute("href"));
        Assert.Equal(["Health care"], page.QuerySelectorAll(".link-chip").Select(chip => chip.TextContent));
        var votes = page.QuerySelectorAll(".bill-votes h3 a").ToArray();
        Assert.Equal([$"https://civic-lens.test/records/{new string('e', 64)}.html"], votes.Select(link => link.GetAttribute("href")));
        var totals = page.QuerySelectorAll(".bill-votes .party-bar-legend li").Select(item => System.Text.RegularExpressions.Regex.Replace(item.TextContent, @"\s+", " ").Trim());
        Assert.Equal(["2 Yes", "1 No", "0 Not voting"], totals);
        Assert.DoesNotContain("This site has no Senate roll call vote", page.QuerySelector(".bill-votes")!.TextContent);
    }

    [Fact]
    public async Task ACosponsorWhoseTermsGiveNoSinglePartyOnTheJoinDayCountsUnderNoPartyListedAndNeverUnderTheCurrentParty()
    {
        var noTermOnThatDay = new SeededOfficial("p3", "Bob Three", new SeededTerm("House", "Representative", "2026-05-01") { Party = "Republican", State = "MA", District = 1 });
        var twoPartiesOnThatDay = new SeededOfficial("p3", "Bob Three",
            new SeededTerm("House", "Representative", "2020-01-01") { Party = "Republican", State = "MA", District = 1 },
            new SeededTerm("Senate", "Senator", "2026-03-20") { Party = "Democrat", State = "MA" });

        foreach (var member in new[] { noTermOnThatDay, twoPartiesOnThatDay })
        {
            await using var host = await StartAsync(member);
            var response = await host.Client.GetAsync($"{ServerPageEndpoints.Prefix}/records/{BillId}.html");
            var page = await new HtmlParser().ParseDocumentAsync(await response.Content.ReadAsStringAsync());
            var legend = page.QuerySelectorAll("[aria-labelledby=bill-backers-title] .party-bar-legend li").Select(item => System.Text.RegularExpressions.Regex.Replace(item.TextContent, @"\s+", " ").Trim());

            Assert.Equal(["1 Democrats", "1 No party listed"], legend);
        }
    }

    [Fact]
    public async Task AVoteRowShowsTheSplitBadgeFromTheStoredAlignment()
    {
        await using var host = await StartAsync();
        await ViewsSeed.ExecuteAsync(host.OperationalDatabase, $"UPDATE vote_revisions SET alignment = 'Split', columns_written = true WHERE record_id = '{new string('e', 64)}';");

        var page = await ReadPageAsync(host, BillId);

        Assert.Equal(["Split vote"], page.QuerySelectorAll(".bill-votes .shared-vote-row .badge").Select(badge => badge.TextContent.Trim()));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task ASenatePassageStepShowsTheGapNoteOnlyWhenNoSenateVoteNamesTheBill(bool senateVote, bool gapNote)
    {
        await using var host = await StartAsync();
        await AddSenatePassageAsync(host.OperationalDatabase);
        if (senateVote) await ViewsSeed.AddVotesAsync(host.OperationalDatabase, new SeededVote('c', "2026-03-08") { Chamber = "Senate", BillNumber = 7 });

        var page = await ReadPageAsync(host, BillId);

        Assert.Contains("Passed the Senate", page.QuerySelectorAll(".bill-stepper b").Select(step => step.TextContent));
        Assert.Equal(gapNote, page.QuerySelector(".bill-votes")!.TextContent.Contains("This site has no Senate roll call vote on this bill.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ASenateVoteOlderThanTheFirstFiftyHouseVotesStillCountsSoTheGapNoteStaysHidden()
    {
        await using var host = await StartAsync();
        await AddSenatePassageAsync(host.OperationalDatabase);
        var houseVotes = Enumerable.Range(1, PublicationProtocol.PageSize + 1)
            .Select(index => new SeededVote('d', "2026-04-01") { BillNumber = 7, RollCall = 100 + index, RecordId = (100 + index).ToString("x64") });
        await ViewsSeed.AddVotesAsync(host.OperationalDatabase, [.. houseVotes, new SeededVote('c', "2026-03-01") { Chamber = "Senate", BillNumber = 7, RecordId = 1.ToString("x64") }]);

        var page = await ReadPageAsync(host, BillId);

        Assert.Equal(PublicationProtocol.PageSize, page.QuerySelectorAll(".bill-votes .shared-vote-row").Length);
        Assert.DoesNotContain("This site has no Senate roll call vote", page.QuerySelector(".bill-votes")!.TextContent);
    }

    [Fact]
    public async Task ABillOnlyIntroducedInTheSenateShowsNoGapNoteAndNoVotesSection()
    {
        await using var host = await ApiHost.StartAsync(postgres, options => options with { PublicBaseUrl = BaseUrl });
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 1, PublishedAt);
        await BillsSeed.AddBillsAsync(host.OperationalDatabase, new SeededBill('b', "2026-03-02") { BillType = "s", BillNumber = 9, Sponsorships = [new("p1", "Sponsor", "2026-03-02")] });
        await OfficialsSeed.AddOfficialsAsync(host.OperationalDatabase, Official("p1", "Pat One", "Democrat"));
        await CorrectionHistorySeed.BindEmptyAsync(host.OperationalDatabase);

        var page = await ReadPageAsync(host, new string('b', 64));

        Assert.Equal(["Introduced in the Senate"], page.QuerySelectorAll(".bill-stepper b").Select(step => step.TextContent));
        Assert.Null(page.QuerySelector(".bill-votes"));
    }

    [Fact]
    public async Task TheSourceFoldShowsTheStoredApprovalTimeAndTheFirstPublicationTime()
    {
        await using var host = await StartAsync();
        var checkedAt = new DateTimeOffset(2026, 11, 5, 10, 5, 0, TimeSpan.Zero);
        var firstPublished = new DateTimeOffset(2026, 11, 5, 10, 7, 0, TimeSpan.Zero);
        // A later approval decision proves that the page reads the stored column and does not pick a decision itself.
        await ViewsSeed.ExecuteAsync(host.OperationalDatabase, $"""
            SET session_replication_role = replica;
            INSERT INTO published_entries (kind, record_id, revision_number, action_record_id, action_revision_number, headline, bill_title, approved_at_utc_ticks)
            VALUES ('action', '{BillId}', 1, '{BillId}', 1, 'H', 'T', {checkedAt.UtcTicks});
            INSERT INTO action_review_decisions (decision_id, record_id, revision_number, review_state_version, kind, actor_subject, created_at_utc_ticks)
            VALUES ('{new string('1', 32)}', '{BillId}', 1, 1, 'Approve', 'code', {checkedAt.AddHours(2).UtcTicks});
            INSERT INTO published_records (kind, record_id, first_published_at_utc_ticks) VALUES ('action', '{BillId}', {firstPublished.UtcTicks});
            RESET session_replication_role;
            """);

        var page = await ReadPageAsync(host, BillId);

        var fold = page.QuerySelector("details#source dl")!;
        var checkedTerm = fold.QuerySelectorAll("dt").Single(term => term.TextContent == "Checked");
        Assert.Equal("5 Nov 2026, 10:05 UTC. First published 5 Nov 2026, 10:07 UTC.", checkedTerm.NextElementSibling!.TextContent);
    }

    [Theory]
    [InlineData("Model", "1", true)]
    [InlineData("Model", null, false)]
    [InlineData("Person", "1", false)]
    public async Task TheCrsLineShowsOnlyForModelTextDraftedFromTheCrsSummary(string origin, string? summaryVersion, bool shown)
    {
        await using var host = await StartAsync();
        var version = summaryVersion is null ? "NULL" : $"'{summaryVersion}'";
        var citation = summaryVersion is null ? "NULL, NULL, NULL" : "'ext', 0, 1";
        await ViewsSeed.ExecuteAsync(host.OperationalDatabase, $"""
            SET session_replication_role = replica;
            UPDATE action_revisions SET text_origin = '{origin}', summary_version = {version}, crs_summary_text = CASE WHEN {version} IS NULL THEN NULL ELSE 'CRS text.' END,
                   (summary_citation_extraction_id, summary_citation_start, summary_citation_length) = ({citation})
             WHERE record_id = '{BillId}';
            RESET session_replication_role;
            """);

        var page = await ReadPageAsync(host, BillId);

        Assert.Equal(shown, page.Body!.TextContent.Contains("The text above summarizes the Congressional Research Service summary.", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Model", "A summary.", "An AI summary of the official record")]
    [InlineData("Code", "A summary.", "Written automatically from Congress.gov data, with no AI")]
    [InlineData("Person", "A summary.", null)]
    [InlineData("Model", " ", null)]
    public async Task TheSourceFoldStatesTheWordingOfTheSummaryByItsStoredOriginAndAPersonSummaryOrNoSummaryHasNoWordingRow(string origin, string summary, string? wording)
    {
        await using var host = await StartAsync();
        await ViewsSeed.ExecuteAsync(host.OperationalDatabase,
            $"SET session_replication_role = replica; UPDATE action_revisions SET text_origin = '{origin}', summary = '{summary}' WHERE record_id = '{BillId}';");

        var page = await ReadPageAsync(host, BillId);

        var term = page.QuerySelectorAll("details#source dt").SingleOrDefault(item => item.TextContent == "Wording");
        Assert.Equal(wording, term?.NextElementSibling!.TextContent);
    }

    [Fact]
    public async Task AnUnknownRecordAndAMalformedRecordIdGetTheNotFoundPage()
    {
        await using var host = await StartAsync();

        foreach (var id in new[] { new string('9', 64), "not-an-id" })
        {
            var response = await host.Client.GetAsync($"{ServerPageEndpoints.Prefix}/records/{id}.html");
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }

    [Fact]
    public async Task ThePageAnswers304ForTheETagOfItsRelease()
    {
        await using var host = await StartAsync();
        var path = $"{ServerPageEndpoints.Prefix}/records/{BillId}.html";
        var first = await host.Client.GetAsync(path);
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.IfNoneMatch.Add(first.Headers.ETag!);

        using var second = await host.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotModified, second.StatusCode);
    }

    private static async Task<AngleSharp.Html.Dom.IHtmlDocument> ReadPageAsync(ApiHost host, string recordId)
    {
        var response = await host.Client.GetAsync($"{ServerPageEndpoints.Prefix}/records/{recordId}.html");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await new HtmlParser().ParseDocumentAsync(await response.Content.ReadAsStringAsync());
    }

    private static Task AddSenatePassageAsync(string connectionString) => ViewsSeed.ExecuteAsync(connectionString, """
        SET session_replication_role = replica;
        UPDATE published_bill_histories SET action_count = 4 WHERE detail_job_id = 'history';
        INSERT INTO published_bill_history_actions (detail_job_id, position, action_date, text, type)
        VALUES ('history', 3, DATE '2026-03-05', 'Passed Senate without amendment by Unanimous Consent.', 'Floor');
        RESET session_replication_role;
        """);

    private static async Task SeedHistoryAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            SET session_replication_role = replica;
            INSERT INTO published_bill_histories (detail_job_id, source_id, congress, bill_type, bill_number, observed_at_utc_ticks, action_count, public_law)
            VALUES ('history', 'congress', 119, 'hr', 7, 1, 3, '119-7');
            INSERT INTO published_bill_history_actions (detail_job_id, position, action_date, text, type) VALUES
              ('history', 0, DATE '2026-03-05', 'Passed/agreed to in House: On passage Passed by voice vote.', 'Floor'),
              ('history', 1, DATE '2026-03-06', 'Presented to President.', 'President'),
              ('history', 2, DATE '2026-03-07', 'Became Public Law No: 119-7.', 'BecameLaw');
            INSERT INTO release_bill_histories (release_number, record_id, detail_job_id) VALUES (1, @record, 'history');
            RESET session_replication_role;
            """, connection);
        command.Parameters.AddWithValue("record", BillId);
        await command.ExecuteNonQueryAsync();
    }
}
