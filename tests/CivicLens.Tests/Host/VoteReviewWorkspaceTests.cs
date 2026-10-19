using System.Net;
using System.Text.Json;
using CivicLens.Application.Collection;
using CivicLens.Core.Documents;
using CivicLens.Core.Review;
using CivicLens.Core.Votes;
using CivicLens.Tests.Fixtures;
using CivicLens.Tests.Infrastructure.Collection;
using Npgsql;
using static CivicLens.Tests.Host.ReviewWorkspaceHost;

namespace CivicLens.Tests.Host;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class VoteReviewWorkspaceTests(PostgresCollection postgres) : IAsyncLifetime
{
    private readonly ReviewWorkspaceHost host = new(postgres) { Issues = StatementGateFixtures.Configuration() };

    public Task InitializeAsync() => host.StartAsync();

    public Task DisposeAsync() => host.StopAsync();

    [Fact]
    public async Task TheRailLinksTheVoteListWhichCountsEachFilter()
    {
        host.Client.DefaultRequestHeaders.Add("Cookie", host.CreateCookie("auth0|friend"));

        var page = await host.GetDocumentAsync("/Review/Votes");

        Assert.Equal("page", page.QuerySelector(".nav a[href='/Review/Votes']")!.GetAttribute("aria-current"));
        Assert.Null(page.QuerySelector(".nav a[href='/Review']")!.GetAttribute("aria-current"));
        Assert.Equal(["0", "0", "0"], page.QuerySelectorAll("[aria-labelledby='drafts'] .segmented-count").Select(count => count.TextContent));
        Assert.Equal(["/Review/Votes#drafts", "/Review/Votes?drafts=needs-action#drafts", "/Review/Votes?drafts=approved#drafts"],
            page.QuerySelectorAll("[aria-labelledby='drafts'] a.segmented-item").Select(tab => tab.GetAttribute("href")));
        Assert.Equal("No vote records yet", page.QuerySelector("[aria-labelledby='drafts'] .empty h3")!.TextContent);
    }

    [Fact]
    public async Task AnUnknownOrMalformedVoteRecordIsNotFound()
    {
        host.Client.DefaultRequestHeaders.Add("Cookie", host.CreateCookie("auth0|owner"));

        foreach (var id in new[] { new string('a', 64), "not-a-record" })
        {
            using var missing = await host.Client.GetAsync($"/Review/Votes/{id}");
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        }
    }

