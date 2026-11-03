using CivicLens.Infrastructure.Collection;
using CivicLens.Tests.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.Actions;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class ActionRevisionColumnsMigrationTests(PostgresCollection postgres) : IAsyncLifetime
{
    private static readonly string RecordId = new('a', 64);
    private static readonly string[] NewTables = ["action_revision_issues", "action_revision_sponsorship_citations", "action_revision_sponsorships"];
    private const string Migration = "20261103165049_ActionRevisionColumns";
    private const string NewColumnCount = """
        SELECT count(*) FROM information_schema.columns
        WHERE table_schema = current_schema() AND table_name = 'action_revisions' AND column_name IN ('headline', 'bill_title', 'issue_candidates_recorded')
        """;
    private const string LegacyRows = "1|{\"a\":1}";

    private string connectionString = null!;

    public async Task InitializeAsync()
    {
        connectionString = await postgres.CreateEmptyDatabaseAsync();
        await MigrateToAsync(PreviousMigration());
    }

    public Task DisposeAsync() => postgres.DropDatabaseAsync(connectionString);

    [Fact]
    public async Task LegacyRowsKeepTheirJsonAndGetNullColumnsBecauseTheBackfillIsALaterStep()
    {
        await InsertLegacyRowsAsync();

        await MigrateToAsync(Migration);

        Assert.Equal([LegacyRows, "2|{\"b\":2}"], await ReadAsync("SELECT revision_number || '|' || revision_json FROM action_revisions ORDER BY revision_number"));
        Assert.Equal(["0"], await ReadAsync("SELECT num_nonnulls(headline, summary, text_origin, source_id, bill_number, candidate_id, fingerprint, bill_title, issue_candidates_recorded) FROM action_revisions WHERE revision_number = 1"));
        Assert.Equal(["1"], await ReadAsync("SELECT count(*) FROM action_drafts"));
    }

    [Fact]
    public async Task TheMigrationGoesDownAndUpAgainWithLegacyRowsAndTheJsonSurvivesBecauseARollbackMustLoseNoRevision()
    {
        await InsertLegacyRowsAsync();
        await MigrateToAsync(Migration);

        await MigrateToAsync(PreviousMigration());

        Assert.Equal([LegacyRows, "2|{\"b\":2}"], await ReadAsync("SELECT revision_number || '|' || revision_json FROM action_revisions ORDER BY revision_number"));
        Assert.Empty(await ReadAsync("SELECT table_name FROM information_schema.tables WHERE table_schema = current_schema() AND table_name = ANY (@names)", NewTables));
        Assert.Equal(["0"], await ReadAsync(NewColumnCount));
        Assert.Empty(await ReadAsync("SELECT conname FROM pg_constraint WHERE conname = 'ux_action_drafts_record_bill'"));

        await MigrateToAsync(Migration);

        Assert.Equal([LegacyRows, "2|{\"b\":2}"], await ReadAsync("SELECT revision_number || '|' || revision_json FROM action_revisions ORDER BY revision_number"));
        Assert.Equal(["3"], await ReadAsync(NewColumnCount));
    }

    private Task InsertLegacyRowsAsync() => ExecuteAsync($$"""
        SET session_replication_role = replica;
        INSERT INTO action_drafts (record_id, source_id, congress, bill_type, bill_number, current_revision_number, review_state_version,
            review_status, created_at_utc_ticks, last_revised_at_utc_ticks, candidate_id)
        VALUES ('{{RecordId}}', 'congress-bills', 119, 'hr', 7, 2, 0, 'AwaitingReview', 100, 100, 'c1');
        INSERT INTO action_revisions (record_id, revision_number, author_subject, author_kind, run_id, revision_json, created_at_utc_ticks)
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
