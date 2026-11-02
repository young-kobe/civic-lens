using CivicLens.Infrastructure.Collection;
using CivicLens.Tests.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.Votes;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class VoteRevisionColumnsMigrationTests(PostgresCollection postgres) : IAsyncLifetime
{
    private const string Migration = "20261102170325_VoteRevisionColumns";
    private static readonly string RecordId = new('a', 64);
    private static readonly string[] NewTables = ["vote_member_list_entries", "vote_member_lists", "vote_revision_facts", "vote_revision_issues"];
    private const string NewColumnCount = """
        SELECT count(*) FROM information_schema.columns
        WHERE table_schema = current_schema() AND table_name = 'vote_revisions' AND column_name IN ('headline', 'member_list_id', 'issue_candidates_recorded')
        """;

    private string connectionString = null!;

    public async Task InitializeAsync()
    {
        connectionString = await postgres.CreateEmptyDatabaseAsync();
        await MigrateToAsync(PreviousMigration());
    }

    public Task DisposeAsync() => postgres.DropDatabaseAsync(connectionString);

    // Rows written before the migration have no columns, and readers still need their JSON. The migration must neither fill nor change them.
    [Fact]
    public async Task LegacyRowsKeepTheirJsonAndGetNullColumnsBecauseTheBackfillIsALaterStep()
    {
        await InsertLegacyRowsAsync();

        await MigrateToAsync(Migration);

        Assert.Equal(["1|{\"a\":1}", "2|{\"b\":2}"], await ReadAsync("SELECT revision_number || '|' || revision_json FROM vote_revisions ORDER BY revision_number"));
        Assert.Equal(["0"], await ReadAsync("SELECT num_nonnulls(headline, summary, text_origin, source_id, chamber, member_list_id, kind, issue_candidates_recorded) FROM vote_revisions WHERE revision_number = 1"));
        Assert.Equal(["1"], await ReadAsync("SELECT count(*) FROM vote_drafts"));
    }

    // A rollback drops the columns and the four tables. The JSON is the source of truth today, so it must survive both ways.
    [Fact]
    public async Task TheMigrationGoesDownAndUpAgainWithLegacyRowsAndTheJsonSurvivesBecauseARollbackMustLoseNoRevision()
    {
        await InsertLegacyRowsAsync();
        await MigrateToAsync(Migration);

        await MigrateToAsync(PreviousMigration());

        Assert.Equal(["1|{\"a\":1}", "2|{\"b\":2}"], await ReadAsync("SELECT revision_number || '|' || revision_json FROM vote_revisions ORDER BY revision_number"));
        Assert.Empty(await ReadAsync("SELECT table_name FROM information_schema.tables WHERE table_schema = current_schema() AND table_name = ANY (@names)", NewTables));
        Assert.Equal(["0"], await ReadAsync(NewColumnCount));
        Assert.Empty(await ReadAsync("SELECT conname FROM pg_constraint WHERE conname = 'ux_vote_drafts_record_roll'"));

        await MigrateToAsync(Migration);

        Assert.Equal(["1|{\"a\":1}", "2|{\"b\":2}"], await ReadAsync("SELECT revision_number || '|' || revision_json FROM vote_revisions ORDER BY revision_number"));
        Assert.Equal(["3"], await ReadAsync(NewColumnCount));
    }

    private Task InsertLegacyRowsAsync() => ExecuteAsync($$"""
        SET session_replication_role = replica;
        INSERT INTO vote_drafts (record_id, source_id, chamber, congress, session_number, roll_call_number, candidate_id,
            current_revision_number, review_state_version, review_status, created_at_utc_ticks)
        VALUES ('{{RecordId}}', 'house-clerk', 'House', 119, 1, 100, 'c1', 2, 0, 'AwaitingReview', 100);
        INSERT INTO vote_revisions (record_id, revision_number, author_subject, author_kind, run_id, revision_json, created_at_utc_ticks)
        VALUES ('{{RecordId}}', 1, 'analysis', 'Analysis', 'run1', '{"a":1}', 100),
               ('{{RecordId}}', 2, 'auth0|editor', 'Person', NULL, '{"b":2}', 200)
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

    private async Task<List<string>> ReadAsync(string sql, string[]? names = null)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        if (names is not null) command.Parameters.AddWithValue("names", names);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<string>();
        while (await reader.ReadAsync()) rows.Add(reader.GetValue(0).ToString()!);
        return rows;
    }
}