    [Fact]
    public async Task TheVotePageShowsTheTrackedMembersCastAndTheKindWithTheirCitedLines()
    {
        host.Client.DefaultRequestHeaders.Add("Cookie", host.CreateCookie("auth0|friend"));
        var recordId = await SeedVoteDraftAsync();

        var page = await host.GetDocumentAsync($"/Review/Votes/{recordId}");

        var members = page.QuerySelector("[aria-labelledby='members']")!.TextContent;
        Assert.Contains("A: Yea", members, StringComparison.Ordinal);
        Assert.Contains("Member: A000001, A, R, AL, Yea", members, StringComparison.Ordinal);
        var evidence = page.QuerySelector("[aria-labelledby='evidence']")!.TextContent;
        Assert.Contains("Vote kind: Passage", evidence, StringComparison.Ordinal);
        Assert.Contains("Question: On Passage", evidence, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheTriagePageShowsTheQuotesAndPassingCheckAndAnInlineApproveBindsTheRowRevisionThenRedirects()
    {
        host.Client.DefaultRequestHeaders.Add("Cookie", host.CreateCookie("auth0|friend"));
        var recordId = await SeedVoteDraftAsync();

        var page = await host.GetDocumentAsync("/Review/Votes/Triage");

        var row = Assert.Single(page.QuerySelectorAll("[aria-labelledby='drafts'] tbody tr"));
        Assert.Contains("Question: On Passage", row.TextContent, StringComparison.Ordinal);
        Assert.Contains("Approval check passed", row.TextContent, StringComparison.Ordinal);
        Assert.Contains("Passage", row.TextContent, StringComparison.Ordinal);
        Assert.Null(row.QuerySelector("input[name='targets']"));
        Assert.Equal($"/Review/Votes/{recordId}", row.QuerySelector("a.btn")!.GetAttribute("href"));
        using var approved = await host.Client.PostAsync("/Review/Votes/Triage", TriageDecision(page, "approve"));

        Assert.Equal(HttpStatusCode.Redirect, approved.StatusCode);
        Assert.EndsWith("/Review/Votes/Triage?decided=approved#drafts", approved.Headers.Location!.OriginalString, StringComparison.Ordinal);
        Assert.Equal(1L, await CountAsync($"SELECT count(*) FROM vote_review_decisions WHERE record_id = '{recordId}' AND revision_number = 1 AND kind = 'Approve'"));
        var after = await host.GetDocumentAsync("/Review/Votes/Triage");
        Assert.Empty(after.QuerySelectorAll("[aria-labelledby='drafts'] tbody tr"));
    }

    [Fact]
    public async Task ARowWhoseCitedExtractionIsMissingRendersWithAFailedCheckAndNoQuoteSoOneBadRowKeepsThePageUp()
    {
        await SeedVoteDraftAsync(citedExtractionId: new string('f', 64));
        host.Client.DefaultRequestHeaders.Add("Cookie", host.CreateCookie("auth0|friend"));

        var page = await host.GetDocumentAsync("/Review/Votes/Triage");

        var row = Assert.Single(page.QuerySelectorAll("[aria-labelledby='drafts'] tbody tr"));
        Assert.Contains("Approval check failed", row.TextContent, StringComparison.Ordinal);
        Assert.Contains("missing extraction", row.TextContent, StringComparison.Ordinal);
        Assert.Null(row.QuerySelector("blockquote"));
        Assert.Null(row.QuerySelector("button[name='approve']"));
        Assert.NotNull(row.QuerySelector("button[name='requestChanges']"));
    }

    [Fact]
    public async Task RequestingChangesInlineNeedsANoteAndLeavesTheRowWithOnlyAnEditorLinkBecauseItNowHasAConcern()
    {
        host.Client.DefaultRequestHeaders.Add("Cookie", host.CreateCookie("auth0|friend"));
        await SeedVoteDraftAsync();
        var page = await host.GetDocumentAsync("/Review/Votes/Triage");

        using var withoutNote = await host.Client.PostAsync("/Review/Votes/Triage", TriageDecision(page, "requestChanges"));
        using var withNote = await host.Client.PostAsync("/Review/Votes/Triage", TriageDecision(page, "requestChanges", "Check the question."));

        Assert.Equal(HttpStatusCode.BadRequest, withoutNote.StatusCode);
        Assert.Contains("Requesting changes needs a note", await withoutNote.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Redirect, withNote.StatusCode);
        var after = await host.GetDocumentAsync("/Review/Votes/Triage?decided=changes-requested");
        var row = Assert.Single(after.QuerySelectorAll("[aria-labelledby='drafts'] tbody tr"));
        Assert.Contains("Changes are requested", after.QuerySelector(".notice")!.TextContent, StringComparison.Ordinal);
        Assert.Empty(row.QuerySelectorAll("button, textarea, input"));
        Assert.NotNull(row.QuerySelector("a.btn"));
    }

    [Fact]
    public async Task ABulkSafeRowGetsACheckboxAndTheBulkPostApprovesIt()
    {
        host.Client.DefaultRequestHeaders.Add("Cookie", host.CreateCookie("auth0|friend"));
        var recordId = await SeedVoteDraftAsync(VoteKind.Rule);
        var page = await host.GetDocumentAsync("/Review/Votes/Triage");
        var fields = Fields(page.QuerySelector("#approve-bulk")!);
        var target = page.QuerySelector("input[name='targets']")!.GetAttribute("value")!;
        Assert.Equal($"{recordId}:1:0", target);

        using var approved = await host.Client.PostAsync("/Review/Votes/Triage", new FormUrlEncodedContent([.. fields, new("targets", target)]));

        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        Assert.Contains("Approved", await approved.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(1L, await CountAsync("SELECT count(*) FROM vote_review_decisions"));
    }

    [Fact]
    public async Task TheTriagePageRefusesAnEmptyBulkPostAndTheListLinksTheTriagePage()
    {
        host.Client.DefaultRequestHeaders.Add("Cookie", host.CreateCookie("auth0|friend"));
        var triage = await host.GetDocumentAsync("/Review/Votes/Triage");
        var list = await host.GetDocumentAsync("/Review/Votes");

        using var empty = await host.Client.PostAsync("/Review/Votes/Triage", Form(triage.QuerySelector("#approve-bulk")!));

        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.Equal("/Review/Votes/Triage", list.QuerySelector(".page-header-actions a")!.GetAttribute("href"));
        Assert.Equal("No votes need action", triage.QuerySelector("[aria-labelledby='drafts'] .empty h3")!.TextContent);
    }

    private async Task<long> CountAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(host.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private async Task<string> SeedVoteDraftAsync(VoteKind kind = VoteKind.Passage, string? citedExtractionId = null)
    {
        const string question = "Question: On Passage";
        const string member = "Member: A000001, A, R, AL, Yea";
        var extraction = await host.SaveExtractionAsync($"{question}\n{member}\n");
        var configuration = CollectionConfigurationRevision.Create(HouseVoteFixtures.Configuration());
        var cited = citedExtractionId ?? extraction.ExtractionId;
        var recordId = new string('a', 64);
        var candidateId = new string('b', 64);
        var revision = new VoteDraftRevision(recordId, 1, "auth0|editor", DateTimeOffset.UtcNow, "The House passed a measure",
            "The House voted on passage.", ReviewTextOrigin.Person, null, HouseVoteFixtures.SourceId, 119, 1, 100, null, null, candidateId,
            HouseVoteFixtures.Updated, [new(question, new SourceSpan(cited, 0, question.Length))],
            new DateOnly(2025, 4, 10), new SourceSpan(cited, 0, question.Length), kind,
            new SourceSpan(cited, 0, question.Length),
            [new("with-id-a", "A000001", "Yea", new SourceSpan(cited, question.Length + 1, member.Length))], [], new string('e', 64));
        await using var connection = new NpgsqlConnection(host.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"""
            SET session_replication_role = replica;
            INSERT INTO collection_configuration_revisions (revision_id, json, created_at) VALUES ('{configuration.Id}', @configuration, 1);
            INSERT INTO house_vote_candidates (candidate_id, source_id, congress, session_number, roll_call_number, update_date_utc_ticks,
                detail_job_id, members_job_id, detail_extraction_id, members_extraction_id, configuration_revision_id, created_at_utc_ticks)
            VALUES ('{candidateId}', '{HouseVoteFixtures.SourceId}', 119, 1, 100, 1, 'detail', 'members', '{extraction.ExtractionId}',
                '{extraction.ExtractionId}', '{configuration.Id}', 1);
            INSERT INTO vote_drafts (record_id, source_id, congress, session_number, roll_call_number, current_revision_number,
                review_state_version, review_status, created_at_utc_ticks)
            VALUES ('{recordId}', '{HouseVoteFixtures.SourceId}', 119, 1, 100, 1, 0, 'AwaitingReview', 1);
            INSERT INTO vote_revisions (record_id, revision_number, author_subject, author_kind, run_id, revision_json, created_at_utc_ticks)
            VALUES ('{recordId}', 1, 'auth0|editor', 'Person', NULL, @revision, 1);
            """, connection);
        command.Parameters.AddWithValue("configuration", configuration.Json);
        command.Parameters.AddWithValue("revision", JsonSerializer.Serialize(revision, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        await command.ExecuteNonQueryAsync();
        return recordId;
    }
}
