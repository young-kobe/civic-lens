using CivicLens.Infrastructure.Collection;
using CivicLens.Tests.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.Votes;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class VoteQuestionResultColumnsMigrationTests(PostgresCollection postgres) : IAsyncLifetime
{
    private const string Migration = "20261105005544_VoteQuestionResultColumns";
    private static readonly string RecordId = new('a', 64);
    private const string ColumnCount = "SELECT count(*) FROM information_schema.columns WHERE table_name = 'vote_revisions' AND column_name IN ('question', 'result')";

    private string connectionString = null!;

    public async Task InitializeAsync()
    {
        connectionString = await postgres.CreateEmptyDatabaseAsync();
        await MigrateToAsync(PreviousMigration());
    }

    public Task DisposeAsync() => postgres.DropDatabaseAsync(connectionString);

    // The old public view matched the prefixes "Question: " and "Result: " and took the first fact by position. The fill must give the same values.
    [Fact]
    public async Task TheFillGivesWhatTheOldPrefixMatchGaveBecauseThePublicValuesMustNotChange()
    {
        await InsertDraftAsync();
        await InsertRevisionAsync(1, "Started: 2025-04-10", "Vote title: Question: not the question", "Question: First", "Question: Second", "Result: Passed");
        await InsertRevisionAsync(2, "Result: Failed", "Count: Yea 1");
        await InsertRevisionAsync(3, "Result: Agreed to: by voice", "Question: On the Motion");

        await MigrateToAsync(Migration);

        Assert.Equal(["1|First|Passed", "2|none|Failed", "3|On the Motion|Agreed to: by voice"],
            await ReadAsync("SELECT revision_number || '|' || coalesce(question, 'none') || '|' || result FROM vote_revisions ORDER BY revision_number"));
    }

    [Fact]
    public async Task TheFactRowsStayAndTheResultColumnBecomesRequiredBecauseNoRowMayLackAResult()
    {
        await InsertDraftAsync();
        await InsertRevisionAsync(1, "Question: On Passage", "Result: Passed");

        await MigrateToAsync(Migration);

        Assert.Equal(["2"], await ReadAsync("SELECT count(*) FROM vote_revision_facts"));
        Assert.Equal(["NO", "YES"], await ReadAsync("SELECT is_nullable FROM information_schema.columns WHERE table_name = 'vote_revisions' AND column_name IN ('result', 'question') ORDER BY column_name DESC"));
        Assert.Equal([null], await ReadAsync("SELECT column_default FROM information_schema.columns WHERE table_name = 'vote_revisions' AND column_name = 'result'"));
        var failure = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync("UPDATE vote_revisions SET result = NULL"));
        Assert.Equal(PostgresErrorCodes.NotNullViolation, failure.SqlState);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task ARevisionWithoutExactlyOneResultFactStopsTheMigrationAndNamesItBecauseTheFillWouldGuess(int resultFacts)
    {
        await InsertDraftAsync();
        await InsertRevisionAsync(1, "Question: On Passage", "Result: Passed");
        await InsertRevisionAsync(2, [.. Enumerable.Repeat("Result: Passed", resultFacts), "Question: On Passage"]);

        var failure = await Assert.ThrowsAsync<PostgresException>(() => MigrateToAsync(Migration));

        Assert.Contains("Cannot fill result", failure.MessageText);
        Assert.Contains($"{RecordId}/2", failure.MessageText);
        Assert.Equal(["0"], await ReadAsync(ColumnCount));
        await using var db = NewContext();
        Assert.Contains(Migration, await db.Database.GetPendingMigrationsAsync());
    }

    [Fact]
    public async Task TheRollbackDropsTheColumnsAndKeepsTheFactsAndTheNextRunFillsAgainBecauseARollbackMustLoseNothing()
    {
        await InsertDraftAsync();
        await InsertRevisionAsync(1, "Question: On Passage", "Result: Passed");
        await MigrateToAsync(Migration);

        await MigrateToAsync(PreviousMigration());

        Assert.Equal(["0"], await ReadAsync(ColumnCount));
        Assert.Equal(["2"], await ReadAsync("SELECT count(*) FROM vote_revision_facts"));

        await MigrateToAsync(Migration);

        Assert.Equal(["On Passage|Passed"], await ReadAsync("SELECT question || '|' || result FROM vote_revisions"));
    }

    private Task InsertDraftAsync() => ExecuteAsync($"""
        SET session_replication_role = replica;
        INSERT INTO vote_drafts (record_id, source_id, chamber, congress, session_number, roll_call_number, candidate_id,
            current_revision_number, review_state_version, review_status, created_at_utc_ticks)
        VALUES ('{RecordId}', 'house-clerk', 'House', 119, 1, 100, 'c1', 3, 0, 'AwaitingReview', 100)
        """);

    // The fact rows go in last position first, so only the position column can give the order.
    private Task InsertRevisionAsync(int number, params string[] facts) => ExecuteAsync($"""
        SET session_replication_role = replica;
        INSERT INTO vote_revisions (record_id, revision_number, author_subject, author_kind, run_id, revision_json, created_at_utc_ticks,
            headline, summary, text_origin, source_id, vote_source, chamber, congress, session_number, roll_call_number, candidate_id,
            candidate_update_utc_ticks, voted_on, voted_on_citation_extraction_id, voted_on_citation_start, voted_on_citation_length, kind,
            member_list_id, issue_revision_id, issue_candidates_recorded)
        VALUES ('{RecordId}', {number}, 'auth0|editor', 'Person', NULL, 'x', 100,
            'Headline', 'Summary', 'Person', 'house-clerk', 'CongressGov', 'House', 119, 1, 100, 'c1',
            1, DATE '2026-01-02', 'e1', 0, 5, 'Passage',
            '{new string('1', 64)}', '{new string('2', 64)}', true);
        {string.Join('\n', facts.Select((text, position) => $"""
            INSERT INTO vote_revision_facts (record_id, revision_number, position, fact_text, citation_extraction_id, citation_start, citation_length)
            VALUES ('{RecordId}', {number}, {position}, '{text.Replace("'", "''")}', 'e1', 0, 1);
            """).Reverse())}
        """);

    private string PreviousMigration()
    {
        using var db = NewContext();
        return db.Database.GetMigrations().TakeWhile(id => id != Migration).Last();
    }

    private CollectionAttemptDbContext NewContext() =>
        new(new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options);

    private async Task MigrateToAsync(string target)
    {
        await using var db = NewContext();
        await db.GetService<IMigrator>().MigrateAsync(target);
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<List<string?>> ReadAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<string?>();
        while (await reader.ReadAsync()) rows.Add(reader.IsDBNull(0) ? null : reader.GetValue(0).ToString());
        return rows;
    }
}
