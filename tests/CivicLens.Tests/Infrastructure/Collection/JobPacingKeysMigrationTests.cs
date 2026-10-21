using System.Text.Json;
using CivicLens.Application.Collection.Jobs;
using CivicLens.Collection.Contracts;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Collection.Jobs;
using CivicLens.Tests.Fixtures;
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
    private readonly string root = Path.Combine(Path.GetTempPath(), "civic-pacing-keys-" + Guid.NewGuid().ToString("N"));
    private string connectionString = null!;

    public async Task InitializeAsync()
    {
        connectionString = await postgres.CreateEmptyDatabaseAsync();
    }

    public Task DisposeAsync() => postgres.DropDatabaseAsync(connectionString);

    [Fact]
    public async Task AnOlderJobKeepsNoKeyAndStillWaitsForItsOriginAtAttemptStart()
    {
        await using var db = new CollectionAttemptDbContext(new DbContextOptionsBuilder<CollectionAttemptDbContext>()
            .UseNpgsql(connectionString).Options);
        await db.GetService<IMigrator>().MigrateAsync(PreviousMigration);
        var definition = CollectionFixtures.PageDefinition();
        await ExecuteAsync(connectionString, $"""
            INSERT INTO collection_configuration_revisions (revision_id, json, created_at)
            VALUES ('{definition.ConfigurationRevision!.Id}', '{definition.ConfigurationRevision.Json}', 1);
            INSERT INTO collection_jobs (job_id, idempotency_key, definition_json, configuration_revision_id, state, created_at,
                cancellation_requested, charged_requests, charged_bytes, charged_seconds, lease_fence)
            VALUES ('older-job', 'older-key', '{JsonSerializer.Serialize(definition with { ConfigurationRevision = null }, CollectionProtocol.JsonOptions)}',
                '{definition.ConfigurationRevision.Id}', 'Pending', 1, false, 0, 0, 0, 0);
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

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
