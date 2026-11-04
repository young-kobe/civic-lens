using CivicLens.Infrastructure.Collection;
using CivicLens.Tests.Fixtures;
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
    private const string Json = "{\"a\":1}";
    private const string Migration = "20261104011000_ActionRevisionDerivedColumns";
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
    public async Task ARowThatAlreadyHasTheEarlierColumnsMigratesWithNullDerivedColumnsBecauseTheSetCheckStaysAtItsFirstColumns()
    {
        await InsertRowWithTheEarlierColumnsAsync();

        await MigrateToAsync(Migration);

        Assert.Equal(["H||"], await ReadAsync("SELECT headline || '|' || COALESCE(event_on::text, '') || '|' || COALESCE(cosponsor_count::text, '') FROM action_revisions"));
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

    private Task InsertLegacyRowAsync() => ExecuteAsync(LegacyActionRevisionInsert.Sql(RecordId, Json));

    private Task InsertRowWithTheEarlierColumnsAsync() => ExecuteAsync($"""
        {LegacyActionRevisionInsert.Sql(RecordId, Json)};
        UPDATE action_revisions SET headline = 'H', summary = 'S', text_origin = 'Person', source_id = 'congress-bills', congress = 119, bill_type = 'hr',
            bill_number = 7, candidate_id = '{RecordId}', candidate_update_utc_ticks = 100, candidate_includes_summaries = false,
            candidate_includes_subjects = false, fingerprint = '{RecordId}', bill_title = 'T', issue_revision_id = '{RecordId}', issue_candidates_recorded = true
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
