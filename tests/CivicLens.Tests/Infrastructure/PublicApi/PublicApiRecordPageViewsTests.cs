using CivicLens.Application.Actions;
using CivicLens.Infrastructure.Actions;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.Fixtures;
using CivicLens.Tests.Infrastructure.Collection;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.PublicApi;

/// <summary>The views for the bill page, the related records panel, and the vote and executive action bodies. Each test seeds base tables and reads the views as the public role.</summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class PublicApiRecordPageViewsTests(PostgresCollection postgres) : IAsyncLifetime
{
    private const string Role = "civic_lens_public";
    private const string LoginPassword = "test-only-password";
    private static readonly string Action1 = new('a', 64);
    private static readonly string Action2 = new('b', 64);
    private static readonly string Action3 = new('c', 64);
    private static readonly string Action4 = new('d', 64);
    private static readonly string Vote1 = new('1', 64);
    private static readonly string Statement1 = new('5', 64);
    private static readonly string Statement2 = new('6', 64);
    private static readonly string Link1 = new('7', 64);
    private static readonly string Link2 = new('8', 64);

    private string connectionString = null!;

    public async Task InitializeAsync()
    {
        connectionString = await postgres.CreateMigratedDatabaseAsync();
        await ExecuteAsync($"ALTER ROLE {Role} LOGIN PASSWORD '{LoginPassword}'");
    }

    // The role belongs to the cluster, so each test puts it back to the state that the migration leaves.
    public async Task DisposeAsync()
    {
        await ExecuteAsync($"ALTER ROLE {Role} NOLOGIN PASSWORD NULL");
        await postgres.DropDatabaseAsync(connectionString);
    }

    [Fact]
    public async Task BillStatusEqualsTheStatusRowThatTheReleaseBuilderReadsBecauseOneRuleMustGiveBothAnswers()
    {
        await SeedBillAsync(Action1, 101);
        await SeedBillAsync(Action2, 102);
        await SeedBillAsync(Action3, 103);
        await SeedBillAsync(Action4, 104, notYetActive: true);
        await ExecuteAsync($"""
            SET session_replication_role = replica;
            {PublicRecordPageRows.Gate(101, 1, 100, "Referred to the Committee on Ways and Means.", "2026-01-12", 500)}
            {PublicRecordPageRows.Gate(101, 2, 200, "Received in the Senate.", "2026-02-13", 600)}
            {PublicRecordPageRows.Gate(102, 1, 100, "Motion to reconsider laid on the table.", "2026-01-14", 700)}
            {PublicRecordPageRows.Gate(102, 2, 200, null, null, 800)}
            {PublicRecordPageRows.Gate(104, 1, 100, "Became Public Law No: 119-21.", "2026-03-01", 900)}
            """);
        string[] ids = [Action1, Action2, Action3, Action4];

        var fromBuilder = await PostgresActionReviewStore.FromConnectionString(connectionString).ReadBillStatusesAsync([.. ids], default);
        var expected = fromBuilder.Select(item => $"{item.Status.RecordId[0]}|{item.Status.Text}|{item.Status.Date:yyyy-MM-dd}|{item.Status.ObservedAtUtc.UtcTicks}")
            .Where(row => !row.StartsWith('d')).Order(StringComparer.Ordinal).ToArray();
        var fromView = await PublicAsync("""
            SELECT left(record_id, 1) || '|' || latest_action_text || '|' || latest_action_date || '|' || read_at_utc_ticks
              FROM public_api.bill_status ORDER BY 1
            """);

        Assert.Equal(["a|Received in the Senate.|2026-02-13|600"], expected.Where(row => row.StartsWith('a')));
        Assert.Equal(expected, fromView);
    }

    [Fact]
    public async Task BillStatusHidesTheBillsThatTheBillsViewHidesBecauseAChildViewNeverShowsWhatItsParentHides()
    {
        await SeedBillAsync(Action1, 101);
        await SeedBillAsync(Action2, 102, sponsorships: false);
        await SeedBillAsync(Action3, 103, eventOn: null, cosponsors: null);
        await ExecuteAsync($"""
            SET session_replication_role = replica;
            {PublicRecordPageRows.Gate(101, 1, 100, "Received in the Senate.", "2026-02-13", 600)}
            {PublicRecordPageRows.Gate(102, 1, 100, "Received in the Senate.", "2026-02-13", 600)}
            {PublicRecordPageRows.Gate(103, 1, 100, "Received in the Senate.", "2026-02-13", 600)}
            """);

        Assert.Equal(["a"], await PublicAsync("SELECT left(record_id, 1) FROM public_api.bills ORDER BY 1"));
        Assert.Equal(["a"], await PublicAsync("SELECT left(record_id, 1) FROM public_api.bill_status ORDER BY 1"));
    }

    [Fact]
    public async Task EachActiveRelationshipGivesOneRowToThePageOfTheStatementAndOneToThePageOfTheTargetWithTheOtherEndAsTheCSharpRuleNamesIt()
    {
        await SeedBillAsync(Action1, 101);
        await SeedAsync(PublicRecordPageRows.Link(Link1, Statement1, "Doe on rent", Action1, "action", "Housing bill", "DescribesOwnBill", "2026-05-01"));
        await SeedAsync(PublicRecordPageRows.Link(Link2, Statement1, "Doe on rent", Vote1, "vote", "Vote on housing", "StatementAboutVote", null));

        var rows = await PublicAsync("""
            SELECT kind || '|' || left(record_id, 1) || '|' || related_kind || '|' || left(related_record_id, 1) || '|' || related_headline || '|' || link_type || '|' || coalesce(statement_date::text, 'none') || '|' || left(link_record_id, 1)
              FROM public_api.record_links ORDER BY 1
            """);

        Assert.Equal(
        [
            "action|a|statement|5|Doe on rent|DescribesOwnBill|2026-05-01|7",
            "statement|5|action|a|Housing bill|DescribesOwnBill|2026-05-01|7",
            "statement|5|vote|1|Vote on housing|StatementAboutVote|none|8",
            "vote|1|statement|5|Doe on rent|StatementAboutVote|none|8"
        ], rows);
        var link = Relationship(Link1, Statement1, PublishedRecordKind.Action, Action1);
        Assert.Equal(PublishedRecordKind.Statement, link.Other(PublishedRecordKind.Action, Action1).Kind);
        Assert.Equal(PublishedRecordKind.Action, link.Other(PublishedRecordKind.Statement, Statement1).Kind);
    }

    [Fact]
    public async Task ARelationshipThatIsNotActiveOrWhoseOtherEndIsNotActiveShowsNoRowBecauseAWithdrawnRecordNeverLinks()
    {
        await SeedAsync(PublicRecordPageRows.Link(Link1, Statement1, "Doe on rent", Action1, "action", "Housing bill", "DescribesOwnBill", "2026-05-01", linkFirst: 2));
        await SeedAsync(PublicRecordPageRows.Link(Link2, Statement2, "Roe on rent", Action2, "action", "Other bill", "StatesPositionOn", "2026-05-02", targetFirst: 2));

        Assert.Empty(await PublicAsync("SELECT record_id FROM public_api.record_links"));
    }

    [Fact]
    public async Task VoteFactsAreTheFactRowsOfTheActiveRevisionInPositionOrderAndNoViewShowsACitation()
    {
        await ExecuteAsync($"""
            SET session_replication_role = replica;
            INSERT INTO publication_releases (release_number, directory_name, actor_subject, published_at_utc_ticks, record_count, is_active)
              VALUES (1, '000001-00000000000000000000000000000001', 'test', 1, 1, true);
            INSERT INTO publication_record_states (kind, record_id, first_release, revision_number, last_release) VALUES ('vote', '{Vote1}', 1, 2, NULL);
            INSERT INTO vote_member_lists (list_id, member_count, created_at_utc_ticks) VALUES ('{Vote1}', 1, 1);
            INSERT INTO vote_revisions (record_id, revision_number, author_subject, author_kind, created_at_utc_ticks, headline, summary, text_origin, source_id, vote_source,
                chamber, congress, session_number, roll_call_number, candidate_id, candidate_update_utc_ticks, voted_on, voted_on_citation_extraction_id,
                voted_on_citation_start, voted_on_citation_length, kind, member_list_id, issue_revision_id, issue_candidates_recorded, result)
              VALUES {string.Join(", ", new[] { 1, 2 }.Select(revision => $"('{Vote1}', {revision}, 'test', 'Person', 1, 'H', 'S', 'Person', 'src', 'CongressGov', 'House', 119, 1, 1, 'c', 1, '2026-02-01', 'ext', 0, 1, 'Passage', '{Vote1}', '{new string('a', 64)}', true, 'Passed')"))};
            INSERT INTO vote_revision_facts (record_id, revision_number, "position", fact_text, citation_extraction_id, citation_start, citation_length) VALUES
              ('{Vote1}', 1, 0, 'Old fact', 'ext', 0, 1),
              ('{Vote1}', 2, 1, 'Result: Passed', 'ext', 0, 1),
              ('{Vote1}', 2, 0, 'Question: On passage', 'ext', 0, 1);
            """);

        Assert.Equal(["0|Question: On passage", "1|Result: Passed"], await PublicAsync(
            "SELECT \"position\" || '|' || fact_text FROM public_api.vote_facts WHERE record_id = '" + Vote1 + "' ORDER BY \"position\""));
    }

    [Fact]
    public async Task AnExecutiveActionViewShowsTheFactsOfTheRevisionAndItsNoticesInPositionOrderAndNoBioguideOrCitation()
    {
        await SeedAsync(PublicRecordPageRows.ExecutiveAction(Action1));

        Assert.Equal(["2|Title two|ExecutiveOrder|2026-00001|2026-01-20|2026-01-23|91 FR 1|https://fr.example/1|president|Jane Roe"], await PublicAsync("""
            SELECT revision_number || '|' || headline || '|' || kind || '|' || document_number || '|' || signed_on || '|' || published_on || '|' || federal_register_citation
                   || '|' || document_url || '|' || official_id || '|' || president_name
              FROM public_api.executive_actions
            """));
        Assert.Equal(["0|2026-00009|2026-02-01|https://fr.example/9", "1|2026-00005|2026-02-03|https://fr.example/5"], await PublicAsync("""
            SELECT "position" || '|' || document_number || '|' || published_on || '|' || notice_url FROM public_api.executive_action_notices ORDER BY "position"
            """));
    }

    private Task SeedBillAsync(string record, int billNumber, bool notYetActive = false, bool sponsorships = true, string? eventOn = "2026-02-20", int? cosponsors = 1) => ExecuteAsync($"""
        SET session_replication_role = replica;
        INSERT INTO publication_releases (release_number, directory_name, actor_subject, published_at_utc_ticks, record_count, is_active)
          VALUES (1, '000001-00000000000000000000000000000001', 'test', 1, 1, true) ON CONFLICT DO NOTHING;
        INSERT INTO publication_record_states (kind, record_id, first_release, revision_number, last_release)
          VALUES ('action', '{record}', {(notYetActive ? 2 : 1)}, 1, NULL);
        {PublicActionRows.ActionRevision(record, 1, "Person", eventOn, cosponsors, billNumber: billNumber)}
        {(sponsorships ? PublicActionRows.ActionSponsorship(record, 1, 0, "p1", "Sponsor", true, "2026-01-10") : "")}
        {PublicRecordPageRows.ActionDraft(record, billNumber)}
        """);

    private static PublishedRelationship Relationship(string link, string statement, PublishedRecordKind targetKind, string target) => new()
    {
        Corrections = [],
        RecordId = link,
        RevisionNumber = 1,
        Headline = "Link",
        Statement = new() { Kind = PublishedRecordKind.Statement, RecordId = statement, RevisionNumber = 1, Headline = "Doe on rent" },
        Target = new() { Kind = targetKind, RecordId = target, RevisionNumber = 1, Headline = "Housing bill" },
        Type = PublishedRelationshipType.DescribesOwnBill,
        Reason = "r",
        Quote = "q",
        TargetFact = "f",
        MatchedOfficialId = "o1",
        Officials = [new() { Id = "o1", Name = "Jane Doe" }],
        IssueIds = [],
        PolicyAreaIds = [],
        ApprovedAtUtc = DateTimeOffset.UnixEpoch,
        FirstPublishedAtUtc = DateTimeOffset.UnixEpoch
    };

    private async Task<string[]> PublicAsync(string sql)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString) { Username = Role, Password = LoginPassword };
        await using var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<string>();
        while (await reader.ReadAsync()) rows.Add(Convert.ToString(reader.GetValue(0))!);
        return [.. rows];
    }

    private Task SeedAsync(string sql) => ExecuteAsync("SET session_replication_role = replica;\n" + sql);

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
