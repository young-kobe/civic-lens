using CivicLens.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.Collection;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class KeysetReadsMigrationTests(PostgresCollection postgres) : IAsyncLifetime
{
    private const string PreviousMigration = "20261009120000_PublicationReleases";
    private readonly string schema = "keyset_" + Guid.NewGuid().ToString("N");
    private string connectionString = null!;

    public async Task InitializeAsync()
    {
        await ExecuteAsync(postgres.ConnectionString, $"CREATE SCHEMA {schema}");
        connectionString = new NpgsqlConnectionStringBuilder(postgres.ConnectionString) { SearchPath = schema }.ConnectionString;
    }

    public Task DisposeAsync() => ExecuteAsync(postgres.ConnectionString, $"DROP SCHEMA IF EXISTS {schema} CASCADE");

    // Rows written before the migration have the time and kind only inside their JSON. The new columns must carry the same values.
    [Fact]
    public async Task ExistingRevisionsAndDecisionsKeepTheirTimeAndKindInTheNewColumns()
    {
        await using var db = new CollectionAttemptDbContext(new DbContextOptionsBuilder<CollectionAttemptDbContext>()
            .UseNpgsql(connectionString).Options);
        await db.GetService<IMigrator>().MigrateAsync(PreviousMigration);
        var created = DateTimeOffset.Parse("2026-10-08T16:32:08.1234567+00:00");
        var draft = new string('a', 32);
        // Foreign keys are skipped: only the three review tables matter to the backfill.
        await ExecuteAsync(connectionString, $$"""
            SET session_replication_role = replica;
            INSERT INTO document_change_revisions (draft_id, revision_number, revision_json)
            VALUES ('{{draft}}', 1, '{"createdAtUtc":"2026-10-08T16:32:08.1234567+00:00"}');
            INSERT INTO document_change_review_decisions (decision_id, draft_id, revision_number, review_state_version, decision_json)
            VALUES ('{{new string('1', 32)}}', '{{draft}}', 1, 1, '{"createdAtUtc":"2026-10-08T16:32:08.1234567+00:00","kind":0}'),
                   ('{{new string('2', 32)}}', '{{draft}}', 1, 2, '{"createdAtUtc":"2026-10-08T16:32:08.1234567+00:00","kind":1}'),
                   ('{{new string('3', 32)}}', '{{draft}}', 1, 3, '{"createdAtUtc":"2026-10-08T16:32:08.1234567+00:00","kind":2}');
            """);

        await db.Database.MigrateAsync();

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        // The database keeps microseconds and rounds the seventh digit.
        var expected = (created.UtcTicks + 5) / 10 * 10;
        await using var revisions = new NpgsqlCommand("SELECT created_at_utc_ticks FROM document_change_revisions", connection);
        Assert.Equal(expected, await revisions.ExecuteScalarAsync());
        await using var decisions = new NpgsqlCommand(
            "SELECT kind, created_at_utc_ticks FROM document_change_review_decisions ORDER BY review_state_version", connection);
        await using var reader = await decisions.ExecuteReaderAsync();
        var rows = new List<(string Kind, long Ticks)>();
        while (await reader.ReadAsync()) rows.Add((reader.GetString(0), reader.GetInt64(1)));
        Assert.Equal([("Approve", expected), ("RequestChanges", expected), ("WithdrawApproval", expected)], rows);
    }

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
