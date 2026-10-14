using System.Text.Json;
using CivicLens.Application.Collection.Jobs;
using CivicLens.Collection.Contracts;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Collection.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.Collection;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class JobPacingKeysMigrationTests(PostgresCollection postgres) : IAsyncLifetime
{
    private const string PreviousMigration = "20261018130000_BillListUnknownTypes";
    private readonly string schema = "pacing_keys_" + Guid.NewGuid().ToString("N");
    private readonly string root = Path.Combine(Path.GetTempPath(), "civic-pacing-keys-" + Guid.NewGuid().ToString("N"));
    private string connectionString = null!;

    public async Task InitializeAsync()
    {
        await ExecuteAsync(postgres.ConnectionString, $"CREATE SCHEMA {schema}");
        connectionString = new NpgsqlConnectionStringBuilder(postgres.ConnectionString) { SearchPath = schema }.ConnectionString;
    }

    public Task DisposeAsync() => ExecuteAsync(postgres.ConnectionString, $"DROP SCHEMA IF EXISTS {schema} CASCADE");

    [Fact]
    public async Task AnOlderJobKeepsNoKeyAndStillWaitsForItsOriginAtAttemptStart()
    {
        await using var db = new CollectionAttemptDbContext(new DbContextOptionsBuilder<CollectionAttemptDbContext>()
            .UseNpgsql(connectionString).Options);
        await db.GetService<IMigrator>().MigrateAsync(PreviousMigration);
        await ExecuteAsync(connectionString, $"""
            INSERT INTO collection_jobs (job_id, idempotency_key, definition_json, state, created_at,
                cancellation_requested, charged_requests, charged_bytes, charged_seconds, lease_fence)
            VALUES ('older-job', 'older-key', '{JsonSerializer.Serialize(Definition(), CollectionProtocol.JsonOptions)}',
                'Pending', 1, false, 0, 0, 0, 0);
            """);

        await db.Database.MigrateAsync();
        await ExecuteAsync(connectionString, $"""
            INSERT INTO collection_job_origins (origin, not_before)
            VALUES ('https://example.test:443', {DateTimeOffset.UtcNow.AddHours(1).UtcTicks})
            """);

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT pacing_key FROM collection_jobs WHERE job_id = 'older-job'", connection);
        Assert.IsType<DBNull>(await command.ExecuteScalarAsync());
        var page = await PostgresCollectionWorkerQueue.FromConnectionString(connectionString).GetEligibleJobIdsAsync(null, 10, default);
        Assert.Equal("older-job", Assert.Single(page.JobIds));
        var jobs = PostgresCollectionJobStore.FromConnectionString(connectionString);
        var claim = await jobs.TryClaimAsync("older-job", TimeSpan.FromSeconds(30), default);
        Assert.NotNull(claim);
        var start = await jobs.TryStartAttemptAsync(claim.Lease, root, default);
        Assert.Equal(CollectionJobBlockReason.OriginBackoff, start.BlockReason);
    }

    private static CollectionJobDefinition Definition() => new()
    {
        SourceId = "source",
        Url = "https://example.test/page",
        AllowedOrigin = "https://example.test",
        AllowedPathPrefix = "/page",
        MaxRequests = 5,
        MaxBytes = 100,
        TimeoutSeconds = 10,
        MinDelayMilliseconds = 0,
        Policy = new CollectionJobPolicy()
    };

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
