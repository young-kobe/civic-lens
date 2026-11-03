using CivicLens.Application.Publication;
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
            new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options));
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
        Assert.Equal(["S000002|Sam Second", "S000001|Sam First"], await ReadAsync("SELECT official_id || '|' || official_name FROM published_entry_officials ORDER BY position"));
        Assert.Equal(["tax", "housing"], await ReadAsync("SELECT issue_id FROM published_entry_issues ORDER BY position"));
        Assert.Equal(["economics"], await ReadAsync("SELECT policy_area_id FROM published_entry_policy_areas ORDER BY position"));
        Assert.Equal(["C000003", "C000001"], await ReadAsync("SELECT official_id FROM published_entry_cosponsors ORDER BY position"));
    }

    [Fact]
    public async Task AnActionWithNoCosponsorsAndNoIssuesWritesNoChildRows()
    {
        await SeedActionAsync(A);

        await CommitAsync(1, null, PublishedEntryFixtures.Action(A, First) with { CosponsorIds = [], IssueIds = [] });

        Assert.Empty(await ReadAsync("SELECT official_id FROM published_entry_cosponsors"));
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
        Assert.Equal(["4", "2"], await ReadAsync("SELECT count(*) FROM published_entry_officials UNION ALL SELECT count(*) FROM published_entry_cosponsors WHERE record_id = '" + A + "'"));
    }

    [Theory]
    [InlineData("headline")]
    [InlineData("officials")]
    [InlineData("issues")]
    [InlineData("cosponsorOrder")]
    [InlineData("eventDate")]
    public async Task APublishOfTheSameRevisionWithADifferentEntryConflictsAndWritesNoReleaseBecauseOneRevisionHasOneEntry(string change)
    {
        await SeedActionAsync(A, B);
        await CommitAsync(1, null, PublishedEntryFixtures.Action(B, First));
        await CommitAsync(2, 1, PublishedEntryFixtures.Action(A, First));
        await publications.ActivateAsync(1, (_, _) => Task.CompletedTask, default);
        var entry = PublishedEntryFixtures.Action(A, First);
        var changed = change switch
        {
            "headline" => entry with { Headline = "Another headline" },
            "officials" => entry with { Officials = [entry.Officials[1], entry.Officials[0]] },
            "issues" => entry with { IssueIds = ["tax"] },
            "cosponsorOrder" => entry with { CosponsorIds = ["C000001", "C000003"] },
            _ => entry with { EventDate = new DateOnly(2026, 3, 2) }
        };

        await Assert.ThrowsAsync<PublicationConflictException>(() => CommitAsync(3, 1, changed));

        Assert.Equal(["1", "2"], await ReadAsync("SELECT release_number FROM publication_releases ORDER BY 1"));
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
        { "ck_published_entries_document_type", ["document_type", "'Notice'"] },
        { "ck_published_entries_document_type", [.. AsExecutiveAction, "document_type", "NULL"] },
        { "ck_published_entries_document_type", [.. AsExecutiveAction, "document_type", "'Order'"] },
        { "ck_published_entries_link", ["link_type", "'DescribesOwnBill'"] },
        { "ck_published_entries_link", [.. AsRelationship, "link_type", "'StatementAboutVote'"] },
        { "ck_published_entries_link", [.. AsRelationship, "target_kind", "'brief'"] },
        { "ck_published_entries_link", [.. AsRelationship, "statement_record_id", "'xyz'"] },
        { "ck_published_entries_vote_facts", [.. AsVote, "bill_title", "NULL", "yea_count", "5"] },
        { "ck_published_entries_vote_facts", [.. AsVote, "bill_title", "NULL", "yea_count", "-1", "nay_count", "5"] },
        { "ck_published_entries_vote_facts", [.. AsVote, "bill_title", "NULL", "yea_count", "300", "nay_count", "301"] },
        { "ck_published_entries_vote_facts", [.. AsVote, "bill_title", "NULL", "alignment", "'Split'"] },
        { "ck_published_entries_vote_facts", [.. AsVote, "bill_title", "NULL", "yea_count", "1", "nay_count", "1", "alignment", "'Other'"] },
        { "ck_published_entries_vote_facts", ["yea_count", "1", "nay_count", "1"] },
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
    [InlineData("published_entry_cosponsors", "official_id", "'C000001'", "position", "0", "kind", "'vote'", "ck_published_entry_cosponsors_kind")]
    [InlineData("published_entry_cosponsors", "official_id", "' '", "position", "0", "kind", "'action'", "ck_published_entry_cosponsors_official_id")]
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
        INSERT INTO published_entry_officials (kind, record_id, revision_number, position, official_id, official_name, is_matched)
        VALUES ('{kind}', '{A}', 1, {position}, '{id}', 'Name', {matched})
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

    private Task CommitAsync(int number, int? baseNumber, PublishedRecordEntry entry) =>
        publications.CommitAsync("auth0|owner", new PublicationCommit(number, baseNumber, $"{number:D6}-{Guid.NewGuid():N}",
            [new PublishedRevisionBinding(PublishedRecordKind.Action, entry.RecordId, 1)],
            [new ReviewStateExpectation(PublishedRecordKind.Action, entry.RecordId, 1, 1)])
        { AddedEntries = [entry] },
            $"key-{++commitCount}", Convert.ToHexStringLower(BitConverter.GetBytes(commitCount)).PadLeft(64, '0'), default);

    private async Task SeedActionAsync(params string[] ids)
    {
        foreach (var (id, index) in ids.Select((id, index) => (id, index)))
        {
            await ExecuteAsync(PublishedEntryFixtures.InsertActionRevisionSql(id));
            await ExecuteAsync(ActionDraftSql(id, index + 1));
        }
    }

    private static string ActionDraftSql(string id, int number = 1) => $"""
        SET session_replication_role = replica;
        INSERT INTO action_drafts (record_id, source_id, congress, bill_type, bill_number, current_revision_number, created_at_utc_ticks,
            last_revised_at_utc_ticks, review_state_version, review_status)
        VALUES ('{id}', 'congress-bills', 119, 'hr', {number}, 1, 0, 0, 1, 'Approved')
        """;

    private static PostgresException FindPostgres(Exception exception) =>
        exception as PostgresException ?? (PostgresException)exception.InnerException!;

    private static async Task RunAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        await command.ExecuteNonQueryAsync();
    }

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
