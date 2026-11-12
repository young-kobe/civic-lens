using System.Text.Json;
using CivicLens.Application.Publication;
using CivicLens.Core.Documents;
using CivicLens.Core.Relationships;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Publication;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.Fixtures;
using CivicLens.Tests.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.Publication;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class PostgresPublishedEntriesTests(PostgresCollection postgres) : IAsyncLifetime
{
    private static readonly string A = new('a', 64);
    private static readonly string B = new('b', 64);
    private static readonly string C = new('c', 64);
    private static readonly DateTimeOffset First = new(2026, 3, 1, 8, 0, 0, TimeSpan.Zero);
    private string connectionString = null!;
    private PostgresPublicationStore publications = null!;
    private int commitCount;

    public async Task InitializeAsync()
    {
        connectionString = await postgres.CreateMigratedDatabaseAsync();
        publications = new PostgresPublicationStore(new PooledDbContextFactory<CollectionAttemptDbContext>(
            new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options), TestCodeVersion.Current);
    }

    public Task DisposeAsync() => postgres.DropDatabaseAsync(connectionString);

    [Fact]
    public async Task ACommitWritesTheEntryAndKeepsTheManifestOrderOfEveryArrayBecauseTheReleaseFileMustBeRebuiltByteForByte()
    {
        await SeedActionAsync(A);

        await CommitAsync(1, null, PublishedEntryFixtures.Action(A, First));

        Assert.Equal(["action|" + A + "|1|A bill about housing|www.congress.gov|2026-03-01|To fund housing"], await ReadAsync($"""
            SELECT kind || '|' || record_id || '|' || revision_number || '|' || headline || '|' || source_host || '|' || event_date || '|' || bill_title
            FROM published_entries WHERE action_record_id = '{A}' AND action_revision_number = 1
            """));
        Assert.Equal(["S000002", "S000001"], await ReadAsync("SELECT official_id FROM published_entry_officials ORDER BY position"));
        Assert.Equal(["tax", "housing"], await ReadAsync("SELECT issue_id FROM published_entry_issues ORDER BY position"));
        Assert.Equal(["economics"], await ReadAsync("SELECT policy_area_id FROM published_entry_policy_areas ORDER BY position"));
    }

    [Fact]
    public async Task AnActionWithNoIssuesWritesNoIssueRows()
    {
        await SeedActionAsync(A);

        await CommitAsync(1, null, PublishedEntryFixtures.Action(A, First) with { IssueIds = [] });

        Assert.Empty(await ReadAsync("SELECT issue_id FROM published_entry_issues"));
        Assert.Single(await ReadAsync("SELECT kind FROM published_entries"));
    }

    [Fact]
    public async Task ARollbackAndAPublishOfTheSameRevisionAgainKeepTheOneRowBecauseTheEntryIsInsertOnly()
    {
        await SeedActionAsync(A, B);
        await CommitAsync(1, null, PublishedEntryFixtures.Action(B, First));
        await CommitAsync(2, 1, PublishedEntryFixtures.Action(A, First));
        await publications.ActivateAsync(1, (_, _) => Task.CompletedTask, default);

        await CommitAsync(3, 1, PublishedEntryFixtures.Action(A, First));

        Assert.Equal(["2"], await ReadAsync("SELECT count(*) FROM published_entries"));
        Assert.Equal(["4"], await ReadAsync("SELECT count(*) FROM published_entry_officials"));
    }

    [Fact]
    public async Task ACommitWritesTheBillLineOfAnActionAndNoLineForAnActionWithNoneBecauseTheTableOwnsTheLine()
    {
        await SeedActionAsync(A, B);

        await CommitAsync(1, null, PublishedEntryFixtures.Action(A, First) with { BillLine = "This bill funds rent aid." },
            PublishedEntryFixtures.Action(B, First));

        Assert.Equal(["a|This bill funds rent aid.", "b|none"], await ReadAsync(
            "SELECT left(record_id, 1) || '|' || coalesce(bill_line, 'none') FROM published_entries ORDER BY record_id"));
    }

    [Fact]
    public async Task APublishOfTheSameActionRevisionWithAnotherBillLineConflictsAndWritesNoReleaseBecauseTheRowHoldsTheLine()
    {
        await SeedActionAsync(A);
        await CommitAsync(1, null, PublishedEntryFixtures.Action(A, First) with { BillLine = "This bill funds rent aid." });
        await publications.ActivateAsync(1, (_, _) => Task.CompletedTask, default);

        await Assert.ThrowsAsync<PublicationConflictException>(() =>
            CommitAsync(2, 1, PublishedEntryFixtures.Action(A, First) with { BillLine = "This bill funds school meals." }));

        Assert.Equal(["1"], await ReadAsync("SELECT release_number FROM publication_releases ORDER BY 1"));
    }

    [Fact]
    public async Task APublishOfAnOlderActionRevisionAgainWithABillLineSucceedsAndFillsTheRowBecauseTheRowHoldsNoLine()
    {
        await SeedActionAsync(A);
        await CommitAsync(1, null, PublishedEntryFixtures.Action(A, First));

        await CommitAsync(2, 1, PublishedEntryFixtures.Action(A, First) with { BillLine = "This bill funds rent aid." });

        Assert.Equal(["1", "2"], await ReadAsync("SELECT release_number FROM publication_releases ORDER BY 1"));
        Assert.Equal(["This bill funds rent aid."], await ReadAsync("SELECT bill_line FROM published_entries"));
    }

    [Fact]
    public async Task AReturnedActionWhoseRowHoldsNoLineIsPublishedAgainAndFillsTheRowBecauseAWithdrawalKeepsTheRow()
    {
        await SeedActionAsync(A);
        await CommitAsync(1, null, PublishedEntryFixtures.Action(A, First));
        await ExecuteAsync($"UPDATE action_drafts SET review_status = 'ApprovalWithdrawn' WHERE record_id = '{A}'");
        await publications.CommitAsync("auth0|owner", new PublicationCommit(2, 1, $"000002-{Guid.NewGuid():N}", [], [])
        {
            Withdrawals = [],
            WithdrawnRecords = [new PublishedRecordReference(PublishedRecordKind.Action, A)],
            Names = ReleaseNameFixtures.None
        }, "key-withdraw", new string('1', 64), default);
        await ExecuteAsync($"UPDATE action_drafts SET review_status = 'Approved' WHERE record_id = '{A}'");

        await CommitAsync(3, 2, PublishedEntryFixtures.Action(A, First) with { BillLine = "This bill funds rent aid." });

        Assert.Equal(["1", "2", "3"], await ReadAsync("SELECT release_number FROM publication_releases ORDER BY 1"));
        Assert.Equal(["This bill funds rent aid."], await ReadAsync("SELECT bill_line FROM published_entries"));
    }

    [Fact]
    public async Task ARewrittenCarriedActionFillsAnEmptyRowInTheCommitBecauseTheRewriteGivesActionsTheirLine()
    {
        await SeedActionAsync(A);
        await CommitAsync(1, null, PublishedEntryFixtures.Action(A, First));

        await CommitRewrittenAsync(2, 1, PublishedEntryFixtures.Action(A, First) with { BillLine = "This bill funds rent aid." });

        Assert.Equal(["This bill funds rent aid."], await ReadAsync("SELECT bill_line FROM published_entries"));
    }

    [Fact]
    public async Task ARewrittenCarriedActionWithAnotherLineThanTheRowConflictsAndWritesNoReleaseBecauseTheLineIsWrittenOnce()
    {
        await SeedActionAsync(A);
        await CommitAsync(1, null, PublishedEntryFixtures.Action(A, First) with { BillLine = "This bill funds rent aid." });

        await Assert.ThrowsAsync<PublicationConflictException>(() =>
            CommitRewrittenAsync(2, 1, PublishedEntryFixtures.Action(A, First) with { BillLine = "This bill funds school meals." }));

        Assert.Equal(["1"], await ReadAsync("SELECT release_number FROM publication_releases ORDER BY 1"));
    }

    [Fact]
    public async Task ARewrittenCarriedActionWithNoRowConflictsAndWritesNoReleaseBecauseTheRewriteNeedsAStoredRowForEveryCarriedAction()
    {
        await SeedActionAsync(A, B);
        await CommitAsync(1, null, PublishedEntryFixtures.Action(B, First));

        await Assert.ThrowsAsync<PublicationConflictException>(() =>
            CommitRewrittenAsync(2, 1, PublishedEntryFixtures.Action(A, First) with { BillLine = "This bill funds rent aid." }));

        Assert.Equal(["1"], await ReadAsync("SELECT release_number FROM publication_releases ORDER BY 1"));
    }

    [Fact]
    public async Task TheFillWritesTheBillLineOfARowThatHoldsNoneAndLeavesTheOtherFactsAsTheyAre()
    {
        await SeedActionAsync(A);
        await CommitAsync(1, null, PublishedEntryFixtures.Action(A, First));

        var filled = await publications.FillBillLinesAsync([PublishedEntryFixtures.Action(A, First) with { BillLine = "This bill funds rent aid." }], default);

        Assert.Equal(1, filled);
        Assert.Equal(["This bill funds rent aid.|A bill about housing"], await ReadAsync("SELECT bill_line || '|' || headline FROM published_entries"));
    }

    [Fact]
    public async Task TheFillNeverChangesABillLineThatARowHoldsAndRefusesAnotherLineBecauseTheLineIsWrittenOnce()
    {
        await SeedActionAsync(A);
        await CommitAsync(1, null, PublishedEntryFixtures.Action(A, First) with { BillLine = "This bill funds rent aid." });

        Assert.Equal(0, await publications.FillBillLinesAsync([PublishedEntryFixtures.Action(A, First) with { BillLine = "This bill funds rent aid." }], default));
        await Assert.ThrowsAsync<PublicationConflictException>(() =>
            publications.FillBillLinesAsync([PublishedEntryFixtures.Action(A, First) with { BillLine = "This bill funds school meals." }], default));

        Assert.Equal(["This bill funds rent aid."], await ReadAsync("SELECT bill_line FROM published_entries"));
    }

    [Theory]
    [InlineData("headline")]
    [InlineData("officials")]
    [InlineData("issues")]
    [InlineData("matchedOfficial")]
    [InlineData("eventDate")]
    public async Task APublishOfTheSameRevisionWithADifferentEntryConflictsAndWritesNoReleaseBecauseOneRevisionHasOneEntry(string change)
    {
        await SeedActionAsync(B);
        await SeedPublishableRelationshipAsync();
        await CommitAsync(1, null, PublishedEntryFixtures.Action(B, First));
        await CommitAsync(2, 1, PublishedEntryFixtures.Relationship(A, B, C, First), StatementAndAction);
        await publications.ActivateAsync(1, (_, _) => Task.CompletedTask, default);
        var entry = PublishedEntryFixtures.Relationship(A, B, C, First);
        var changed = change switch
        {
            "headline" => entry with { Headline = "Another headline" },
            "officials" => entry with { Officials = [entry.Officials[1], entry.Officials[0], entry.Officials[2]] },
            "issues" => entry with { IssueIds = ["tax"] },
            "matchedOfficial" => entry with { MatchedOfficialId = "S000003" },
            _ => entry with { EventDate = new DateOnly(2026, 3, 2) }
        };

        await Assert.ThrowsAsync<PublicationConflictException>(() => CommitAsync(3, 1, changed, StatementAndAction));

        Assert.Equal(["1", "2"], await ReadAsync("SELECT release_number FROM publication_releases ORDER BY 1"));
    }

    [Fact]
    public async Task APublishOfTheSameRevisionWithADifferentOfficialNameIsAcceptedBecauseTheTableHoldsNoNames()
    {
        await SeedActionAsync(A, B);
        await CommitAsync(1, null, PublishedEntryFixtures.Action(B, First));
        await CommitAsync(2, 1, PublishedEntryFixtures.Action(A, First));
        await publications.ActivateAsync(1, (_, _) => Task.CompletedTask, default);
        var entry = PublishedEntryFixtures.Action(A, First);

        await CommitAsync(3, 1, entry with { Officials = [.. entry.Officials.Select(official => official with { Name = "Renamed" })] });

        Assert.Equal(["1", "2", "3"], await ReadAsync("SELECT release_number FROM publication_releases ORDER BY 1"));
    }

    [Fact]
    public async Task APublishOfTheSameVoteRevisionWithOtherCountsAndAlignmentIsAcceptedBecauseThoseAreNotEntryFacts()
    {
        await SeedVoteAsync(A);
        await SeedVoteAsync(B, 101);
        await CommitAsync(1, null, PublishedEntryFixtures.Vote(B, First));
        await CommitAsync(2, 1, PublishedEntryFixtures.Vote(A, First));
        await publications.ActivateAsync(1, (_, _) => Task.CompletedTask, default);

        await CommitAsync(3, 1, PublishedEntryFixtures.Vote(A, First) with { YeaCount = 9, NayCount = 8, Alignment = PublishedVoteAlignment.Split });

        Assert.Equal(["1", "2", "3"], await ReadAsync("SELECT release_number FROM publication_releases ORDER BY 1"));
    }

    [Fact]
    public async Task ARelationshipEntryWritesOneMatchedFlagForEachOfficialBecauseOnlyTheSecondOfThreeMatched()
    {
        await SeedPublishableRelationshipAsync();

        await CommitAsync(1, null, PublishedEntryFixtures.Relationship(A, B, C, First), StatementAndAction);

        Assert.Equal(["S000001|false", "S000002|true", "S000003|false"],
            await ReadAsync($"SELECT official_id || '|' || is_matched FROM published_entry_officials WHERE kind = 'relationship' AND record_id = '{A}' AND revision_number = 1 ORDER BY position"));
        Assert.Equal(["relationship|DescribesOwnBill|" + B + "|action|" + C], await ReadAsync(
            $"SELECT kind || '|' || link_type || '|' || statement_record_id || '|' || target_kind || '|' || target_record_id FROM published_entries WHERE kind = 'relationship' AND record_id = '{A}' AND revision_number = 1"));
    }

    [Fact]
    public async Task ARelationshipWhoseTargetHasNoFirstPublicationTimeIsRefusedWithTheBackfillMessageAndNoReleaseIsWritten()
    {
        await SeedPublishableRelationshipAsync(withTargetTime: false);

        var failure = await Assert.ThrowsAsync<FirstPublishedMissingException>(() =>
            CommitAsync(1, null, PublishedEntryFixtures.Relationship(A, B, C, First), StatementAndAction));

        Assert.Contains("releases first-published backfill", failure.Message);
        Assert.Empty(await ReadAsync("SELECT release_number FROM publication_releases"));
    }

    [Fact]
    public async Task AVoteEntryAndAnExecutiveActionEntryWriteTheirKindAndTheirEnumColumns()
    {
        await SeedVoteAsync(A);
        await SeedExecutiveActionAsync(B);

        await CommitAsync(1, null, PublishedEntryFixtures.Vote(A, First), PublishedEntryFixtures.ExecutiveAction(B, First));

        Assert.Equal(["executiveAction|ExecutiveOrder|" + B + "|" + B, "vote||" + A + "|" + A], await ReadAsync("""
            SELECT kind || '|' || COALESCE(document_type, '') || '|' || COALESCE(executive_action_record_id, vote_record_id) || '|' || record_id
            FROM published_entries ORDER BY kind
            """));
    }

    [Fact]
    public async Task ACommitWritesTheApprovalTimeOfEachKindAndAnEntryWithNoTimeWritesNoneBecauseTheViewsReadTheStoredTime()
    {
        await SeedActionAsync(A);
        await SeedVoteAsync(B);
        await SeedExecutiveActionAsync(C);
        var approved = First.AddHours(5);

        await CommitAsync(1, null, [PublishedEntryFixtures.Action(A, First) with { ApprovedAtUtc = approved },
            PublishedEntryFixtures.Vote(B, First) with { ApprovedAtUtc = approved.AddMinutes(1) },
            PublishedEntryFixtures.ExecutiveAction(C, First)], []);

        Assert.Equal([$"action|{approved.UtcTicks}", $"executiveAction|none", $"vote|{approved.AddMinutes(1).UtcTicks}"],
            await ReadAsync("SELECT kind || '|' || coalesce(approved_at_utc_ticks::text, 'none') FROM published_entries ORDER BY kind"));
    }

    [Fact]
    public async Task APublishOfTheSameRevisionWithAnotherApprovalTimeConflictsAndWritesNoReleaseBecauseTheStoredTimeIsNeverReplaced()
    {
        await SeedActionAsync(A);
        var entry = PublishedEntryFixtures.Action(A, First) with { ApprovedAtUtc = First.AddHours(5) };
        await CommitAsync(1, null, entry);

        await Assert.ThrowsAsync<PublicationConflictException>(() => CommitAsync(2, 1, entry with { ApprovedAtUtc = First.AddHours(6) }));

        Assert.Equal([$"{First.AddHours(5).UtcTicks}"], await ReadAsync("SELECT approved_at_utc_ticks FROM published_entries"));
        Assert.Equal(["1"], await ReadAsync("SELECT count(*) FROM publication_releases"));
    }

    [Fact]
    public async Task ARevisionWithNoRevisionRowIsRefusedByTheKeyAndTheReleaseIsNotWritten()
    {
        await ExecuteAsync(ActionDraftSql(A));

        var failure = await Assert.ThrowsAnyAsync<Exception>(() => CommitAsync(1, null, PublishedEntryFixtures.Action(A, First)));

        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, FindPostgres(failure).SqlState);
        Assert.Empty(await ReadAsync("SELECT release_number FROM publication_releases"));
    }

    [Fact]
    public async Task AnActionRowWithABillLineOfTheLongestLengthIsAccepted()
    {
        await ExecuteAsync(WithoutKeyChecks(InsertEntry("bill_line", "'" + new string('x', 200) + "'")));

        Assert.Equal(["200"], await ReadAsync("SELECT length(bill_line) FROM published_entries"));
    }

    [Fact]
    public async Task ABaseRowWithoutAnyOverrideIsAccepted()
    {
        await ExecuteAsync(WithoutKeyChecks(InsertEntry()));

        Assert.Single(await ReadAsync("SELECT kind FROM published_entries"));
    }

    [Theory]
    [MemberData(nameof(RefusedRows))]
    public async Task TheTableRefusesARowThatBreaksOneRule(string constraint, string[] overrides)
    {
        var failure = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(WithoutKeyChecks(InsertEntry(overrides))));

        Assert.Equal((PostgresErrorCodes.CheckViolation, constraint), (failure.SqlState, failure.ConstraintName));
    }

    public static TheoryData<string, string[]> RefusedRows => new()
    {
        { "ck_published_entries_kind", ["kind", "'draft'", "bill_title", "NULL"] },
        { "ck_published_entries_record_id", ["record_id", "'" + new string('a', 32) + "'", "action_record_id", "'" + new string('a', 32) + "'"] },
        { "ck_published_entries_revision_number", ["revision_number", "0", "action_revision_number", "0"] },
        { "ck_published_entries_headline", ["headline", "'  '"] },
        { "ck_published_entries_source_host", ["source_host", "'  '"] },
        { "ck_published_entries_bill_title", ["bill_title", "NULL"] },
        { "ck_published_entries_bill_title", [.. AsVote, "bill_title", "'A title'"] },
        { "ck_published_entries_bill_line", ["bill_line", "'  '"] },
        { "ck_published_entries_bill_line", ["bill_line", "'" + new string('x', 201) + "'"] },
        { "ck_published_entries_bill_line", [.. AsVote, "bill_title", "NULL", "bill_line", "'A line'"] },
        { "ck_published_entries_document_type", ["document_type", "'Notice'"] },
        { "ck_published_entries_document_type", [.. AsExecutiveAction, "document_type", "NULL"] },
        { "ck_published_entries_document_type", [.. AsExecutiveAction, "document_type", "'Order'"] },
        { "ck_published_entries_link", ["link_type", "'DescribesOwnBill'"] },
        { "ck_published_entries_link", [.. AsRelationship, "link_type", "'StatementAboutVote'"] },
        { "ck_published_entries_link", [.. AsRelationship, "target_kind", "'brief'"] },
        { "ck_published_entries_link", [.. AsRelationship, "statement_record_id", "'xyz'"] },
        { "ck_published_entries_revision_key", ["vote_record_id", "'" + new string('a', 64) + "'", "vote_revision_number", "1"] },
        { "ck_published_entries_revision_key", ["action_record_id", "NULL", "action_revision_number", "NULL"] },
        { "ck_published_entries_revision_key", ["action_record_id", "'" + new string('b', 64) + "'"] },
        { "ck_published_entries_revision_key", ["kind", "'vote'", "bill_title", "NULL"] }
    };

    private static readonly string[] AsVote =
        ["kind", "'vote'", "action_record_id", "NULL", "action_revision_number", "NULL", "vote_record_id", "'" + new string('a', 64) + "'", "vote_revision_number", "1"];

    private static readonly string[] AsExecutiveAction =
        ["kind", "'executiveAction'", "bill_title", "NULL", "action_record_id", "NULL", "action_revision_number", "NULL",
            "executive_action_record_id", "'" + new string('a', 64) + "'", "executive_action_revision_number", "1"];

    private static readonly string[] AsRelationship =
    [
        "kind", "'relationship'", "bill_title", "NULL", "action_record_id", "NULL", "action_revision_number", "NULL",
        "relationship_record_id", "'" + new string('a', 64) + "'", "relationship_revision_number", "1", "link_type", "'DescribesOwnBill'",
        "statement_record_id", "'" + new string('b', 64) + "'", "target_kind", "'action'", "target_record_id", "'" + new string('c', 64) + "'"
    ];

    [Theory]
    [InlineData("published_entry_issues", "issue_id", "'tax'", "position", "-1", "kind", "'action'", "ck_published_entry_issues_position")]
    [InlineData("published_entry_policy_areas", "policy_area_id", "' '", "position", "0", "kind", "'action'", "ck_published_entry_policy_areas_policy_area_id")]
    public async Task TheChildTablesRefuseARowThatBreaksOneRule(string table, string valueColumn, string value, string positionColumn, string position,
        string kindColumn, string kind, string constraint)
    {
        var failure = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync($"""
            SET session_replication_role = replica;
            INSERT INTO {table} ({kindColumn}, record_id, revision_number, {positionColumn}, {valueColumn}) VALUES ({kind}, '{A}', 1, {position}, {value})
            """));

        Assert.Equal((PostgresErrorCodes.CheckViolation, constraint), (failure.SqlState, failure.ConstraintName));
    }

    [Fact]
    public async Task TwoChildRowsWithTheSameValueInOneEntryAreRefusedBecauseTheArraysHoldDistinctValues()
    {
        await ExecuteAsync(WithoutKeyChecks(InsertEntry()));
        await ExecuteAsync(WithoutKeyChecks($"INSERT INTO published_entry_issues (kind, record_id, revision_number, position, issue_id) VALUES ('action', '{A}', 1, 0, 'tax')"));

        var failure = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(WithoutKeyChecks(
            $"INSERT INTO published_entry_issues (kind, record_id, revision_number, position, issue_id) VALUES ('action', '{A}', 1, 1, 'tax')")));

        Assert.Equal(PostgresErrorCodes.UniqueViolation, failure.SqlState);
    }

    [Fact]
    public async Task ASecondMatchedOfficialInOneEntryIsRefusedBecauseARelationshipMatchesOneOfficial()
    {
        await SeedRelationshipAsync();
        await ExecuteAsync(InsertEntry(AsRelationship.Concat(["relationship_record_id", $"'{A}'"]).ToArray()));
        await ExecuteAsync(InsertOfficial("S000001", 0, "true"));

        var failure = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(InsertOfficial("S000002", 1, "true")));

        Assert.Equal((PostgresErrorCodes.UniqueViolation, "ux_published_entry_officials_matched"), (failure.SqlState, failure.ConstraintName));
    }

    [Fact]
    public async Task AMatchedOfficialOfAnEntryThatIsNotARelationshipIsRefused()
    {
        await ExecuteAsync(WithoutKeyChecks(InsertEntry()));

        var failure = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(WithoutKeyChecks(InsertOfficial("S000001", 0, "true", "action"))));

        Assert.Equal((PostgresErrorCodes.CheckViolation, "ck_published_entry_officials_matched_kind"), (failure.SqlState, failure.ConstraintName));
    }

    [Fact]
    public async Task OneMatchedOfficialAndUnmatchedOthersAreAccepted()
    {
        await SeedRelationshipAsync();
        await ExecuteAsync(InsertEntry(AsRelationship.Concat(["relationship_record_id", $"'{A}'"]).ToArray()));
        await ExecuteAsync(InsertOfficial("S000001", 0, "false"));
        await ExecuteAsync(InsertOfficial("S000002", 1, "true"));
        await ExecuteAsync(InsertOfficial("S000003", 2, "false"));

        Assert.Equal(["S000002"], await ReadAsync("SELECT official_id FROM published_entry_officials WHERE is_matched"));
    }

    private static string InsertOfficial(string id, int position, string matched, string kind = "relationship") => $"""
        INSERT INTO published_entry_officials (kind, record_id, revision_number, position, official_id, is_matched)
        VALUES ('{kind}', '{A}', 1, {position}, '{id}', {matched})
        """;

    [Fact]
    public async Task ARelationshipEntryThatNamesARecordNobodyPublishedIsRefusedByTheRecordKey()
    {
        await SeedRelationshipAsync(withTarget: false);

        var failure = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(InsertEntry(AsRelationship)));

        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, failure.SqlState);
    }

    private async Task SeedRelationshipAsync(bool withTarget = true)
    {
        await ExecuteAsync($$"""
            SET session_replication_role = replica;
            INSERT INTO relationship_revisions (record_id, revision_number, author_subject, author_kind, revision_json, created_at_utc_ticks)
            VALUES ('{{A}}', 1, 'owner', 'Person', '{}', 0);
            INSERT INTO published_records (kind, record_id, first_published_at_utc_ticks) VALUES ('relationship', '{{A}}', 1), ('statement', '{{B}}', 1)
            """);
        if (withTarget) await ExecuteAsync($"INSERT INTO published_records (kind, record_id, first_published_at_utc_ticks) VALUES ('action', '{C}', 1)");
    }

    private static string WithoutKeyChecks(string sql) => "SET session_replication_role = replica;\n" + sql;

    private static string InsertEntry(params string[] overrides)
    {
        var row = new Dictionary<string, string>
        {
            ["kind"] = "'action'",
            ["record_id"] = $"'{A}'",
            ["revision_number"] = "1",
            ["action_record_id"] = $"'{A}'",
            ["action_revision_number"] = "1",
            ["headline"] = "'H'",
            ["bill_title"] = "'T'"
        };
        for (var index = 0; index < overrides.Length; index += 2) row[overrides[index]] = overrides[index + 1];
        return $"""
            INSERT INTO published_entries ({string.Join(", ", row.Keys)}) VALUES ({string.Join(", ", row.Values)})
            """;
    }

    private static readonly PublishedRevisionBinding[] StatementAndAction =
        [new(PublishedRecordKind.Statement, B, 1), new(PublishedRecordKind.Action, C, 1)];

    private Task CommitAsync(int number, int? baseNumber, PublishedRecordEntry entry, params PublishedRevisionBinding[] cited) =>
        CommitAsync(number, baseNumber, [entry], cited);

    private Task CommitAsync(int number, int? baseNumber, PublishedRecordEntry first, PublishedRecordEntry second) =>
        CommitAsync(number, baseNumber, [first, second], []);

    private async Task CommitAsync(int number, int? baseNumber, PublishedRecordEntry[] entries, PublishedRevisionBinding[] cited)
    {
        var records = entries.Select(PublishedRevisionBinding.Of).Concat(cited).ToArray();
        var histories = await publications.PrepareCorrectionHistoriesAsync(
            [.. records.Select(record => new PublicationCorrectionHistory(record, []))], default);
        await publications.CommitAsync("auth0|owner", new PublicationCommit(number, baseNumber, $"{number:D6}-{Guid.NewGuid():N}",
            [.. records],
            [.. entries.Select(entry => new ReviewStateExpectation(entry.Kind, entry.RecordId, 1, 1))])
        {
            Withdrawals = [],
            AddedEntries = [.. entries],
            CorrectionHistories = [.. histories],
            Names = ReleaseNameFixtures.None
        },
            $"key-{++commitCount}", Convert.ToHexStringLower(BitConverter.GetBytes(commitCount)).PadLeft(64, '0'), default);
    }

    private async Task CommitRewrittenAsync(int number, int baseNumber, PublishedRecordEntry entry)
    {
        var record = PublishedRevisionBinding.Of(entry);
        var histories = await publications.PrepareCorrectionHistoriesAsync(
            [new PublicationCorrectionHistory(record, [])], default);
        await publications.CommitAsync("auth0|owner", new PublicationCommit(number, baseNumber, $"{number:D6}-{Guid.NewGuid():N}",
            [record], [])
        {
            Withdrawals = [],
            RewrittenEntries = [entry],
            CorrectionHistories = [.. histories],
            Names = ReleaseNameFixtures.None
        }, $"key-{++commitCount}", Convert.ToHexStringLower(BitConverter.GetBytes(commitCount)).PadLeft(64, '0'), default);
    }

    private async Task SeedActionAsync(params string[] ids)
    {
        foreach (var (id, index) in ids.Select((id, index) => (id, index)))
        {
            await ActionRevisionSeeder.AddAsync(connectionString, ActionRevisionFixtures.Model() with { RecordId = id });
            await ExecuteAsync(ActionDraftSql(id, index + 1));
        }
    }

    private async Task SeedVoteAsync(string id, int rollCall = 100) => await ExecuteAsync($$"""
        SET session_replication_role = replica;
        INSERT INTO vote_drafts (record_id, source_id, chamber, congress, session_number, roll_call_number, current_revision_number, review_state_version,
            review_status, created_at_utc_ticks, last_revised_at_utc_ticks)
        VALUES ('{{id}}', 'congress-house-votes', 'House', 119, 1, {{rollCall}}, 1, 1, 'Approved', 0, 0);
        INSERT INTO vote_revisions (record_id, revision_number, author_subject, author_kind, run_id, created_at_utc_ticks, {{VoteRevisionColumnsSeed.RequiredColumns}})
        VALUES ('{{id}}', 1, 'owner', 'Person', NULL, 0, {{VoteRevisionColumnsSeed.PlaceholderValues}})
        """);

    private async Task SeedExecutiveActionAsync(string id) => await ExecuteAsync($$"""
        SET session_replication_role = replica;
        INSERT INTO executive_action_drafts (record_id, current_revision_number, review_state_version, review_status, created_at_utc_ticks, last_revised_at_utc_ticks)
        VALUES ('{{id}}', 1, 1, 'Approved', 0, 0);
        INSERT INTO executive_action_revisions (record_id, revision_number, revision_json, created_at_utc_ticks, author_kind, author_subject)
        VALUES ('{{id}}', 1, '{}', 0, 'Code', 'civic-lens:code')
        """);

    private async Task SeedPublishableRelationshipAsync(bool withTargetTime = true)
    {
        var span = new SourceSpan(A, 0, 10);
        var revision = new RelationshipRevision(A, 1, "owner", First, new(RelationshipRecordKind.Statement, B, 1), new(RelationshipRecordKind.Action, C, 1),
            new(RelationshipMatchBasis.BillNumber, span), RelationshipType.DescribesOwnBill, "The statement describes the bill.", span, "F2", "S000002");
        var json = JsonSerializer.Serialize(revision, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        await ExecuteAsync($$"""
            SET session_replication_role = replica;
            INSERT INTO relationship_drafts (record_id, current_revision_number, review_state_version, review_status, no_longer_matches, created_at_utc_ticks,
                last_revised_at_utc_ticks)
            VALUES ('{{A}}', 1, 1, 'Approved', false, 0, 0);
            INSERT INTO relationship_revisions (record_id, revision_number, author_subject, author_kind, revision_json, created_at_utc_ticks,
                statement_candidate_id, statement_revision_number, target_kind, target_action_record_id, target_action_revision_number, match_basis,
                match_extraction_id, match_start, match_length, type, reason, statement_quote_extraction_id, statement_quote_start, statement_quote_length,
                target_fact_id, official_id)
            VALUES ('{{A}}', 1, 'owner', 'Person', '{{json}}', 0, '{{B}}', 1, 'Action', '{{C}}', 1, 'BillNumber', '{{A}}', 0, 10, 'DescribesOwnBill',
                '{{revision.Reason}}', '{{A}}', 0, 10, 'F2', 'S000002');
            INSERT INTO statement_review_decisions (decision_id, candidate_id, revision_number, review_state_version, kind, actor_subject, created_at_utc_ticks)
            VALUES ('{{Guid.NewGuid():N}}', '{{B}}', 1, 1, 'Approve', 'owner', 1);
            INSERT INTO action_review_decisions (decision_id, record_id, revision_number, review_state_version, kind, actor_subject, created_at_utc_ticks)
            VALUES ('{{Guid.NewGuid():N}}', '{{C}}', 1, 1, 'Approve', 'owner', 1);
            INSERT INTO statement_revisions (candidate_id, revision_number, revision_json, created_at_utc_ticks, author_kind, author_subject)
            VALUES ('{{B}}', 1, '{}', 0, 'Person', 'owner');
            INSERT INTO published_records (kind, record_id, first_published_at_utc_ticks) VALUES ('statement', '{{B}}', 1)
            """);
        if (withTargetTime)
        {
            await ActionRevisionSeeder.AddAsync(connectionString, ActionRevisionFixtures.Model() with { RecordId = C });
            await ExecuteAsync($"INSERT INTO published_records (kind, record_id, first_published_at_utc_ticks) VALUES ('action', '{C}', 1)");
        }
        var seedEntries = new List<PublishedRecordEntry>
        {
            new()
            {
                Kind = PublishedRecordKind.Statement,
                RecordId = B,
                RevisionNumber = 1,
                Headline = "A statement about housing",
                FirstPublishedAtUtc = new DateTimeOffset(1, TimeSpan.Zero),
                Officials = [],
                IssueIds = [],
                PolicyAreaIds = [],
                ApprovedBy = PublishedApprover.Person
            }
        };
        if (withTargetTime) seedEntries.Add(PublishedEntryFixtures.Action(C, new DateTimeOffset(1, TimeSpan.Zero)));
        await publications.AddEntriesAsync(seedEntries, default);
    }

    private static string ActionDraftSql(string id, int number = 1) => $"""
        SET session_replication_role = replica;
        INSERT INTO action_drafts (record_id, source_id, congress, bill_type, bill_number, current_revision_number, created_at_utc_ticks,
            last_revised_at_utc_ticks, review_state_version, review_status)
        VALUES ('{id}', 'congress-bills', 119, 'hr', {number}, 1, 0, 0, 1, 'Approved')
        """;

    private static PostgresException FindPostgres(Exception exception) =>
        exception as PostgresException ?? (PostgresException)exception.InnerException!;

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<List<string>> ReadAsync(string sql) =>
        [.. (await PublishedRecordStateReader.ReadAsync(connectionString, sql)).Select(row => row[0])];
}
