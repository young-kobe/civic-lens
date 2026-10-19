using System.Text.Json;
using CivicLens.Application.Collection;
using CivicLens.Application.Collection.Discovery;
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
public sealed class ConfigurationRevisionReferencesMigrationTests(PostgresCollection postgres) : IAsyncLifetime
{
    private const string PreviousMigration = "20261013120000_BillStreamCursor";
    private string connectionString = null!;

    public async Task InitializeAsync()
    {
        connectionString = await postgres.CreateEmptyDatabaseAsync();
    }

    public Task DisposeAsync() => postgres.DropDatabaseAsync(connectionString);

    [Fact]
    public async Task LegacyRowsThatEmbedTheirRevisionReadBackIdenticallyFromOneSharedRevision()
    {
        var definition = ArticleDefinition("Original");
        await using var db = new CollectionAttemptDbContext(new DbContextOptionsBuilder<CollectionAttemptDbContext>()
            .UseNpgsql(connectionString).Options);
        await db.GetService<IMigrator>().MigrateAsync(PreviousMigration);
        await SeedLegacyRowsAsync(definition);

        await db.Database.MigrateAsync();

        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM collection_configuration_revisions"));
        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM collection_jobs WHERE definition_json::jsonb -> 'configurationRevision' IS NOT NULL"));
        Assert.Equal(2L, await ScalarAsync(
            $"SELECT count(*) FROM collection_jobs WHERE configuration_revision_id = '{definition.ConfigurationRevision!.Id}'"));
        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM collection_admission_batches WHERE input_json::jsonb -> 'articleTemplate' -> 'configurationRevision' IS NOT NULL"));
        Assert.Equal(1L, await ScalarAsync(
            $"SELECT count(*) FROM collection_admission_batches WHERE configuration_revision_id = '{definition.ConfigurationRevision.Id}'"));
        Assert.Equal(0L, await ScalarAsync(
            "SELECT count(*) FROM information_schema.columns WHERE table_schema = current_schema() AND column_name = 'configuration_json'"));
        Assert.Equal(2L, await ScalarAsync(
            $"SELECT count(*) FROM collection_source_schedules WHERE configuration_revision_id = '{definition.ConfigurationRevision.Id}'"));
        var jobs = PostgresCollectionJobStore.FromConnectionString(connectionString);
        foreach (var id in new[] { "legacy-job-1", "legacy-job-2" })
            Assert.Equal(definition, (await jobs.GetAsync(id, default))!.Definition);

        var request = new DiscoveryAdmissionRequest("legacy-attempt", "legacy-batch", definition, new DiscoveryAdmissionPolicy());
        await Assert.ThrowsAsync<ArgumentException>(() => jobs.AdmitAsync(request, default));
    }

    private async Task SeedLegacyRowsAsync(CollectionJobDefinition definition)
    {
        var legacyDefinition = JsonSerializer.Serialize(definition, CollectionProtocol.JsonOptions);
        var legacyInput = JsonSerializer.Serialize(new
        {
            attemptId = "legacy-attempt",
            articleTemplate = definition,
            policy = new DiscoveryAdmissionPolicy()
        }, CollectionProtocol.JsonOptions);
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var seed = new NpgsqlCommand("""
            SET session_replication_role = replica;
            INSERT INTO collection_jobs (job_id, idempotency_key, definition_json, state, created_at,
                cancellation_requested, charged_requests, charged_bytes, charged_seconds, lease_fence)
            VALUES ('legacy-job-1', 'legacy-key-1', @definition, 'Pending', 1, false, 0, 0, 0, 0),
                   ('legacy-job-2', 'legacy-key-2', @definition, 'Pending', 2, false, 0, 0, 0, 0);
            INSERT INTO collection_admission_batches (idempotency_key, attempt_id, input_json, result_json, created_at)
            VALUES ('legacy-batch', 'legacy-attempt', @input,
                '{"jobs":[{"jobId":"legacy-job-1","url":"https://example.test/articles/template"}],"deferredCount":0,"duplicateCount":0,"admittedCount":1}', 1);

            INSERT INTO collection_source_schedules (source_id, configuration_revision_id, configuration_json,
                interval_seconds, next_due_utc_ticks, enabled)
            VALUES ('source', @revisionId, @revisionJson, 3600, 0, FALSE), ('source-two', @revisionId, @revisionJson, 3600, 0, FALSE);
            """, connection);
        seed.Parameters.AddWithValue("revisionId", definition.ConfigurationRevision!.Id);
        seed.Parameters.AddWithValue("revisionJson", definition.ConfigurationRevision.Json);
        seed.Parameters.AddWithValue("definition", legacyDefinition);
        seed.Parameters.AddWithValue("input", legacyInput);
        await seed.ExecuteNonQueryAsync();
    }

    private static CollectionJobDefinition ArticleDefinition(string personName)
    {
        var configuration = new CollectionConfiguration
        {
            People = [new PersonConfiguration { Id = "person", Name = personName }],
            Sources = [new WatchedSourceConfiguration
            {
                Id = "source", Coverage = [new SourceCoverageConfiguration { PersonId = "person" }], Url = "https://example.test/feed.xml",
                AllowedOrigin = "https://example.test", AllowedPathPrefix = "/", Mode = CollectionMode.Feed,
                MaxRequests = 2, MaxBytes = 10, TimeoutSeconds = 4, MinDelayMilliseconds = 0,
                JobPolicy = new CollectionJobPolicy { MaxAttempts = 2, InitialRetryDelaySeconds = 0, MaximumRetryDelaySeconds = 0 }
            }]
        };
        return CollectionJobDefinition.FromConfiguration(configuration, "source", new DateOnly(2026, 7, 1)) with
        { Mode = CollectionMode.Page, ETag = null, LastModified = null, Url = "https://example.test/articles/template" };
    }

    private async Task<object?> ScalarAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return await command.ExecuteScalarAsync();
    }
}
