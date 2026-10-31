using System.Net;
using CivicLens.Application.Votes;
using CivicLens.Application.Collection;
using CivicLens.Core.Actions;
using CivicLens.Core.Documents;
using CivicLens.Core.Relationships;
using CivicLens.Tests.Fixtures;
using CivicLens.Tests.Infrastructure.Collection;

namespace CivicLens.Tests.Host;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class BillLinksWorkspaceTests(PostgresCollection postgres) : IAsyncLifetime
{
    private const string SponsorText = "Sponsor: Warren, Elizabeth\nCosponsor: Other, Person\n";

    private readonly ReviewWorkspaceHost host = new(postgres)
    {
        Issues = StatementGateFixtures.Configuration(),
        OfficialId = "warren",
        OfficialName = "Elizabeth Warren"
    };

    private RelationshipWorldSeeder? seeder;

    private RelationshipWorldSeeder Seeder => seeder ??= new(host.ConnectionString);

    public Task InitializeAsync() => host.StartAsync();

    public Task DisposeAsync() => host.StopAsync();

    [Fact]
    public async Task TheActionPageListsTheVoteDraftsOfItsOwnBillNewestFirstAndSkipsOtherBillsAndCongresses()
    {
        var action = await SeedActionAsync();
        await SeedVoteAsync(10, action.BillNumber, new DateOnly(2025, 4, 10));
        await SeedVoteAsync(20, action.BillNumber, new DateOnly(2025, 5, 1), "Approved");
        await SeedVoteAsync(3, action.BillNumber, new DateOnly(2025, 9, 1), "AwaitingReviewWithConcerns", session: 2);
        await SeedVoteAsync(30, action.BillNumber + 1, new DateOnly(2025, 6, 1));
        await SeedVoteAsync(40, action.BillNumber, new DateOnly(2025, 7, 1), congress: action.Congress - 1);
        host.Client.DefaultRequestHeaders.Add("Cookie", host.CreateCookie("auth0|friend"));

        var page = await host.GetDocumentAsync($"/Review/Actions/{action.RecordId}");

        Assert.Equal("Record ID " + action.RecordId, page.QuerySelector(".page-header-record-id")!.TextContent);
        var rows = page.QuerySelectorAll("[aria-labelledby='bill-votes'] li").ToArray();
        Assert.Equal([Id(3), Id(20), Id(10)], rows.Select(row => row.QuerySelector("a")!.GetAttribute("href")!.Split('/')[^1]));
        Assert.Contains("House roll call 3, Congress 119, session 2", rows[0].TextContent, StringComparison.Ordinal);
        Assert.Contains("Open concerns", rows[0].TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain("0 open", rows[0].TextContent, StringComparison.Ordinal);
        Assert.Contains("House roll call 20, Congress 119, session 1", rows[1].TextContent, StringComparison.Ordinal);
        Assert.Contains("Passage, 2025-05-01", rows[1].TextContent, StringComparison.Ordinal);
        Assert.Contains("Approved", rows[1].TextContent, StringComparison.Ordinal);
        Assert.Contains("House roll call 10, Congress 119, session 1", rows[2].TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain("The newest", page.QuerySelector("[aria-labelledby='bill-votes']")!.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheActionPageShowsOnlyTheNewestVoteDraftsOfABillAndSaysSo()
    {
        var action = await SeedActionAsync();
        for (var roll = 1; roll <= BillVoteDraft.MaximumPerBill + 1; roll++)
            await SeedVoteAsync(roll, action.BillNumber, new DateOnly(2025, 1, 1).AddDays(100 - roll));
        host.Client.DefaultRequestHeaders.Add("Cookie", host.CreateCookie("auth0|friend"));

        var page = await host.GetDocumentAsync($"/Review/Actions/{action.RecordId}");

        var section = page.QuerySelector("[aria-labelledby='bill-votes']")!;
        Assert.Equal(BillVoteDraft.MaximumPerBill, section.QuerySelectorAll("li").Length);
        Assert.Contains($"/Review/Votes/{Id(1)}", section.InnerHtml, StringComparison.Ordinal);
        Assert.DoesNotContain($"/Review/Votes/{Id(BillVoteDraft.MaximumPerBill + 1)}", section.InnerHtml, StringComparison.Ordinal);
        Assert.Contains($"The newest {BillVoteDraft.MaximumPerBill} vote drafts show here.", section.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheActionPageSaysSoWhenNoVoteDraftNamesTheBill()
    {
        var action = await SeedActionAsync();
        host.Client.DefaultRequestHeaders.Add("Cookie", host.CreateCookie("auth0|friend"));

        var page = await host.GetDocumentAsync($"/Review/Actions/{action.RecordId}");

        Assert.Equal("No vote draft names this bill.", page.QuerySelector("[aria-labelledby='bill-votes'] p")!.TextContent);
    }

    [Theory]
    [InlineData("hr", 119, 1234, "https://www.congress.gov/bill/119th-congress/house-bill/1234")]
    [InlineData("sjres", 101, 7, "https://www.congress.gov/bill/101st-congress/senate-joint-resolution/7")]
    [InlineData("hconres", 102, 14, "https://www.congress.gov/bill/102nd-congress/house-concurrent-resolution/14")]
    [InlineData("sres", 113, 3, "https://www.congress.gov/bill/113th-congress/senate-resolution/3")]
    [InlineData("hres", 103, 9, "https://www.congress.gov/bill/103rd-congress/house-resolution/9")]
    [InlineData("s", 118, 55, "https://www.congress.gov/bill/118th-congress/senate-bill/55")]
    [InlineData("hjres", 111, 2, "https://www.congress.gov/bill/111th-congress/house-joint-resolution/2")]
    [InlineData("sconres", 112, 8, "https://www.congress.gov/bill/112th-congress/senate-concurrent-resolution/8")]
    public async Task TheActionPageLinksTheBillOnCongressGovForEachBillTypeAndOrdinal(string type, int congress, int number, string expected)
    {
        var action = await SeedActionAsync(type, congress, number);
        host.Client.DefaultRequestHeaders.Add("Cookie", host.CreateCookie("auth0|friend"));

        var page = await host.GetDocumentAsync($"/Review/Actions/{action.RecordId}");

        var link = page.QuerySelector("[aria-labelledby='evidence'] a")!;
        Assert.Equal(expected, link.GetAttribute("href"));
        Assert.Equal("noopener noreferrer", link.GetAttribute("rel"));
    }

    [Fact]
    public async Task TheLookupBoxOpensTheActionDraftOfABillOrItsRecordIdAndTheVoteDraftOfARollCall()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var congress = CongressCalendar.CongressOn(today)!.Value;
        var action = await SeedActionAsync(congress: congress);
        await SeedVoteAsync(245, 99, today, congress: congress, session: CongressCalendar.SessionOn(today));
        host.Client.DefaultRequestHeaders.Add("Cookie", host.CreateCookie("auth0|friend"));

        using var bill = await host.Client.GetAsync("/Review?find=HR+1234");
        using var explicitBill = await host.Client.GetAsync($"/Review?find={congress}+H.R.+1234");
        using var roll = await host.Client.GetAsync("/Review?find=Roll+245");
        using var recordId = await host.Client.GetAsync($"/Review?find={action.RecordId}");

        Assert.Equal(HttpStatusCode.Redirect, bill.StatusCode);
        Assert.Equal($"/Review/Actions/{action.RecordId}", bill.Headers.Location!.AbsolutePath);
        Assert.Equal($"/Review/Actions/{action.RecordId}", explicitBill.Headers.Location!.AbsolutePath);
        Assert.Equal($"/Review/Votes/{Id(245)}", roll.Headers.Location!.AbsolutePath);
        Assert.Equal($"/Review/Actions/{action.RecordId}", recordId.Headers.Location!.AbsolutePath);
    }

    [Theory]
    [InlineData("HR+77", "No draft for “HR 77”.")]
    [InlineData("Roll+9", "No draft for “Roll 9”.")]
    [InlineData("0000000000000000000000000000000000000000000000000000000000000abc", "No draft for “0000000000000000000000000000000000000000000000000000000000000abc”.")]
    [InlineData("banana", "Enter a bill such as HR 1234 or 118 HR 1234, a roll call such as Roll 245, a record ID, or a link to the record.")]
    [InlineData("118+Roll+9", "Enter a bill such as HR 1234 or 118 HR 1234, a roll call such as Roll 245, a record ID, or a link to the record.")]
    public async Task AnUnknownBillRollCallOrRecordIdAndAnInvalidInputShowAMessageOnTheHomePageWithoutAnError(string query, string message)
    {
        host.Client.DefaultRequestHeaders.Add("Cookie", host.CreateCookie("auth0|friend"));

        using var response = await host.Client.GetAsync($"/Review?find={query}");
        var page = await host.GetDocumentAsync($"/Review?find={query}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(message, page.QuerySelector(".notice")!.TextContent, StringComparison.Ordinal);
        Assert.NotNull(page.QuerySelector("form[role='search'] input[name='find']"));
    }

    private static string Id(int number) => number.ToString("x").PadLeft(64, '0');

    [Fact]
    public async Task TheActionHistoryNamesTheCodeCheckAsTheActorOfACodeApproval()
    {
        var action = await SeedActionAsync();
        await Seeder.ExecuteAsync($$"""
            SET session_replication_role = replica;
            UPDATE action_drafts SET review_state_version = 1, review_status = 'Approved' WHERE record_id = @record;
            INSERT INTO action_review_decisions (decision_id, record_id, revision_number, review_state_version, decision_json, kind, actor_subject, created_at_utc_ticks)
            VALUES ('{{new string('a', 32)}}', @record, 1, 1,
                '{"decisionId":"{{new string('a', 32)}}","draftId":"{{action.RecordId}}","revisionNumber":1,"reviewStateVersion":1,"kind":0,"actorSubject":"civic-lens:code","note":null,"resolvedDecisionIds":[],"createdAtUtc":"2026-10-19T00:00:00+00:00"}',
                'Approve', 'civic-lens:code', 1)
            """, ("record", action.RecordId));
        host.Client.DefaultRequestHeaders.Add("Cookie", host.CreateCookie("auth0|friend"));

        var page = await host.GetDocumentAsync($"/Review/Actions/{action.RecordId}");

        Assert.Contains("Code check", page.QuerySelectorAll("td.hide-narrow").Select(cell => cell.TextContent));
    }

    private async Task<ActionDraftRevision> SeedActionAsync(string type = "hr", int congress = 119, int number = 1234)
    {
        var extraction = await host.SaveExtractionAsync(SponsorText);
        var action = ActionRevisionFixtures.Model(1) with
        {
            BillType = type,
            Congress = congress,
            BillNumber = number,
            Sponsorships = [ActionRevisionFixtures.Sponsorship() with { Citations = [new(extraction.ExtractionId, 0, 26), new(extraction.ExtractionId, 27, 24)] }],
            TrackedIssueIds = []
        };
        var configuration = CollectionConfigurationRevision.Create(HouseVoteFixtures.Configuration());
        await Seeder.ExecuteAsync("""
            SET session_replication_role = replica;
            INSERT INTO collection_configuration_revisions (revision_id, json, created_at) VALUES (@revision, @configuration, 1);
            INSERT INTO action_candidates (candidate_id, source_id, congress, bill_type, bill_number, update_date_utc_ticks, detail_job_id,
                configuration_revision_id, created_at_utc_ticks, includes_summaries)
            VALUES (@candidate, @source, @congress, @type, @number, 1, 'detail', @revision, 1, false)
            """, ("revision", configuration.Id), ("configuration", configuration.Json), ("candidate", action.CandidateId),
            ("source", action.SourceId), ("congress", congress), ("type", type), ("number", number));
        await Seeder.ActionAsync(action, "AwaitingReview");
        return action;
    }

    // The seeder gives every candidate the same job IDs, so each roll call needs its own candidate row first.
    private async Task SeedVoteAsync(int roll, int bill, DateOnly votedOn, string status = "AwaitingReview", int congress = 119, int session = 1)
    {
        var vote = RelationshipFixtures.Vote(bill, congress: congress) with
        {
            RecordId = Id(roll),
            CandidateId = Id(roll + 10_000),
            SessionNumber = session,
            RollCallNumber = roll,
            VotedOn = votedOn
        };
        await Seeder.ExecuteAsync("""
            SET session_replication_role = replica;
            INSERT INTO vote_candidates (candidate_id, source_id, chamber, congress, session_number, roll_call_number, update_date_utc_ticks, bill_type,
                bill_number, detail_job_id, members_job_id, detail_extraction_id, members_extraction_id, configuration_revision_id, created_at_utc_ticks)
            VALUES (@candidate, @source, 'House', @congress, @session, @roll, 1, @type, @bill, 'detail-' || @roll, 'members-' || @roll, 'x', 'x', 'x', 1)
            """, ("candidate", vote.CandidateId), ("source", vote.SourceId), ("congress", congress), ("session", session),
            ("roll", roll), ("type", vote.BillType!), ("bill", vote.BillNumber!.Value));
        await Seeder.VoteAsync(vote, status);
    }
}
