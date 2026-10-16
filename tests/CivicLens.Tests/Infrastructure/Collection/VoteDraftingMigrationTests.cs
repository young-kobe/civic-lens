using CivicLens.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.Collection;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class VoteDraftingMigrationTests(PostgresCollection postgres) : IAsyncLifetime
{
    private const string PreviousMigration = "20261027120000_HouseVotes";
    private string connectionString = null!;

    public async Task InitializeAsync()
    {
        connectionString = await postgres.CreateEmptyDatabaseAsync();
    }

    public Task DisposeAsync() => postgres.DropDatabaseAsync(connectionString);

    [Fact]
    public async Task BackfillQueuesOnePendingDraftRowForEveryVoteCandidateThatExistedBeforeTheMigration()
    {
        await using var db = new CollectionAttemptDbContext(new DbContextOptionsBuilder<CollectionAttemptDbContext>()
            .UseNpgsql(connectionString).Options);
        await db.GetService<IMigrator>().MigrateAsync(PreviousMigration);
        await ExecuteAsync("SET session_replication_role = replica;" + Candidate('a', 100, 100) + Candidate('b', 101, 200));

        await db.Database.MigrateAsync();

        Assert.Equal([
            [new string('a', 64), "vote-draft", "Pending", "0", "0", "0", "100"],
            [new string('b', 64), "vote-draft", "Pending", "0", "0", "0", "200"]],
            await ReadAsync("SELECT candidate_id, task, status, fence, attempts, reserved_tokens, created_at_utc_ticks FROM vote_analysis ORDER BY candidate_id"));
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task TheQueueTableWakesTheWorkerThroughTheNotifyTrigger()
    {
        await using var db = new CollectionAttemptDbContext(new DbContextOptionsBuilder<CollectionAttemptDbContext>()
            .UseNpgsql(connectionString).Options);
        await db.Database.MigrateAsync();

        Assert.Equal([["trg_vote_analysis_notify"]],
            await ReadAsync("SELECT tgname FROM pg_trigger WHERE tgrelid = 'vote_analysis'::regclass AND NOT tgisinternal"));
    }

    private static string Candidate(char id, int roll, long created) => $$"""
        INSERT INTO house_vote_candidates (candidate_id, source_id, congress, session_number, roll_call_number, update_date_utc_ticks,
            detail_job_id, members_job_id, detail_extraction_id, members_extraction_id, configuration_revision_id, created_at_utc_ticks)
        VALUES ('{{new string(id, 64)}}', 'source', 119, 1, {{roll}}, 1, 'detail-{{id}}', 'members-{{id}}', '{{new string('d', 64)}}',
            '{{new string('e', 64)}}', '{{new string('c', 64)}}', {{created}});
        """;

    private async Task<List<string[]>> ReadAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<string[]>();
        while (await reader.ReadAsync())
            rows.Add([.. Enumerable.Range(0, reader.FieldCount).Select(index => reader.GetValue(index).ToString()!)]);
        return rows;
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
