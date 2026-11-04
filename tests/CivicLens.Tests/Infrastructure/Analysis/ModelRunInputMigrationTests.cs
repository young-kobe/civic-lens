using CivicLens.Infrastructure.Collection;
using CivicLens.Tests.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.Analysis;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class ModelRunInputMigrationTests(PostgresCollection postgres) : IAsyncLifetime
{
    private const string Migration = "20261104154215_ModelRunInput";
    private static readonly string[] NewTables = ["analysis_output_schemas", "analysis_system_prompts"];
    private const string NewColumnCount = """
        SELECT count(*) FROM information_schema.columns
        WHERE table_schema = current_schema() AND table_name = 'document_change_analysis_runs'
          AND column_name IN ('system_prompt_sha256', 'output_schema_sha256', 'user_message', 'effort', 'max_output_tokens')
        """;

    private string connectionString = null!;

    public async Task InitializeAsync()
    {
        connectionString = await postgres.CreateEmptyDatabaseAsync();
        await MigrateToAsync(PreviousMigration());
    }

    public Task DisposeAsync() => postgres.DropDatabaseAsync(connectionString);

    // Runs written before the migration have no input, and no step rebuilds one. The migration must neither fill nor change them.
    [Fact]
    public async Task LegacyRunsKeepTheirColumnsAndGetNullInputBecauseNoStepCanRebuildAPastRequest()
    {
        await InsertLegacyRunAsync();

        await MigrateToAsync(Migration);

        Assert.Equal(["Refused|{\"a\":1}|5"], await ReadAsync("SELECT outcome || '|' || context_json || '|' || charged_tokens FROM document_change_analysis_runs"));
        Assert.Equal(["0"], await ReadAsync("SELECT num_nonnulls(system_prompt_sha256, output_schema_sha256, user_message, effort, max_output_tokens) FROM document_change_analysis_runs"));
        Assert.Equal(["0", "0"], await ReadAsync("SELECT count(*) FROM analysis_system_prompts UNION ALL SELECT count(*) FROM analysis_output_schemas"));
    }

    // A rollback drops the columns and the two tables. The run rows are the audit trail, so they must survive both ways.
    [Fact]
    public async Task TheMigrationGoesDownAndUpAgainWithLegacyRunsAndTheRunsSurviveBecauseARollbackMustLoseNoAudit()
    {
        await InsertLegacyRunAsync();
        await MigrateToAsync(Migration);

        await MigrateToAsync(PreviousMigration());

        Assert.Equal(["Refused|{\"a\":1}|5"], await ReadAsync("SELECT outcome || '|' || context_json || '|' || charged_tokens FROM document_change_analysis_runs"));
        Assert.Empty(await ReadAsync("SELECT table_name FROM information_schema.tables WHERE table_schema = current_schema() AND table_name = ANY (@names)", NewTables));
        Assert.Equal(["0"], await ReadAsync(NewColumnCount));

        await MigrateToAsync(Migration);

        Assert.Equal(["1"], await ReadAsync("SELECT count(*) FROM document_change_analysis_runs"));
        Assert.Equal(["5"], await ReadAsync(NewColumnCount));
    }

    private Task InsertLegacyRunAsync() => ExecuteAsync($$"""
        SET session_replication_role = replica;
        INSERT INTO document_change_analysis_runs (run_id, comparison_id, task, task_version, model, prompt_version, schema_version, input_hash,
            outcome, input_tokens, output_tokens, cache_read_tokens, cache_write_tokens, charged_tokens, context_json, started_at_utc_ticks, finished_at_utc_ticks)
        VALUES ('{{new string('a', 32)}}', '{{new string('b', 64)}}', 'draft', 'v', 'model', 'p', 's', '{{new string('c', 64)}}',
            'Refused', 1, 2, 0, 0, 5, '{"a":1}', 100, 200)
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
        _ = await command.ExecuteNonQueryAsync();
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
