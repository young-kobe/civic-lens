using System.Security.Cryptography;
using System.Text;
using CivicLens.Application.Collection;
using CivicLens.Application.Collection.Jobs;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Collection.Jobs;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.Collection.Jobs;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class PostgresCollectionScheduleStoreTests(PostgresCollection postgres) : IAsyncLifetime
{
    private readonly string schema = "source_schedules_" + Guid.NewGuid().ToString("N");
    private string connectionString = null!;
    private PostgresCollectionJobStore store = null!;

    public async Task InitializeAsync()
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"CREATE SCHEMA {schema}", connection);
        await command.ExecuteNonQueryAsync();
        connectionString = new NpgsqlConnectionStringBuilder(postgres.ConnectionString) { SearchPath = schema }.ConnectionString;
        store = PostgresCollectionJobStore.FromConnectionString(connectionString);
        await PostgresCollectionAttemptStore.FromConnectionString(connectionString).MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"DROP SCHEMA IF EXISTS {schema} CASCADE", connection);
        await command.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task NewSourceIsDueImmediatelyAndMissedIntervalsCoalesceToOneJob()
    {
        await store.SynchronizeAsync(Configuration(), default);
        Assert.Equal(WatchedSourceConfiguration.DefaultCheckIntervalSeconds, await ReadIntervalAsync("senate"));
        await SetDueAsync("senate", DateTimeOffset.UtcNow.AddDays(-7).UtcTicks);

        Assert.Equal(1, await store.AdvanceDueAsync(10, default));
        Assert.Equal(0, await store.AdvanceDueAsync(10, default));
        Assert.Single(await store.ListAsync(10, default));
        Assert.True(await ReadNextDueAsync("senate") > DateTimeOffset.UtcNow.UtcTicks);
    }

    [Fact]
    public async Task RestartPreservesDueTimeAndCadenceChangeStartsNewInterval()
    {
        await store.SynchronizeAsync(Configuration(), default);
        var due = DateTimeOffset.UtcNow.AddMinutes(15).UtcTicks;
        await SetDueAsync("senate", due);
        await store.SynchronizeAsync(Configuration(), default);
        Assert.Equal(due, await ReadNextDueAsync("senate"));

        await store.SynchronizeAsync(Configuration(intervalSeconds: 600), default);
        var changedDue = await ReadNextDueAsync("senate");
        Assert.InRange(changedDue, DateTimeOffset.UtcNow.AddSeconds(590).UtcTicks,
            DateTimeOffset.UtcNow.AddSeconds(610).UtcTicks);
    }

    [Fact]
    public async Task ConcurrentSchedulersAdmitOnlyOneRootJob()
    {
        await store.SynchronizeAsync(Configuration(intervalSeconds: 60), default);
        var admitted = await Task.WhenAll(store.AdvanceDueAsync(10, default), store.AdvanceDueAsync(10, default));

        Assert.Equal(1, admitted.Sum());
        Assert.Single(await store.ListAsync(10, default));
    }

    [Fact]
    public async Task ActiveManualRootJobConsumesDueSlotWithoutScheduledDuplicate()
    {
        var configuration = Configuration(intervalSeconds: 60);
        await store.SynchronizeAsync(configuration, default);
        var configured = new ConfiguredCollectionSource(configuration, "senate");
        await store.EnqueueAsync(configured, "manual-request", default);

        Assert.Equal(1, await store.AdvanceDueAsync(10, default));
        Assert.Single(await store.ListAsync(10, default));
        Assert.True(await ReadNextDueAsync("senate") > DateTimeOffset.UtcNow.UtcTicks);
    }

    [Fact]
    public async Task DisabledAndRemovedSourcesPauseTheirScheduleWithoutDeletingIt()
    {
        await store.SynchronizeAsync(Configuration(), default);
        await store.SynchronizeAsync(Configuration(enabled: false), default);
        Assert.False(await ReadEnabledAsync("senate"));
        Assert.Equal(0, await store.AdvanceDueAsync(10, default));

        await store.SynchronizeAsync(Configuration(withSource: false), default);
        Assert.False(await ReadEnabledAsync("senate"));
        Assert.Equal(0, await store.AdvanceDueAsync(10, default));
    }

    [Fact]
    public async Task NoCoverageOnDatabaseAdmissionDateConsumesSlotWithoutCreatingJob()
    {
        await store.SynchronizeAsync(Configuration(version: 2, coverageStartsOn: DateOnly.MaxValue), default);

        Assert.Equal(1, await store.AdvanceDueAsync(10, default));
        Assert.Empty(await store.ListAsync(10, default));
        Assert.True(await ReadNextDueAsync("senate") > DateTimeOffset.UtcNow.UtcTicks);
    }

    [Fact]
    public async Task FailedBatchRollsBackEarlierJobAndScheduleAdvancement()
    {
        await store.SynchronizeAsync(Configuration(), default);
        await SetDueAsync("senate", DateTimeOffset.UtcNow.AddMinutes(-5).UtcTicks);
        await ExecuteAsync("""
            INSERT INTO collection_source_schedules (source_id, configuration_revision_id, configuration_json,
                interval_seconds, next_due_utc_ticks, enabled)
            VALUES ('zz-invalid', @revision, '{', 3600, @due, TRUE)
            """, ("revision", Hash("{")), ("due", DateTimeOffset.UtcNow.AddMinutes(-1).UtcTicks));

        await Assert.ThrowsAsync<ArgumentException>(() => store.AdvanceDueAsync(10, default));
        Assert.Empty(await store.ListAsync(10, default));
        Assert.True(await ReadNextDueAsync("senate") < DateTimeOffset.UtcNow.UtcTicks);
    }

    [Fact]
    public async Task SkippedDueSlotsDoNotStrandEligibleSourcesBeyondFirstBatch()
    {
        var configuration = Configuration(version: 2, coverageStartsOn: DateOnly.MaxValue);
        var first = configuration.Sources[0];
        configuration = configuration with
        {
            Sources = [first, first with
            {
                Id = "z-current",
                Url = "https://example.test/current",
                Coverage = [new SourceCoverageConfiguration { PersonId = "person" }]
            }]
        };
        await store.SynchronizeAsync(configuration, default);
        var pipeline = new CollectionJobWorker(new EmptyQueue(),
            new RunCollectionJob(null!, null!, null!, null!, null!), schedules: store);

        var result = await pipeline.ExecuteAsync(Path.GetTempPath(), once: true, batchSize: 1);

        Assert.Equal(0, result.JobFailures);
        Assert.Equal("z-current", Assert.Single(await store.ListAsync(10, default)).Definition.SourceId);
        Assert.True(await ReadNextDueAsync("senate") > DateTimeOffset.UtcNow.UtcTicks);
        Assert.True(await ReadNextDueAsync("z-current") > DateTimeOffset.UtcNow.UtcTicks);
    }

    private sealed class EmptyQueue : ICollectionWorkerQueue
    {
        public Task<CollectionWorkerQueuePage> GetEligibleJobIdsAsync(CollectionWorkerCursor? cursor, int limit,
            CancellationToken cancellationToken) => Task.FromResult(new CollectionWorkerQueuePage([], null));
    }

    private static CollectionConfiguration Configuration(int intervalSeconds = WatchedSourceConfiguration.DefaultCheckIntervalSeconds,
        bool enabled = true, bool withSource = true, int version = 1, DateOnly? coverageStartsOn = null)
    {
        var source = new WatchedSourceConfiguration
        {
            Id = "senate",
            PersonIds = version == 1 ? ["person"] : null,
            Coverage = version == 2 ? [new SourceCoverageConfiguration
            {
                PersonId = "person",
                StartsOn = coverageStartsOn ?? DateOnly.MinValue
            }] : null,
            Url = "https://example.test/senate",
            AllowedOrigin = "https://example.test",
            AllowedPathPrefix = "/",
            Enabled = enabled,
            CheckIntervalSeconds = intervalSeconds == WatchedSourceConfiguration.DefaultCheckIntervalSeconds
                ? null : intervalSeconds
        };
        return new CollectionConfiguration
        {
            Version = version,
            People = [new PersonConfiguration { Id = "person", Name = "Person" }],
            Sources = withSource ? [source] : []
        };
    }

    private async Task<long> ReadNextDueAsync(string sourceId) =>
        await ReadLongAsync("SELECT next_due_utc_ticks FROM collection_source_schedules WHERE source_id = @source", sourceId);

    private async Task<int> ReadIntervalAsync(string sourceId) =>
        (int)await ReadLongAsync("SELECT interval_seconds FROM collection_source_schedules WHERE source_id = @source", sourceId);

    private async Task<bool> ReadEnabledAsync(string sourceId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT enabled FROM collection_source_schedules WHERE source_id = @source", connection);
        command.Parameters.AddWithValue("source", sourceId);
        return (bool)(await command.ExecuteScalarAsync() ?? throw new InvalidOperationException("Schedule is missing."));
    }

    private async Task<long> ReadLongAsync(string sql, string sourceId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("source", sourceId);
        return Convert.ToInt64(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private async Task SetDueAsync(string sourceId, long ticks) =>
        await ExecuteAsync("UPDATE collection_source_schedules SET next_due_utc_ticks = @due WHERE source_id = @source",
            ("source", sourceId), ("due", ticks));

    private async Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var parameter in parameters) command.Parameters.AddWithValue(parameter.Name, parameter.Value);
        await command.ExecuteNonQueryAsync();
    }

    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
