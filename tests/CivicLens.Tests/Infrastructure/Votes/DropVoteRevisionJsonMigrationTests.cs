using CivicLens.Infrastructure.Collection;
using CivicLens.Tests.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.Votes;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class DropVoteRevisionJsonMigrationTests(PostgresCollection postgres) : IAsyncLifetime
{
    private const string Migration = "20261104213058_DropVoteRevisionJson";
    private static readonly string RecordId = new('a', 64);
    private const string RevisionColumnCount = "SELECT count(*) FROM information_schema.columns WHERE table_name = 'vote_revisions' AND column_name = 'revision_json'";

    private string connectionString = null!;

    public async Task InitializeAsync()
    {
        connectionString = await postgres.CreateEmptyDatabaseAsync();
        await MigrateToAsync(PreviousMigration());
    }

    public Task DisposeAsync() => postgres.DropDatabaseAsync(connectionString);

    [Fact]
    public async Task RevisionsWithColumnsKeepEveryColumnAndLoseTheJsonBecauseTheColumnsAreTheOnlyCopy()
    {
        await InsertDraftAsync(RecordId);
        await InsertRevisionWithColumnsAsync(RecordId, 1);
        await InsertRevisionWithColumnsAsync(RecordId, 2);

        await MigrateToAsync(Migration);

        Assert.Equal(["1|Headline|Senate|7", "2|Headline|Senate|7"],
            await ReadAsync("SELECT revision_number || '|' || headline || '|' || chamber || '|' || roll_call_number FROM vote_revisions ORDER BY revision_number"));
        Assert.Equal(["0"], await ReadAsync(RevisionColumnCount));
    }

    [Fact]
    public async Task TheCoreColumnsBecomeRequiredAndTheAllOrNoneCheckGoesBecauseNoRowWithoutColumnsCanRemain()
    {
        await InsertDraftAsync(RecordId);
        await InsertRevisionWithColumnsAsync(RecordId, 1);
        Assert.Equal(["19"], await ReadAsync(NullableCoreColumns));
        Assert.Equal(["1"], await ReadAsync(ColumnsWrittenCheckCount));

        await MigrateToAsync(Migration);

        Assert.Equal(["0"], await ReadAsync(NullableCoreColumns));
        Assert.Equal(["0"], await ReadAsync(ColumnsWrittenCheckCount));
        var failure = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync("UPDATE vote_revisions SET headline = NULL"));
        Assert.Equal(PostgresErrorCodes.NotNullViolation, failure.SqlState);
    }

    // The JSON of a row without columns is the only copy of that revision, so the drop must refuse it and leave the column in place.
    [Fact]
    public async Task ARevisionWithoutColumnsStopsTheMigrationAndNamesTheRevisionBecauseTheDropWouldLoseIt()
    {
        await InsertDraftAsync(RecordId);
        await InsertRevisionWithColumnsAsync(RecordId, 1);
        await ExecuteAsync($$"""
            SET session_replication_role = replica;
            INSERT INTO vote_revisions (record_id, revision_number, author_subject, author_kind, run_id, revision_json, created_at_utc_ticks)
            VALUES ('{{RecordId}}', 2, 'auth0|editor', 'Person', NULL, '{"a":1}', 100)
            """);

        var failure = await Assert.ThrowsAsync<PostgresException>(() => MigrateToAsync(Migration));

        Assert.Contains("Cannot drop revision_json", failure.MessageText);
        Assert.Contains($"{RecordId}/2", failure.MessageText);
        Assert.Equal(["1"], await ReadAsync(RevisionColumnCount));
        Assert.Equal(["{\"a\":1}"], await ReadAsync("SELECT revision_json FROM vote_revisions WHERE headline IS NULL"));
        await using var db = NewContext();
        Assert.Contains(Migration, await db.Database.GetPendingMigrationsAsync());
    }

    [Fact]
    public async Task TheRollbackAddsAnEmptyNullableJsonColumnAndRestoresTheNullableColumnsAndTheCheckAndTheNextRunKeepsEveryColumnBecauseARollbackMustLoseNothing()
    {
        await InsertDraftAsync(RecordId);
        await InsertRevisionWithColumnsAsync(RecordId, 1);
        await MigrateToAsync(Migration);

        await MigrateToAsync(PreviousMigration());

        Assert.Equal(["1"], await ReadAsync(RevisionColumnCount));
        Assert.Equal(["YES"], await ReadAsync("SELECT is_nullable FROM information_schema.columns WHERE table_name = 'vote_revisions' AND column_name = 'revision_json'"));
        Assert.Equal(["1|Headline"], await ReadAsync("SELECT revision_number || '|' || headline FROM vote_revisions"));
        Assert.Equal(["19"], await ReadAsync(NullableCoreColumns));
        Assert.Equal(["1"], await ReadAsync(ColumnsWrittenCheckCount));

        await MigrateToAsync(Migration);

        Assert.Equal(["0"], await ReadAsync(RevisionColumnCount));
        Assert.Equal(["1|Headline"], await ReadAsync("SELECT revision_number || '|' || headline FROM vote_revisions"));
    }

    private const string NullableCoreColumns = """
        SELECT count(*) FROM information_schema.columns WHERE table_name = 'vote_revisions' AND is_nullable = 'YES'
          AND column_name IN ('headline', 'summary', 'text_origin', 'source_id', 'vote_source', 'chamber', 'congress', 'session_number', 'roll_call_number',
              'candidate_id', 'candidate_update_utc_ticks', 'voted_on', 'voted_on_citation_extraction_id', 'voted_on_citation_start',
              'voted_on_citation_length', 'kind', 'member_list_id', 'issue_revision_id', 'issue_candidates_recorded')
        """;

    private const string ColumnsWrittenCheckCount = "SELECT count(*) FROM pg_constraint WHERE conname = 'ck_vote_revisions_columns_written'";

    private Task InsertDraftAsync(string recordId) => ExecuteAsync($"""
        SET session_replication_role = replica;
        INSERT INTO vote_drafts (record_id, source_id, chamber, congress, session_number, roll_call_number, candidate_id,
            current_revision_number, review_state_version, review_status, created_at_utc_ticks)
        VALUES ('{recordId}', 'voteview-senate', 'Senate', 119, 1, 7, 'c1', 2, 0, 'AwaitingReview', 100)
        """);

    private Task InsertRevisionWithColumnsAsync(string recordId, int number) => ExecuteAsync($"""
        SET session_replication_role = replica;
        INSERT INTO vote_revisions (record_id, revision_number, author_subject, author_kind, run_id, revision_json, created_at_utc_ticks,
            headline, summary, text_origin, source_id, vote_source, chamber, congress, session_number, roll_call_number, candidate_id,
            candidate_update_utc_ticks, voted_on, voted_on_citation_extraction_id, voted_on_citation_start, voted_on_citation_length, kind,
            member_list_id, issue_revision_id, issue_candidates_recorded)
        VALUES ('{recordId}', {number}, 'auth0|editor', 'Person', NULL, 'x', 100,
            'Headline', 'Summary', 'Person', 'voteview-senate', 'Voteview', 'Senate', 119, 1, 7, 'c1',
            1, DATE '2026-01-02', 'e1', 0, 5, 'Passage',
            '{new string('1', 64)}', '{new string('2', 64)}', true)
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

    private async Task<List<string>> ReadAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<string>();
        while (await reader.ReadAsync()) rows.Add(reader.GetValue(0).ToString()!);
        return rows;
    }
}
