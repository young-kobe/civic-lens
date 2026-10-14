using System.Security.Cryptography;
using System.Text;
using CivicLens.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.Collection;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class ActionDraftingMigrationTests(PostgresCollection postgres) : IAsyncLifetime
{
    private const string PreviousMigration = "20261020120000_JobPacingKeys";
    private readonly string schema = "action_queue_" + Guid.NewGuid().ToString("N");
    private string connectionString = null!;

    public async Task InitializeAsync()
    {
        await ExecuteAsync($"CREATE SCHEMA {schema}", postgres.ConnectionString);
        connectionString = new NpgsqlConnectionStringBuilder(postgres.ConnectionString) { SearchPath = schema }.ConnectionString;
    }

    public Task DisposeAsync() => ExecuteAsync($"DROP SCHEMA IF EXISTS {schema} CASCADE", postgres.ConnectionString);

    [Fact]
    public async Task BackfillQueuesOnePendingDraftRowForEveryCandidateThatExistedBeforeTheMigration()
    {
        await using var db = new CollectionAttemptDbContext(new DbContextOptionsBuilder<CollectionAttemptDbContext>()
            .UseNpgsql(connectionString).Options);
        await db.GetService<IMigrator>().MigrateAsync(PreviousMigration);
        await ExecuteAsync("SET session_replication_role = replica;" + Candidate('a', 11, 100) + Candidate('b', 12, 200), connectionString);

        await db.Database.MigrateAsync();

        Assert.Equal([
            [new string('a', 64), "action-draft", "Pending", "0", "0", "0", "100"],
            [new string('b', 64), "action-draft", "Pending", "0", "0", "0", "200"]],
            await ReadAsync("SELECT candidate_id, task, status, fence, attempts, reserved_tokens, created_at_utc_ticks FROM action_analysis ORDER BY candidate_id"));
    }

    [Fact]
    public async Task ACandidateWhoseSummariesHadClosedGetsASecondCandidateWithSummariesAndBothAreQueued()
    {
        await using var db = new CollectionAttemptDbContext(new DbContextOptionsBuilder<CollectionAttemptDbContext>()
            .UseNpgsql(connectionString).Options);
        await db.GetService<IMigrator>().MigrateAsync(PreviousMigration);
        await ExecuteAsync("SET session_replication_role = replica;" + Candidate('a', 11, 100) + Candidate('b', 12, 200) +
            $"UPDATE action_candidates SET summaries_completed_at_utc_ticks = 300 WHERE candidate_id = '{new string('b', 64)}';" +
            $"INSERT INTO collection_configuration_revisions (revision_id, json, created_at) VALUES ('{new string('c', 64)}', '{{}}', 0);" +
            "INSERT INTO bill_gates (detail_job_id, source_id, congress, bill_type, bill_number, update_date_utc_ticks, issue_revision_id, " +
            $"decision, reason, created_at_utc_ticks, decided_at_utc_ticks) VALUES ('job-b', 'source', 119, 'hr', 12, 1, '{new string('e', 64)}', " +
            "'Passed', 'CoveredSponsor', 0, 0);", connectionString);

        await db.Database.MigrateAsync();

        var withSummaries = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("6:source119:hr:12:1:summaries")));
        Assert.Equal([
            [new string('a', 64), "job-a", "100", "False"],
            [new string('b', 64), "job-b", "200", "False"],
            [withSummaries, "job-b", "300", "True"]],
            await ReadAsync("SELECT candidate_id, detail_job_id, created_at_utc_ticks, includes_summaries FROM action_candidates ORDER BY detail_job_id, includes_summaries"));
        Assert.Equal([[new string('a', 64)], [new string('b', 64)], [withSummaries]],
            await ReadAsync("SELECT candidate_id FROM action_analysis WHERE status = 'Pending' ORDER BY created_at_utc_ticks"));
    }

    [Fact]
    public async Task TheQueueTableWakesTheWorkerThroughTheNotifyTrigger()
    {
        await using var db = new CollectionAttemptDbContext(new DbContextOptionsBuilder<CollectionAttemptDbContext>()
            .UseNpgsql(connectionString).Options);
        await db.Database.MigrateAsync();

        Assert.Equal([["trg_action_analysis_notify"]],
            await ReadAsync("SELECT tgname FROM pg_trigger WHERE tgrelid = 'action_analysis'::regclass AND NOT tgisinternal"));
    }

    private static string Candidate(char id, int number, long created) => $$"""
        INSERT INTO action_candidates (candidate_id, source_id, congress, bill_type, bill_number, update_date_utc_ticks, detail_job_id,
            configuration_revision_id, created_at_utc_ticks)
        VALUES ('{{new string(id, 64)}}', 'source', 119, 'hr', {{number}}, 1, 'job-{{id}}', '{{new string('c', 64)}}', {{created}});
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

    private static async Task ExecuteAsync(string sql, string connection)
    {
        await using var open = new NpgsqlConnection(connection);
        await open.OpenAsync();
        await using var command = new NpgsqlCommand(sql, open);
        await command.ExecuteNonQueryAsync();
    }
}
