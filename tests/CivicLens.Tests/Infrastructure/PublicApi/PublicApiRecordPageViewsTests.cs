using System.Collections.Immutable;
using System.Reflection;
using CivicLens.Application.Actions;
using CivicLens.Core.Review;
using CivicLens.Application.Publication;
using CivicLens.Infrastructure.Collection.Migrations;
using CivicLens.PublicWeb.Publication;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.Fixtures;
using CivicLens.Tests.Infrastructure.Collection;
using CivicLens.Tests.Publication.Contracts;
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
    public async Task BillStatusShowsTheStatusThatTheActiveReleasePinnedAndNeverTheLiveGateBecausePublicServingReadsPublishedReleasesOnly()
    {
        await SeedBillAsync(Action1, 101);
        await SeedBillAsync(Action2, 102);
        await SeedBillAsync(Action3, 103);
        await SeedBillAsync(Action4, 104, notYetActive: true);
        await ExecuteAsync($"""
            SET session_replication_role = replica;
            {PublicRecordPageRows.PinnedStatus(1, Action1, "Received in the Senate.", "2026-02-13", 600)}
            {PublicRecordPageRows.PinnedStatus(2, Action1, "Became Public Law No: 119-21.", "2026-04-01", 650)}
            {PublicRecordPageRows.PinnedStatus(1, Action2, "Motion to reconsider laid on the table.", "2026-01-14", 700)}
            {PublicRecordPageRows.PinnedStatus(1, Action4, "Referred to the Committee on Ways and Means.", "2026-01-12", 900)}
            {PublicRecordPageRows.Gate(101, 2, 200, "Presented to the President.", "2026-03-20", 800)}
            {PublicRecordPageRows.Gate(103, 1, 100, "Referred to the Committee on Rules.", "2026-01-30", 810)}
            """);

        var fromView = await PublicAsync("""
            SELECT left(record_id, 1) || '|' || latest_action_text || '|' || latest_action_date || '|' || read_at_utc_ticks
              FROM public_api.bill_status ORDER BY 1
            """);

        Assert.Equal(["a|Received in the Senate.|2026-02-13|600", "b|Motion to reconsider laid on the table.|2026-01-14|700"], fromView);
    }

    [Fact]
    public async Task BillStatusHidesTheBillsThatTheBillsViewHidesBecauseAChildViewNeverShowsWhatItsParentHides()
    {
        await SeedBillAsync(Action1, 101);
        await SeedBillAsync(Action2, 102, sponsorships: false);
        await SeedBillAsync(Action3, 103, eventOn: null, cosponsors: null);
        await ExecuteAsync($"""
            SET session_replication_role = replica;
            {PublicRecordPageRows.PinnedStatus(1, Action1, "Received in the Senate.", "2026-02-13", 600)}
            {PublicRecordPageRows.PinnedStatus(1, Action2, "Received in the Senate.", "2026-02-13", 600)}
            {PublicRecordPageRows.PinnedStatus(1, Action3, "Received in the Senate.", "2026-02-13", 600)}
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
        // The citation of a link holds the headline that it had when the relationship was approved. The view shows the current headline, which differs.
        var current = new Dictionary<string, string>
        {
            [Statement1] = "Doe on rent",
            [Statement2] = "Roe on rent",
            [Action1] = "Housing bill",
            [Vote1] = "Vote on housing"
        };

        foreach (var (kind, id) in pages)
        {
            var expected = CitingLinks(links, kind, id).Select(link =>
            {
                var other = link.Other(kind, id);
                Assert.NotEqual(current[other.RecordId], other.Headline);
                return $"{PublicationProtocol.KindText(other.Kind)}|{other.RecordId[0]}|{current[other.RecordId]}|{link.Type}|{link.StatementDate?.ToString("yyyy-MM-dd") ?? "none"}|{link.RecordId[0]}";
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

    [Fact]
    public async Task EachRecordLinkShowsTheApproverOfItsRelationshipOnBothEndsAndNoneBeforeTheBackfill()
    {
        await SeedAsync($"""
            {PublicRecordPageRows.Headline("statement", Statement1, "Doe on rent")}
            {PublicRecordPageRows.Headline("action", Action1, "Housing bill")}
            {PublicRecordPageRows.Headline("vote", Vote1, "Vote on housing")}
            {PublicRecordPageRows.Link(Link1, Statement1, Action1, "action", "DescribesOwnBill", null)}
            {PublicRecordPageRows.Link(Link2, Statement1, Vote1, "vote", "StatementAboutVote", null)}
            UPDATE published_entries SET approved_by = 'Code' WHERE kind = 'relationship' AND record_id = '{Link1}';
            UPDATE published_entries SET approved_by = 'Person' WHERE kind = 'relationship' AND record_id = '{Link2}';
            """);

        Assert.Equal(["action|Code", "vote|Person"], await PublicAsync($"""
            SELECT related_kind || '|' || approver FROM public_api.record_links WHERE kind = 'statement' AND record_id = '{Statement1}' ORDER BY related_kind
            """));
        Assert.Equal(["statement|Code"], await PublicAsync($"SELECT related_kind || '|' || approver FROM public_api.record_links WHERE kind = 'action' AND record_id = '{Action1}'"));
        Assert.Equal(["statement|Person"], await PublicAsync($"SELECT related_kind || '|' || approver FROM public_api.record_links WHERE kind = 'vote' AND record_id = '{Vote1}'"));
        await ExecuteAsync($"UPDATE published_entries SET approved_by = NULL WHERE kind = 'relationship' AND record_id = '{Link1}'");
        Assert.Equal(["none"], await PublicAsync($"SELECT coalesce(approver, 'none') FROM public_api.record_links WHERE kind = 'action' AND record_id = '{Action1}'"));
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
            {(modelKind == "statement" ? PublicRecordPageRows.StatementApproval(model, 1, 1, ReviewAuthor.CodeSubject) : "")}
            {PublicRecordPageRows.Headline(pageKind, page, "Page headline")}
            {PublicRecordPageRows.Link(Link1, statement, target, targetKind, targetKind == "vote" ? "StatementAboutVote" : "DescribesOwnBill", null)}
            """);

        var rows = await PublicAsync($"""
            SELECT related_headline || '|' || related_text_origin || '|' || related_label FROM public_api.record_links WHERE kind = '{pageKind}' AND record_id = '{page}'
            """);

        var label = modelKind == "statement" ? PublishedRecordLabels.StatementModelWording : PublishedRecordLabels.ModelWording;
        Assert.Equal([$"Model headline|Model|{label}"], rows);
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
            $"Housing bill|Code|{PublishedRecordLabels.CodeWording}",
            "Vote on housing|Person|none"
        ], await PublicAsync("""
            SELECT related_headline || '|' || related_text_origin || '|' || coalesce(related_label, 'none') FROM public_api.record_links ORDER BY related_headline, related_kind
            """));
    }

    [Fact]
    public async Task RecordHeadlinesGiveTheOriginAndLabelThatTheCSharpRulesGiveForEveryCaseBecauseTheViewRepeatsThoseRules()
    {
        string[] statements = [new('1', 64), new('2', 64), new('3', 64), new('4', 64), new('5', 64)];
        string[] actions = [new('a', 64), new('b', 64), new('c', 64)];
        string[] votes = [new('d', 64), new('e', 64), new('f', 64)];
        string[] origins = ["Model", "Code", "Person"];
        await SeedAsync($"""
            {PublicRecordPageRows.ActiveRelease}
            INSERT INTO publication_record_states (kind, record_id, first_release, revision_number, last_release) VALUES
              {string.Join(", ", statements.Select(id => $"('statement', '{id}', 1, 1, NULL)").Concat(actions.Select(id => $"('action', '{id}', 1, 1, NULL)")).Concat(votes.Select(id => $"('vote', '{id}', 1, 1, NULL)")))};
            {string.Join("\n", origins.Select((origin, index) => PublicRecordPageRows.Headline("action", actions[index], "Action " + origin, origin)))}
            {string.Join("\n", origins.Select((origin, index) => PublicRecordPageRows.Headline("vote", votes[index], "Vote " + origin, origin)))}
            {PublicRecordPageRows.Headline("statement", statements[0], "Statement 0", "Model")}
            {PublicRecordPageRows.StatementApproval(statements[0], 1, 1, ReviewAuthor.CodeSubject)}
            {PublicRecordPageRows.Headline("statement", statements[1], "Statement 1", "Model", "Person")}
            {PublicRecordPageRows.StatementApproval(statements[1], 1, 1, "auth0|owner")}
            {PublicRecordPageRows.Headline("statement", statements[2], "Statement 2", "Person")}
            {PublicRecordPageRows.StatementApproval(statements[2], 1, 1, "auth0|owner")}
            {PublicRecordPageRows.Headline("statement", statements[3], "Statement 3", "Model", "Person")}
            {PublicRecordPageRows.StatementApproval(statements[3], 1, 1, ReviewAuthor.CodeSubject)}
            {PublicRecordPageRows.StatementApproval(statements[3], 1, 2, "auth0|owner")}
            {PublicRecordPageRows.Headline("statement", statements[4], "Statement 4", "Model")}
            {PublicRecordPageRows.StatementApproval(statements[4], 1, 1, "auth0|owner")}
            {PublicRecordPageRows.StatementApproval(statements[4], 1, 2, ReviewAuthor.CodeSubject)}
            """);
        // Each statement case: the origin that PublishedStatementBuilder gives, and the decisions in order of review state version.
        (PublishedTextOrigin Origin, string[] Approvers)[] cases =
        [
            (PublishedTextOrigin.Model, [ReviewAuthor.CodeSubject]), (PublishedTextOrigin.Model, ["auth0|owner"]), (PublishedTextOrigin.Person, ["auth0|owner"]),
            (PublishedTextOrigin.Model, [ReviewAuthor.CodeSubject, "auth0|owner"]), (PublishedTextOrigin.Model, ["auth0|owner", ReviewAuthor.CodeSubject])
        ];
        var expected = origins.Select(origin => $"Action {origin}|{origin}|{LabelOfRecord(Enum.Parse<PublishedTextOrigin>(origin))}")
            .Concat(origins.Select(origin => $"Vote {origin}|{origin}|{LabelOfRecord(Enum.Parse<PublishedTextOrigin>(origin))}"))
            .Concat(cases.Select((item, index) => $"Statement {index}|{item.Origin}|{LabelOfStatement(item.Origin, StatementApprover(item.Approvers))}"))
            .Order(StringComparer.Ordinal).ToArray();

        var fromView = await PublicAsync("""
            SELECT headline || '|' || text_origin || '|' || coalesce(label, 'none') FROM public_api.record_headlines ORDER BY 1
            """);

        Assert.Equal(expected, fromView);
        Assert.Contains($"Statement 0|Model|{PublishedRecordLabels.StatementModelWording}", fromView);
        Assert.Contains($"Statement 1|Model|{PublishedRecordLabels.StatementCheckedByPersonWording}", fromView);
        Assert.Contains("Statement 2|Person|none", fromView);
    }

    [Fact]
    public async Task LaterUnpublishedApprovalsAndReleaseRollbackNeverChangeThePublishedStatementLabel()
    {
        await SeedAsync($"""
            {PublicRecordPageRows.Headline("statement", Statement1, "Published statement", "Model")}
            {PublicRecordPageRows.StatementApproval(Statement1, 1, 1, ReviewAuthor.CodeSubject)}
            {PublicRecordPageRows.Link(Link1, Statement1, Action1, "action", "DescribesOwnBill", null)}
            {PublicRecordPageRows.Headline("action", Action1, "Bill")}
            """);
        const string query = "SELECT related_label FROM public_api.record_links WHERE kind = 'action'";
        var published = await PublicAsync(query);
        Assert.Equal([PublishedRecordLabels.StatementModelWording], published);

        await SeedAsync(PublicRecordPageRows.StatementApproval(Statement1, 1, 2, "auth0|owner"));
        Assert.Equal(published, await PublicAsync(query));

        await ExecuteAsync("""
            UPDATE publication_releases SET is_active = false;
            INSERT INTO publication_releases (release_number, directory_name, actor_subject, published_at_utc_ticks, record_count, is_active)
              VALUES (2, '000002-00000000000000000000000000000002', 'test', 2, 1, true);
            """);
        Assert.Equal(published, await PublicAsync(query));
        await ExecuteAsync("UPDATE publication_releases SET is_active = false; UPDATE publication_releases SET is_active = true WHERE release_number = 1");
        Assert.Equal(published, await PublicAsync(query));
    }

    [Fact]
    public async Task AStatementWhosePublishedApprovalIsUnknownIsHiddenUntilBackfillRatherThanShownWithoutItsAiLabel()
    {
        await SeedAsync($"""
            {PublicRecordPageRows.Headline("statement", Statement1, "Published statement", "Model")}
            {PublicRecordPageRows.Headline("action", Action1, "Bill")}
            {PublicRecordPageRows.Link(Link1, Statement1, Action1, "action", "DescribesOwnBill", null)}
            UPDATE published_entries SET approved_by = NULL WHERE kind = 'statement';
            """);

        Assert.Empty(await PublicAsync("SELECT headline FROM public_api.record_headlines WHERE kind = 'statement'"));
        Assert.Empty(await PublicAsync("SELECT related_headline FROM public_api.record_links WHERE kind = 'action'"));
    }

    // The C# rule for an action or a vote is PublishedRecordLabels.Wording.
    private static string LabelOfRecord(PublishedTextOrigin origin) => PublishedRecordLabels.Wording(origin).SingleOrDefault()?.Value ?? "none";

    // The C# rule for a statement is PublishedRecordLabels.StatementWording. It gives the code label when code approved a model statement, the person-checked label when a person did, and none for person text.
    private static string LabelOfStatement(PublishedTextOrigin origin, PublishedApprover approver) =>
        PublishedRecordLabels.StatementWording(PublishedStatementTests.Statement() with { TextOrigin = origin, ApprovedBy = approver }).SingleOrDefault()?.Value ?? "none";

    // PublishedRecordRules.ApproverOf reads the latest Approve decision of the revision.
    private static PublishedApprover StatementApprover(string[] approversInOrder)
    {
        var decisions = approversInOrder.Select((actor, index) => new ReviewDecision(new string('0', 32), "d", 1, index + 1, ReviewDecisionKind.Approve, actor,
            null, [], DateTimeOffset.UnixEpoch)).ToImmutableArray();
        return PublishedRecordRules.ApproverOf(1, decisions);
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
            INSERT INTO vote_member_lists (list_id, {VoteMemberListSeed.Columns}, created_at_utc_ticks)
              VALUES ('{Vote1}', {VoteMemberListSeed.Values(["Not Voting"])}, 1);
            INSERT INTO vote_member_list_entries (list_id, "position", person_id, bioguide_id, vote_cast, citation_extraction_id, citation_start, citation_length)
              VALUES ('{Vote1}', 0, 'member', 'B000001', 'Not Voting', 'ext', 0, 1);
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
    public async Task TheSummaryViewHoldsOneRowForTheActiveRevisionWithItsLabelOnlyForTheModelOriginAndItsSentencesInPositionOrder()
    {
        await SeedAsync(PublicRecordPageRows.ExecutiveAction(Action1));
        Assert.Equal(["0"], await PublicAsync("SELECT count(*)::text FROM public_api.executive_action_summaries"));
        await ExecuteAsync(SummaryRows(Action1, "Model"));

        Assert.Equal([$"2|Model|{ExecutiveSummaryWords.Label}|true"], await PublicAsync(
            "SELECT revision_number || '|' || text_origin || '|' || label || '|' || cut FROM public_api.executive_action_summaries"));
        Assert.Equal(["0|The order tells agencies to act.", "1|Each agency reports in 30 days."], await PublicAsync(
            "SELECT \"position\" || '|' || sentence FROM public_api.executive_action_summary_sentences ORDER BY \"position\""));
        Assert.Equal(["0"], await PublicAsync($"SELECT count(*)::text FROM public_api.executive_action_summaries WHERE revision_number = 1"));
    }

    [Fact]
    public async Task ASummaryOfAnotherOriginHasNoLabelAndNoSentenceInTheViewBecauseOnlyTheModelOriginCarriesTheLabel()
    {
        await SeedAsync(PublicRecordPageRows.ExecutiveAction(Action1));
        await ExecuteAsync("ALTER TABLE executive_action_revisions DROP CONSTRAINT ck_executive_action_revisions_summary_origin");
        await ExecuteAsync(SummaryRows(Action1, "Person"));

        Assert.Equal(["Person|"], await PublicAsync("SELECT text_origin || '|' || coalesce(label, '') FROM public_api.executive_action_summaries"));
        Assert.Equal(["0"], await PublicAsync("SELECT count(*)::text FROM public_api.executive_action_summary_sentences"));
    }

    [Fact]
    public async Task TheLabelTextOfTheViewEqualsTheWordsOwnerBecauseSqlCannotReadTheClass()
    {
        await SeedAsync(PublicRecordPageRows.ExecutiveAction(Action1));
        await ExecuteAsync(SummaryRows(Action1, "Model"));

        Assert.Equal([ExecutiveSummaryWords.Label], await PublicAsync("SELECT label FROM public_api.executive_action_summaries"));
    }

    [Fact]
    public async Task TheExecutiveActionStoreReadsTheSummaryOfTheActiveRevisionAndNullWhenTheRevisionHasNone()
    {
        await SeedAsync(PublicRecordPageRows.ExecutiveAction(Action1));
        var builder = new NpgsqlConnectionStringBuilder(connectionString) { Username = Role, Password = LoginPassword };
        await using var views = NpgsqlDataSource.Create(builder.ConnectionString);
        var store = new CivicLens.PublicApi.ExecutiveActions.ExecutiveActionStore(views);
        Assert.Null((await store.FindAsync(Action1, default))!.Summary);

        await ExecuteAsync(SummaryRows(Action1, "Model"));

        var summary = (await store.FindAsync(Action1, default))!.Summary!;
        Assert.Equal((PublishedTextOrigin.Model, ExecutiveSummaryWords.Label, true), (summary.TextOrigin, summary.Label, summary.Cut));
        Assert.Equal(["The order tells agencies to act.", "Each agency reports in 30 days."], summary.Sentences);
    }

    private static string SummaryRows(string record, string origin) => $"""
        SET session_replication_role = replica;
        UPDATE executive_action_revisions SET summary_text_origin = '{origin}', summary_cut = true WHERE record_id = '{record}' AND revision_number = 2;
        INSERT INTO executive_action_revision_summary_sentences (record_id, revision_number, "position", sentence, quote_extraction_id, quote_start, quote_length)
          VALUES ('{record}', 2, 1, 'Each agency reports in 30 days.', '{new string('e', 64)}', 10, 20),
                 ('{record}', 2, 0, 'The order tells agencies to act.', '{new string('e', 64)}', 0, 10);
        """;

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
        """);

    private static PublishedRelationship Relationship(string link, string statement, string statementHeadline, PublishedRecordKind targetKind, string target,
        string targetHeadline, PublishedRelationshipType type, DateOnly? statementDate) => new()
        {
            Corrections = [],
            RecordId = link,
            RevisionNumber = 1,
            Headline = "Link",
            Statement = new() { Kind = PublishedRecordKind.Statement, RecordId = statement, RevisionNumber = 1, Headline = CitedHeadline(statementHeadline) },
            Target = new() { Kind = targetKind, RecordId = target, RevisionNumber = 1, Headline = CitedHeadline(targetHeadline) },
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

    private static string CitedHeadline(string currentHeadline) => "Earlier: " + currentHeadline;

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
