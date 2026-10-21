using CivicLens.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.Collection;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class ReaderReportsMigrationTests(PostgresCollection postgres) : IAsyncLifetime
{
    private const string PreviousMigration = "20261108120000_RelationshipTargetRecitedReason";
    private string connectionString = null!;

    public async Task InitializeAsync() => connectionString = await postgres.CreateEmptyDatabaseAsync();

    public Task DisposeAsync() => postgres.DropDatabaseAsync(connectionString);

    [Fact]
    public async Task TheMigrationAddsTheReportTablesAndLeavesNoPendingModelChanges()
    {
        await using var db = CreateContext();
        await db.GetService<IMigrator>().MigrateAsync(PreviousMigration);
        Assert.Equal(0L, await CountAsync(TablesSql));

        await db.Database.MigrateAsync();

        Assert.Equal(2L, await CountAsync(TablesSql));
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task TheMigrationCanBeRolledBack()
    {
        await using var db = CreateContext();
        await db.Database.MigrateAsync();

        await db.GetService<IMigrator>().MigrateAsync(PreviousMigration);

        Assert.Equal(0L, await CountAsync(TablesSql));
    }

    [Fact]
    public async Task TheDatabaseRefusesACorrectionWithoutANoteAndAnOpenReportWithAResolverBecauseTheQueueRulesDoNotDependOnTheCode()
    {
        await using var db = CreateContext();
        await db.Database.MigrateAsync();
        var record = new string('a', 64);

        await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(Insert(record, "CorrectionNeeded", "'auth0|friend'", "1", "NULL")));
        await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(Insert(record, "Open", "'auth0|friend'", "1", "NULL")));
        await ExecuteAsync(Insert(record, "CorrectionNeeded", "'auth0|friend'", "1", "'Fix it.'"));
    }

    private const string TablesSql = """
        SELECT count(*) FROM information_schema.tables WHERE table_name IN ('reader_reports', 'reader_report_cursor')
        """;

    private static string Insert(string record, string status, string resolvedBy, string resolvedAt, string note) => $"""
        INSERT INTO reader_reports (report_id, record_id, kind, revision_number, category, body, received_at_utc_ticks, status, note,
            resolved_by, resolved_at_utc_ticks, version)
        VALUES (gen_random_uuid(), '{record}', 'Statement', 1, 'other', 'text', 1, '{status}', {note}, {resolvedBy}, {resolvedAt}, 0)
        """;

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
