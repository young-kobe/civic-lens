using System.Collections.Immutable;
using System.Reflection;
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
    private static readonly string Link3 = new('9', 64);

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
        PublishedRelationship[] links =
        [
            Relationship(Link1, Statement1, "Doe on rent", PublishedRecordKind.Action, Action1, "Housing bill", PublishedRelationshipType.DescribesOwnBill, new(2026, 5, 1)),
            Relationship(Link2, Statement1, "Doe on rent", PublishedRecordKind.Vote, Vote1, "Vote on housing", PublishedRelationshipType.StatementAboutVote, null),
            Relationship(Link3, Statement2, "Roe on rent", PublishedRecordKind.Action, Action1, "Housing bill", PublishedRelationshipType.StatesPositionOn, new(2026, 5, 3))
        ];
        await SeedAsync($"""
            {PublicRecordPageRows.Headline("statement", Statement1, "Doe on rent")}
            {PublicRecordPageRows.Headline("statement", Statement2, "Roe on rent", "Model")}
            {PublicRecordPageRows.Headline("action", Action1, "Housing bill", "Model")}
            {PublicRecordPageRows.Headline("vote", Vote1, "Vote on housing", "Code")}
            {PublicRecordPageRows.Link(Link1, Statement1, Action1, "action", "DescribesOwnBill", "2026-05-01")}
            {PublicRecordPageRows.Link(Link2, Statement1, Vote1, "vote", "StatementAboutVote", null)}
            {PublicRecordPageRows.Link(Link3, Statement2, Action1, "action", "StatesPositionOn", "2026-05-03")}
            """);
        (PublishedRecordKind Kind, string Id)[] pages =
            [(PublishedRecordKind.Statement, Statement1), (PublishedRecordKind.Statement, Statement2), (PublishedRecordKind.Action, Action1), (PublishedRecordKind.Vote, Vote1)];

        foreach (var (kind, id) in pages)
        {
            var expected = CitingLinks(links, kind, id).Select(link =>
            {
                var other = link.Other(kind, id);
                return $"{PublicationProtocol.KindText(other.Kind)}|{other.RecordId[0]}|{other.Headline}|{link.Type}|{link.StatementDate?.ToString("yyyy-MM-dd") ?? "none"}|{link.RecordId[0]}";
            }).ToArray();
            var fromView = await PublicAsync($"""
                SELECT related_kind || '|' || left(related_record_id, 1) || '|' || related_headline || '|' || link_type || '|' || coalesce(statement_date::text, 'none') || '|' || left(link_record_id, 1)
                  FROM public_api.record_links WHERE kind = '{PublicationProtocol.KindText(kind)}' AND record_id = '{id}'
                 ORDER BY statement_date NULLS LAST, link_record_id
                """);

            Assert.NotEmpty(expected);
            Assert.Equal(expected, fromView);
        }
        Assert.Equal(6, (await PublicAsync("SELECT record_id FROM public_api.record_links")).Length);
    }

    [Theory]
    [InlineData("statement", "action")]
    [InlineData("action", "statement")]
    [InlineData("vote", "statement")]
    public async Task ARelatedHeadlineOfModelTextCarriesItsOriginAndLabelBecauseAReaderMustSeeWhenAnAiWroteIt(string modelKind, string pageKind)
    {
        var model = new string('3', 64);
        var page = new string('4', 64);
        var (statement, targetKind, target) = modelKind == "statement" ? (model, pageKind, page) : (page, modelKind, model);
        await SeedAsync($"""
            {PublicRecordPageRows.Headline(modelKind, model, "Model headline", "Model")}
            {PublicRecordPageRows.Headline(pageKind, page, "Page headline")}
            {PublicRecordPageRows.Link(Link1, statement, target, targetKind, targetKind == "vote" ? "StatementAboutVote" : "DescribesOwnBill", null)}
            """);

        var rows = await PublicAsync($"""
            SELECT related_headline || '|' || related_text_origin || '|' || related_label FROM public_api.record_links WHERE kind = '{pageKind}' AND record_id = '{page}'
            """);

        Assert.Equal(["Model headline|Model|An AI summary of the official record"], rows);
    }

    [Fact]
    public async Task ARelatedHeadlineOfPersonTextHasNoLabelAndCodeTextHasTheCodeLabelBecauseNoAiWroteThem()
    {
        await SeedAsync($"""
            {PublicRecordPageRows.Headline("statement", Statement1, "Doe on rent")}
            {PublicRecordPageRows.Headline("action", Action1, "Housing bill", "Code")}
            {PublicRecordPageRows.Headline("vote", Vote1, "Vote on housing")}
            {PublicRecordPageRows.Link(Link1, Statement1, Action1, "action", "DescribesOwnBill", null)}
            {PublicRecordPageRows.Link(Link2, Statement1, Vote1, "vote", "StatementAboutVote", null)}
            """);

        Assert.Equal(
        [
            "Doe on rent|Person|none",
            "Doe on rent|Person|none",
            "Housing bill|Code|Written automatically from Congress.gov data, with no AI",
            "Vote on housing|Person|none"
        ], await PublicAsync("""
            SELECT related_headline || '|' || related_text_origin || '|' || coalesce(related_label, 'none') FROM public_api.record_links ORDER BY related_headline, related_kind
            """));
    }

    [Fact]
    public async Task ARelationshipThatIsNotActiveOrWhoseOtherEndIsNotActiveShowsNoRowBecauseAWithdrawnRecordNeverLinks()
    {
        await SeedAsync($"""
            {PublicRecordPageRows.Headline("statement", Statement1, "Doe on rent")}
            {PublicRecordPageRows.Headline("action", Action1, "Housing bill")}
            {PublicRecordPageRows.Headline("statement", Statement2, "Roe on rent")}
            {PublicRecordPageRows.Headline("action", Action2, "Other bill")}
            {PublicRecordPageRows.Link(Link1, Statement1, Action1, "action", "DescribesOwnBill", "2026-05-01", linkFirst: 2)}
            {PublicRecordPageRows.Link(Link2, Statement2, Action2, "action", "StatesPositionOn", "2026-05-02", targetFirst: 2)}
            """);

        Assert.Empty(await PublicAsync("SELECT record_id FROM public_api.record_links"));
    }

    [Fact]
    public async Task AnInactiveStatementAtEitherEndOfALinkGivesNoRowToThePageOfTheStatementOrToThePageOfTheTarget()
    {
        await SeedAsync($"""
            {PublicRecordPageRows.Headline("statement", Statement1, "Doe on rent")}
            {PublicRecordPageRows.Headline("action", Action1, "Housing bill")}
            {PublicRecordPageRows.Link(Link1, Statement1, Action1, "action", "DescribesOwnBill", "2026-05-01", statementFirst: 2)}
            """);

        Assert.Empty(await PublicAsync("SELECT record_id FROM public_api.record_links WHERE kind = 'statement'"));
        Assert.Empty(await PublicAsync("SELECT record_id FROM public_api.record_links WHERE kind = 'action'"));
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

    [Fact]
    public async Task ACorrectionNoticeWithNoUrlGivesNoRowBecauseTheRouteReadsTheUrlAsTextAndANullWouldFailTheRequest()
    {
        await SeedAsync(PublicRecordPageRows.ExecutiveAction(Action1));
        await ExecuteAsync($"""
            SET session_replication_role = replica;
            INSERT INTO executive_action_documents (source_id, document_number, record_id, revision_number, published_on, document_job_id)
              VALUES ('fr', '2026-00007', '{Action1}', 2, '2026-02-05', 'job3');
            INSERT INTO executive_action_revision_notices (record_id, revision_number, "position", source_id, document_number) VALUES ('{Action1}', 2, 2, 'fr', '2026-00007');
            """);

        Assert.Equal(["2026-00009", "2026-00005"], await PublicAsync("SELECT document_number FROM public_api.executive_action_notices ORDER BY \"position\""));
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

    private static PublishedRelationship Relationship(string link, string statement, string statementHeadline, PublishedRecordKind targetKind, string target,
        string targetHeadline, PublishedRelationshipType type, DateOnly? statementDate) => new()
        {
            Corrections = [],
            RecordId = link,
            RevisionNumber = 1,
            Headline = "Link",
            Statement = new() { Kind = PublishedRecordKind.Statement, RecordId = statement, RevisionNumber = 1, Headline = statementHeadline },
            Target = new() { Kind = targetKind, RecordId = target, RevisionNumber = 1, Headline = targetHeadline },
            Type = type,
            StatementDate = statementDate,
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

    // ReleaseLinks is internal, so the test builds it through its private constructor. The static pages call the same Citing method.
    private static PublishedRelationship[] CitingLinks(PublishedRelationship[] links, PublishedRecordKind kind, string recordId)
    {
        var type = typeof(IActionReviewStore).Assembly.GetType("CivicLens.Application.Publication.ReleaseLinks")!;
        var releaseLinks = Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.NonPublic, null,
            [links.ToImmutableArray(), ImmutableArray<PublishedRelationshipCitation>.Empty], null)!;
        var citing = (ImmutableArray<PublishedRelationship>)type.GetMethod("Citing")!.Invoke(releaseLinks, [kind, recordId])!;
        return [.. citing];
    }

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
