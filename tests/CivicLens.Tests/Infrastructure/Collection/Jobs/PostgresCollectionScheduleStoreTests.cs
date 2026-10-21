using System.Security.Cryptography;
using System.Text;
using CivicLens.Application.Collection;
using CivicLens.Application.Collection.Jobs;
using CivicLens.Collection.Contracts;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Collection.Jobs;
using CivicLens.Tests.Application.Collection.Bills;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.Collection.Jobs;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class PostgresCollectionScheduleStoreTests(PostgresCollection postgres) : IAsyncLifetime
{
    private string connectionString = null!;
    private PostgresCollectionJobStore store = null!;

    public async Task InitializeAsync()
    {
        connectionString = await postgres.CreateMigratedDatabaseAsync();
        store = PostgresCollectionJobStore.FromConnectionString(connectionString);
    }

    public async Task DisposeAsync()
    {
        await postgres.DropDatabaseAsync(connectionString);
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
    public async Task EverySourceScheduleReferencesOneSharedRevisionInsteadOfCopyingIt()
    {
        var configuration = Configuration();
        var first = configuration.Sources[0];
        configuration = configuration with { Sources = [first, first with { Id = "second", Url = "https://example.test/second" }] };

        await store.SynchronizeAsync(configuration, default);
        await store.SynchronizeAsync(configuration, default);
        await SetDueAsync("senate", DateTimeOffset.UtcNow.AddMinutes(-5).UtcTicks);
        await SetDueAsync("second", DateTimeOffset.UtcNow.AddMinutes(-5).UtcTicks);

        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM collection_configuration_revisions"));
        Assert.Equal(1L, await ScalarAsync("SELECT count(DISTINCT configuration_revision_id) FROM collection_source_schedules"));
        Assert.Equal(2, await store.AdvanceDueAsync(10, default));
        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM collection_configuration_revisions"));
    }

    [Fact]
    public async Task FreshStartSpreadsFirstDueTimesInsteadOfBurstingAndRestartKeepsThem()
    {
        var template = Configuration(intervalSeconds: 21600).Sources[0];
        var sources = Enumerable.Range(0, 30).Select(index => template with
        {
            Id = $"member-{index}",
            Url = $"https://member{index}.example.test/about",
            AllowedOrigin = $"https://member{index}.example.test"
        }).ToArray();
        var configuration = Configuration() with { Sources = sources };
        var before = DateTimeOffset.UtcNow;

        await store.SynchronizeAsync(configuration, default);

        var dues = new Dictionary<string, long>();
        foreach (var source in sources) dues[source.Id] = await ReadNextDueAsync(source.Id);
        foreach (var source in sources)
            Assert.InRange(dues[source.Id], before.Add(source.InitialDueOffset()).UtcTicks - TimeSpan.TicksPerSecond * 5,
                DateTimeOffset.UtcNow.Add(source.InitialDueOffset()).UtcTicks + TimeSpan.TicksPerSecond * 5);
        Assert.True(dues.Values.Distinct().Count() > 20);
        Assert.True(dues.Values.Max() <= DateTimeOffset.UtcNow.AddHours(1).UtcTicks);

        await store.SynchronizeAsync(configuration, default);
        foreach (var source in sources) Assert.Equal(dues[source.Id], await ReadNextDueAsync(source.Id));
    }

    private async Task<object?> ScalarAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return await command.ExecuteScalarAsync();
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
        await SynchronizeDueAsync(Configuration(intervalSeconds: 60));
        var admitted = await Task.WhenAll(store.AdvanceDueAsync(10, default), store.AdvanceDueAsync(10, default));

        Assert.Equal(1, admitted.Sum());
        Assert.Single(await store.ListAsync(10, default));
    }

    [Fact]
    public async Task ActiveManualRootJobConsumesDueSlotWithoutScheduledDuplicate()
    {
        var configuration = Configuration(intervalSeconds: 60);
        await SynchronizeDueAsync(configuration);
        var configured = new ConfiguredCollectionSource(configuration, "senate");
        await store.EnqueueAsync(configured, "manual-request", default);

        Assert.Equal(1, await store.AdvanceDueAsync(10, default));
        Assert.Single(await store.ListAsync(10, default));
        Assert.True(await ReadNextDueAsync("senate") > DateTimeOffset.UtcNow.UtcTicks);
    }

    // A bill list check reads the cursor, so only the scheduler may admit it. A hand check just makes the schedule due.
    [Fact]
    public async Task ACheckRequestForABillListSourceMakesItsScheduleDueAndAdmitsNoJob()
    {
        var configuration = CongressBillSourceConfigurationTests.Configuration();
        await store.SynchronizeAsync(configuration, default);
        await SetDueAsync("congress-bills", DateTimeOffset.UtcNow.AddDays(3).UtcTicks);
        var source = new ConfiguredCollectionSource(configuration, "congress-bills");

        var job = await store.RequestCheckAsync(source, "hand-check", default);

        Assert.Null(job);
        Assert.Empty(await store.ListAsync(10, default));
        Assert.True(await ReadNextDueAsync("congress-bills") <= DateTimeOffset.UtcNow.UtcTicks);
        Assert.Equal(1, await store.AdvanceDueAsync(10, default));
        var scheduled = Assert.Single(await store.ListAsync(10, default));
        Assert.Equal(ApiResource.BillList, scheduled.Definition.ApiResource);
    }

    [Fact]
    public async Task ACheckRequestForADisabledOrUnscheduledSourceFailsLoudly()
    {
        var configuration = CongressBillSourceConfigurationTests.Configuration();
        var source = new ConfiguredCollectionSource(configuration, "congress-bills");
        await Assert.ThrowsAsync<ArgumentException>(() => store.RequestCheckAsync(source, "no-schedule", default));

        var disabled = configuration with { Sources = [configuration.Sources[0] with { Enabled = false }] };
        await store.SynchronizeAsync(disabled, default);
        await Assert.ThrowsAsync<ArgumentException>(() => store.RequestCheckAsync(source, "disabled", default));
    }

    [Fact]
    public async Task ACheckRequestFailsWithoutActiveCoverageOrWithAnActiveRootJob()
    {
        var configuration = CongressBillSourceConfigurationTests.Configuration();
        var future = configuration with
        {
            Sources = [configuration.Sources[0] with { BackfillFrom = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30) }]
        };
        await store.SynchronizeAsync(future, default);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.RequestCheckAsync(new ConfiguredCollectionSource(future, "congress-bills"), "no-coverage", default));

        await store.SynchronizeAsync(configuration, default);
        await SetDueAsync("congress-bills", 0);
        Assert.Equal(1, await store.AdvanceDueAsync(10, default));
        var source = new ConfiguredCollectionSource(configuration, "congress-bills");
        await Assert.ThrowsAsync<ArgumentException>(() => store.RequestCheckAsync(source, "active-root", default));
    }

    [Fact]
    public async Task ACheckRequestForAPageSourceEnqueuesAJobWithItsKey()
    {
        var configuration = Configuration();
        await store.SynchronizeAsync(configuration, default);
        var source = new ConfiguredCollectionSource(configuration, "senate");

        var first = await store.RequestCheckAsync(source, "hand-check", default);
        var repeated = await store.RequestCheckAsync(source, "hand-check", default);

        Assert.Equal(first!.JobId, repeated!.JobId);
    }

    // An article found by discovery is not a check of its source, so it must not use the due slot of the source.
    [Fact]
    public async Task ActiveArticleJobNeverCountsAsAnActiveRootJob()
    {
        var configuration = Configuration(intervalSeconds: 60, mode: CollectionMode.Html);
        await SynchronizeDueAsync(configuration);
        var source = CollectionJobDefinition.FromConfiguration(configuration, "senate");
        await store.EnqueueAsync(source with { Mode = CollectionMode.Page, Url = "https://example.test/senate/article" },
            "article", default);

        Assert.Equal(1, await store.AdvanceDueAsync(10, default));
        Assert.Equal(2, (await store.ListAsync(10, default)).Count);
    }

    // Root status is fixed when the job is written. A root job at an earlier configured URL therefore still holds the slot.
    [Fact]
    public async Task ActiveRootJobAtAnEarlierConfiguredUrlStillConsumesTheDueSlot()
    {
        await store.EnqueueAsync(new ConfiguredCollectionSource(Configuration(), "senate"), "manual-request", default);
        await SynchronizeDueAsync(Configuration(intervalSeconds: 60, url: "https://example.test/senate-moved"));

        Assert.Equal(1, await store.AdvanceDueAsync(10, default));
        Assert.Single(await store.ListAsync(10, default));
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
        await SynchronizeDueAsync(Configuration(coverageStartsOn: DateOnly.MaxValue));

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
            INSERT INTO collection_configuration_revisions (revision_id, json, created_at) VALUES (@revision, '{', 0);
            INSERT INTO collection_source_schedules (source_id, configuration_revision_id,
                interval_seconds, next_due_utc_ticks, enabled)
            VALUES ('zz-invalid', @revision, 3600, @due, TRUE)
            """, ("revision", Hash("{")), ("due", DateTimeOffset.UtcNow.AddMinutes(-1).UtcTicks));

        await Assert.ThrowsAsync<InvalidDataException>(() => store.AdvanceDueAsync(10, default));
        Assert.Empty(await store.ListAsync(10, default));
        Assert.True(await ReadNextDueAsync("senate") < DateTimeOffset.UtcNow.UtcTicks);
    }

    [Fact]
    public async Task SkippedDueSlotsDoNotStrandEligibleSourcesBeyondFirstBatch()
    {
        var configuration = Configuration(coverageStartsOn: DateOnly.MaxValue);
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
        await SynchronizeDueAsync(configuration);
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
        bool enabled = true, bool withSource = true, DateOnly? coverageStartsOn = null,
        CollectionMode mode = CollectionMode.Page, string url = "https://example.test/senate")
    {
        var source = new WatchedSourceConfiguration
        {
            Id = "senate",
            Coverage = [new SourceCoverageConfiguration
            {
                PersonId = "person",
                StartsOn = coverageStartsOn ?? DateOnly.MinValue
            }],
            Url = url,
            Mode = mode,
            AllowedOrigin = "https://example.test",
            AllowedPathPrefix = "/",
            Enabled = enabled,
            CheckIntervalSeconds = intervalSeconds == WatchedSourceConfiguration.DefaultCheckIntervalSeconds
                ? null : intervalSeconds
        };
        return new CollectionConfiguration
        {
            People = [new PersonConfiguration { Id = "person", Name = "Person" }],
            Sources = withSource ? [source] : []
        };
    }

    private async Task SynchronizeDueAsync(CollectionConfiguration configuration)
    {
        await store.SynchronizeAsync(configuration, default);
        await ExecuteAsync("UPDATE collection_source_schedules SET next_due_utc_ticks = @due",
            ("due", DateTimeOffset.UtcNow.AddMinutes(-1).UtcTicks));
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
