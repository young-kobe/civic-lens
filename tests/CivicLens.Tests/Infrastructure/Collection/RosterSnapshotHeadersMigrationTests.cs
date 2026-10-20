using CivicLens.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.Collection;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class RosterSnapshotHeadersMigrationTests(PostgresCollection postgres) : IAsyncLifetime
{
    private const string PreviousMigration = "20261106120000_RosterSnapshots";
    private static readonly string Capture = $"INSERT INTO collection_captures (sha256, byte_length) VALUES ('{new string('c', 64)}', 10);";
    private string connectionString = null!;

    public async Task InitializeAsync() => connectionString = await postgres.CreateEmptyDatabaseAsync();

    public Task DisposeAsync() => postgres.DropDatabaseAsync(connectionString);

    [Fact]
    public async Task EachExistingSnapshotGetsOneHeaderWithItsSourceSoNoReaderNeedsTheOldShape()
    {
        await using var db = CreateContext();
        await db.GetService<IMigrator>().MigrateAsync(PreviousMigration);
        await ExecuteAsync(Capture + AttemptWithMembers("a1", "members", 2) + AttemptWithMembers("a2", "members", 1) +
            AttemptWithMembers("a3", "other", 1) + Attempt("a4", "members"));

        await db.Database.MigrateAsync();

        Assert.Equal(["a1:members", "a2:members", "a3:other"], await ReadHeadersAsync());
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task TheMigrationCanBeRolledBackAndLeavesTheMemberRowsOfEverySnapshot()
    {
        await using var db = CreateContext();
        await db.GetService<IMigrator>().MigrateAsync(PreviousMigration);
        await ExecuteAsync(Capture + AttemptWithMembers("a1", "members", 2));
        await db.Database.MigrateAsync();

        await db.GetService<IMigrator>().MigrateAsync(PreviousMigration);

        Assert.Equal(2L, await CountAsync("SELECT count(*) FROM roster_members"));
        Assert.Equal(0L, await CountAsync("SELECT count(*) FROM information_schema.tables WHERE table_name = 'roster_snapshots'"));
    }

    private static string Attempt(string attemptId, string sourceId) => $$"""
        INSERT INTO collection_attempts (attempt_id, source_id, requested_url, final_url, observed_at_utc_ticks, outcome,
            has_response, response_status_code, response_content_encodings, has_sent_validators, capture_sha256)
        VALUES ('{{attemptId}}', '{{sourceId}}', 'https://example.test/{{attemptId}}', 'https://example.test/{{attemptId}}', 0,
            'captured', true, 200, '{}', false, '{{new string('c', 64)}}');
        """;

    private static string AttemptWithMembers(string attemptId, string sourceId, int count) =>
        Attempt(attemptId, sourceId) + string.Concat(Enumerable.Range(1, count).Select(index => $$"""
            INSERT INTO roster_members (attempt_id, bioguide_id, full_name) VALUES ('{{attemptId}}', 'A00000{{index}}', 'Member {{index}}');
            """));

    private CollectionAttemptDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options);

    private async Task<List<string>> ReadHeadersAsync()
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT attempt_id || ':' || source_id FROM roster_snapshots ORDER BY 1", connection);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<string>();
        while (await reader.ReadAsync()) rows.Add(reader.GetString(0));
        return rows;
    }

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
