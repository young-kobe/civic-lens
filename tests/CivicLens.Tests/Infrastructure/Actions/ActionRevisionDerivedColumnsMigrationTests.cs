using CivicLens.Infrastructure.Collection;
using CivicLens.Tests.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.Actions;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class ActionRevisionDerivedColumnsMigrationTests(PostgresCollection postgres) : IAsyncLifetime
{
    private static readonly string RecordId = new('a', 64);
    private const string Migration = "20261103233644_ActionRevisionDerivedColumns";
    private const string DerivedColumnCount = """
        SELECT count(*) FROM information_schema.columns
        WHERE table_schema = current_schema() AND table_name = 'action_revisions' AND column_name IN ('event_on', 'cosponsor_count')
        """;

    private string connectionString = null!;

    public async Task InitializeAsync()
    {
        connectionString = await postgres.CreateEmptyDatabaseAsync();
        await MigrateToAsync(PreviousMigration());
    }

    public Task DisposeAsync() => postgres.DropDatabaseAsync(connectionString);

    [Fact]
    public async Task ALegacyRowKeepsNullDerivedColumnsAndStaysValidBecauseTheBackfillFillsThemLater()
    {
        await InsertLegacyRowAsync();

        await MigrateToAsync(Migration);

        Assert.Equal(["|"], await ReadAsync("SELECT COALESCE(event_on::text, '') || '|' || COALESCE(cosponsor_count::text, '') FROM action_revisions"));
        Assert.Equal(["{\"a\":1}"], await ReadAsync("SELECT revision_json FROM action_revisions"));
    }

    [Fact]
    public async Task TheMigrationGoesDownAndUpAgainWithALegacyRowAndTheJsonSurvivesBecauseARollbackMustLoseNoRevision()
    {
        await InsertLegacyRowAsync();
        await MigrateToAsync(Migration);

        await MigrateToAsync(PreviousMigration());

        Assert.Equal(["{\"a\":1}"], await ReadAsync("SELECT revision_json FROM action_revisions"));
        Assert.Equal(["0"], await ReadAsync(DerivedColumnCount));
        Assert.Empty(await ReadAsync("SELECT indexname FROM pg_indexes WHERE indexname = 'ix_action_revisions_event_on'"));

        await MigrateToAsync(Migration);

        Assert.Equal(["2"], await ReadAsync(DerivedColumnCount));
        Assert.Equal(["ix_action_revisions_event_on"], await ReadAsync("SELECT indexname FROM pg_indexes WHERE indexname = 'ix_action_revisions_event_on'"));
    }

    private Task InsertLegacyRowAsync() => ExecuteAsync($$"""
        SET session_replication_role = replica;
        INSERT INTO action_drafts (record_id, source_id, congress, bill_type, bill_number, current_revision_number, review_state_version,
            review_status, created_at_utc_ticks, last_revised_at_utc_ticks, candidate_id)
        VALUES ('{{RecordId}}', 'congress-bills', 119, 'hr', 7, 1, 0, 'AwaitingReview', 100, 100, 'c1');
        INSERT INTO action_revisions (record_id, revision_number, author_subject, author_kind, run_id, revision_json, created_at_utc_ticks)
        VALUES ('{{RecordId}}', 1, 'analysis', 'Analysis', 'run1', '{"a":1}', 100)
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
