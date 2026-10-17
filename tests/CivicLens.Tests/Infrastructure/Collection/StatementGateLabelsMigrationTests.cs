using CivicLens.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.Collection;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class StatementGateLabelsMigrationTests(PostgresCollection postgres) : IAsyncLifetime
{
    private const string PreviousMigration = "20261029120000_GateBillOrVoteReasons";
    private string connectionString = null!;

    public async Task InitializeAsync() => connectionString = await postgres.CreateEmptyDatabaseAsync();

    public Task DisposeAsync() => postgres.DropDatabaseAsync(connectionString);

    [Fact]
    public async Task TheMigrationAddsTheLabelTableAndLeavesNoPendingModelChanges()
    {
        await using var db = CreateContext();
        await db.GetService<IMigrator>().MigrateAsync(PreviousMigration);
        Assert.Equal(0L, await CountAsync("SELECT count(*) FROM information_schema.tables WHERE table_name = 'statement_gate_labels'"));

        await db.Database.MigrateAsync();

        Assert.Equal(1L, await CountAsync("SELECT count(*) FROM information_schema.tables WHERE table_name = 'statement_gate_labels'"));
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task TheRunIdHasAForeignKeyToTheAnalysisRunsAndAUniqueIndexSoOneRunLabelsOneRelease()
    {
        await using var db = CreateContext();
        await db.Database.MigrateAsync();

        Assert.Equal(1L, await CountAsync("""
            SELECT count(*) FROM pg_constraint
            WHERE conname = 'FK_statement_gate_labels_statement_analysis_runs_run_id' AND confrelid = 'statement_analysis_runs'::regclass
            """));
        Assert.Equal(1L, await CountAsync("""
            SELECT count(*) FROM pg_indexes
            WHERE indexname = 'IX_statement_gate_labels_run_id' AND indexdef LIKE 'CREATE UNIQUE INDEX%'
            """));
    }

    [Fact]
    public async Task TheMigrationCanBeRolledBackSoADeploymentCanReturnToThePreviousSchema()
    {
        await using var db = CreateContext();
        await db.Database.MigrateAsync();

        await db.GetService<IMigrator>().MigrateAsync(PreviousMigration);

        Assert.Equal(0L, await CountAsync("SELECT count(*) FROM information_schema.tables WHERE table_name = 'statement_gate_labels'"));
    }

    [Theory]
    [InlineData("Analysis", "NULL")]
    [InlineData("Person", "'" + "11111111111111111111111111111111" + "'")]
    public async Task TheDatabaseRefusesAModelLabelWithoutARunAndAHumanLabelWithOne(string kind, string runId)
    {
        await using var db = CreateContext();
        await db.Database.MigrateAsync();

        var failure = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync($"""
            SET session_replication_role = replica;
            INSERT INTO statement_gate_labels (label_id, candidate_id, extraction_id, author_subject, author_kind, run_id, label_json, created_at_utc_ticks)
            VALUES ('{new string('a', 32)}', '{new string('1', 64)}', '{new string('2', 64)}', 'auth0|owner', '{kind}', {runId}, 'label', 0);
            """));

        Assert.Equal("ck_statement_gate_labels_run", failure.ConstraintName);
    }

    private CollectionAttemptDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options);

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<long> CountAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return (long)(await command.ExecuteScalarAsync())!;
    }
}
