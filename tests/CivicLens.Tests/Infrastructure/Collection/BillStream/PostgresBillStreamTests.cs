using System.Text.Json;
using CivicLens.Application.Collection;
using CivicLens.Application.Configuration;
using CivicLens.Application.Collection.Bills;
using CivicLens.Application.Collection.Discovery;
using CivicLens.Collection.Contracts;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Collection.Jobs;
using CivicLens.Tests.Application.Collection.Bills;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.Collection.BillStream;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class PostgresBillStreamTests(PostgresCollection postgres) : IAsyncLifetime
{
    private const string SourceId = "congress-bills";
    private const string SourceUrl = "https://api.congress.gov/v3/bill";
    private static readonly DateOnly Backfill = new(2025, 1, 3);
    private readonly string schema = "bill_stream_" + Guid.NewGuid().ToString("N");
    private string connectionString = null!;
    private PostgresCollectionJobStore store = null!;

    public async Task InitializeAsync()
    {
        await ExecuteAsync(postgres.ConnectionString, $"CREATE SCHEMA {schema}");
        connectionString = new NpgsqlConnectionStringBuilder(postgres.ConnectionString) { SearchPath = schema }.ConnectionString;
        store = PostgresCollectionJobStore.FromConnectionString(connectionString);
        await PostgresCollectionAttemptStore.FromConnectionString(connectionString).MigrateAsync();
    }

    public async Task DisposeAsync() => await ExecuteAsync(postgres.ConnectionString, $"DROP SCHEMA IF EXISTS {schema} CASCADE");

    [Fact]
    public async Task FirstDueCheckRequestsTheBackfillWindowWithOneDayOfOverlap()
    {
        await store.SynchronizeAsync(Configuration(), default);

        Assert.Equal(1, await store.AdvanceDueAsync(10, default));

        var job = Assert.Single(await store.ListAsync(10, default));
        var window = new BillStreamWindow(Backfill, Backfill.AddDays(BillStreamWindow.MaximumDays - 1));
        Assert.Equal(window.RequestUrl(SourceUrl), job.Definition.Url);
        Assert.Equal(CollectionMode.Api, job.Definition.Mode);
        Assert.Equal(CollectionCredentials.CongressGov, job.Definition.Credential);
    }

    [Fact]
    public async Task DueCheckWithoutAClosedDayCreatesNoJobAndWaitsOneInterval()
    {
        await store.SynchronizeAsync(Configuration(), default);
        await SetCursorAsync(BillStreamWindow.LastClosedDay(DateTimeOffset.UtcNow));

        Assert.Equal(1, await store.AdvanceDueAsync(10, default));

        Assert.Empty(await store.ListAsync(10, default));
        Assert.True(await ReadNextDueAsync() > DateTimeOffset.UtcNow.UtcTicks);
    }

    [Fact]
    public async Task DueCheckStartsTheDayAfterTheCursorAndStopsAtTheLastClosedDay()
    {
        await store.SynchronizeAsync(Configuration(), default);
        var lastClosed = BillStreamWindow.LastClosedDay(DateTimeOffset.UtcNow);
        await SetCursorAsync(lastClosed.AddDays(-2));

        await store.AdvanceDueAsync(10, default);

        var job = Assert.Single(await store.ListAsync(10, default));
        Assert.Equal(new BillStreamWindow(lastClosed.AddDays(-1), lastClosed), BillStreamWindow.TryRead(job.Definition.Url));
    }

    [Fact]
    public async Task ASourceWithAnUnfinishedPageJobIsNotCheckedAgain()
    {
        await store.SynchronizeAsync(Configuration(), default);
        await store.AdvanceDueAsync(10, default);
        await ExecuteAsync(connectionString, "UPDATE collection_source_schedules SET next_due_utc_ticks = 0");

        await store.AdvanceDueAsync(10, default);

        Assert.Single(await store.ListAsync(10, default));
    }

    [Fact]
    public async Task ListPageAdmitsRecentBillsOnlyOnceAndLeavesTheCursorUntilTheLastPage()
    {
        var source = Source();
        var window = new BillStreamWindow(Backfill, Backfill.AddDays(1));
        var next = window.RequestUrl(SourceUrl) + "&offset=250";
        var first = new CongressBillListPage([Bill(119, 1), Bill(118, 2), Bill(119, 1)], next);

        var result = await store.AdmitListPageAsync(source, "list-one", window, first, default);

        Assert.Equal([$"https://api.congress.gov/v3/bill/119/hr/1?format=json", next], result.Jobs.Select(job => job.Url));
        Assert.Equal(0, result.DuplicateCount);
        Assert.Null(await ReadCursorAsync());

        var replay = await store.AdmitListPageAsync(source, "list-one", window, first, default);
        Assert.Empty(replay.Jobs);
        Assert.Equal(2, replay.DuplicateCount);

        var last = await store.AdmitListPageAsync(source, "list-two", window,
            new CongressBillListPage([Bill(119, 1), Bill(119, 3)], null), default);
        Assert.Equal(["https://api.congress.gov/v3/bill/119/hr/3?format=json"], last.Jobs.Select(job => job.Url));
        Assert.Equal(1, last.DuplicateCount);
        Assert.Equal(window.LastDay, await ReadCursorAsync());
        Assert.Equal(3, (await store.ListAsync(10, default)).Count);
    }

    [Fact]
    public async Task ABillWithANewUpdateDateIsAdmittedAgain()
    {
        var window = new BillStreamWindow(Backfill, Backfill);

        await store.AdmitListPageAsync(Source(), "one", window, new CongressBillListPage([Bill(119, 1)], null), default);
        var again = await store.AdmitListPageAsync(Source(), "two", window,
            new CongressBillListPage([Bill(119, 1) with { UpdateDate = Backfill.AddDays(1) }], null), default);

        Assert.Single(again.Jobs);
        Assert.Equal(2, (await store.ListAsync(10, default)).Count);
    }

    [Fact]
    public async Task AdmittedChildJobsCarryTheCredentialAndNeverTheDiscoveryAdmissionKeyPrefix()
    {
        var window = new BillStreamWindow(Backfill, Backfill);

        await store.AdmitListPageAsync(Source(), "one", window, new CongressBillListPage([Bill(119, 1)], window.RequestUrl(SourceUrl) + "&offset=250"), default);

        var jobs = await store.ListAsync(10, default);
        Assert.All(jobs, job =>
        {
            Assert.StartsWith(BillStreamJobKeys.Prefix, job.IdempotencyKey, StringComparison.Ordinal);
            Assert.Equal(CollectionCredentials.CongressGov, job.Definition.Credential);
            Assert.Equal(CollectionMode.Api, job.Definition.Mode);
        });
    }

    [Fact]
    public async Task CursorNeverMovesBackward()
    {
        var later = new BillStreamWindow(Backfill.AddDays(10), Backfill.AddDays(12));
        var earlier = new BillStreamWindow(Backfill, Backfill.AddDays(2));

        await store.AdmitListPageAsync(Source(), "later", later, new CongressBillListPage([], null), default);
        await store.AdmitListPageAsync(Source(), "earlier", earlier, new CongressBillListPage([], null), default);

        Assert.Equal(later.LastDay, await ReadCursorAsync());
    }

    [Fact]
    public async Task FinishingAWindowThatLeavesClosedDaysBehindMakesTheNextCheckDueAtOnce()
    {
        await store.SynchronizeAsync(Configuration(), default);
        await ExecuteAsync(connectionString, $"UPDATE collection_source_schedules SET next_due_utc_ticks = {DateTimeOffset.UtcNow.AddDays(1).UtcTicks}");
        var behind = new BillStreamWindow(Backfill, Backfill.AddDays(6));

        await store.AdmitListPageAsync(Source(), "behind", behind, new CongressBillListPage([], null), default);

        Assert.True(await ReadNextDueAsync() <= DateTimeOffset.UtcNow.UtcTicks);
    }

    [Fact]
    public async Task FinishingTheLastClosedDayKeepsTheDailyCadence()
    {
        await store.SynchronizeAsync(Configuration(), default);
        var due = DateTimeOffset.UtcNow.AddDays(1).UtcTicks;
        await ExecuteAsync(connectionString, $"UPDATE collection_source_schedules SET next_due_utc_ticks = {due}");
        var caughtUp = new BillStreamWindow(Backfill, BillStreamWindow.LastClosedDay(DateTimeOffset.UtcNow));

        await store.AdmitListPageAsync(Source(), "caught-up", caughtUp, new CongressBillListPage([], null), default);

        Assert.Equal(due, await ReadNextDueAsync());
    }

    [Fact]
    public async Task AFailureWhileAdmittingLeavesNoJobsAndNoCursorMove()
    {
        await ExecuteAsync(connectionString, """
            CREATE FUNCTION reject_bill_three() RETURNS trigger AS $$
            BEGIN RAISE EXCEPTION 'rejected'; END $$ LANGUAGE plpgsql;
            CREATE TRIGGER trg_reject_bill_three BEFORE INSERT ON collection_jobs
                FOR EACH ROW WHEN (NEW.idempotency_key LIKE '%/hr/3/%') EXECUTE FUNCTION reject_bill_three();
            """);
        var window = new BillStreamWindow(Backfill, Backfill);

        await Assert.ThrowsAnyAsync<Exception>(() => store.AdmitListPageAsync(Source(), "failing", window,
            new CongressBillListPage([Bill(119, 1), Bill(119, 2), Bill(119, 3)], null), default));

        Assert.Empty(await store.ListAsync(10, default));
        Assert.Null(await ReadCursorAsync());
    }

    [Fact]
    public async Task PilotSourceBudgetsAdmitAFullPageAndItsNextPage()
    {
        var pilot = JsonSerializer.Deserialize<CollectionConfiguration>(
            File.ReadAllText(Path.Combine(RepositoryRoot(), "config", "federal-pilot.json")), ConfigurationJson.Options)!;
        pilot = pilot with { Sources = [.. pilot.Sources.Where(source => source.Id == SourceId).Select(source => source with { Enabled = true })] };
        var window = new BillStreamWindow(Backfill, Backfill);
        var page = new CongressBillListPage([.. Enumerable.Range(1, BillStreamWindow.PageLimit).Select(number => Bill(119, number))],
            window.RequestUrl(SourceUrl) + "&offset=250");

        var result = await store.AdmitListPageAsync(new ConfiguredCollectionSource(pilot, SourceId, Backfill.AddDays(400)),
            "full", window, page, default);

        Assert.Equal(BillStreamWindow.PageLimit + 1, result.AdmittedCount);
        Assert.Equal(0, result.DeferredCount);
    }

    [Fact]
    public async Task AFullPageStoresItsConfigurationRevisionOnceInsteadOfOncePerJob()
    {
        var window = new BillStreamWindow(Backfill, Backfill);
        var page = new CongressBillListPage([.. Enumerable.Range(1, BillStreamWindow.PageLimit).Select(number => Bill(119, number))],
            window.RequestUrl(SourceUrl) + "&offset=250");

        var result = await store.AdmitListPageAsync(Source(), "full", window, page, default);

        Assert.Equal(BillStreamWindow.PageLimit + 1, result.AdmittedCount);
        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM collection_configuration_revisions"));
        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM collection_jobs WHERE definition_json::jsonb -> 'configurationRevision' IS NOT NULL"));
        var expected = CollectionConfigurationRevision.Create(Configuration());
        Assert.All(await store.ListAsync(100, default), job => Assert.Equal(expected, job.Definition.ConfigurationRevision));
    }

    private async Task<object?> ScalarAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return await command.ExecuteScalarAsync();
    }

    private static ConfiguredCollectionSource Source() => new(Configuration(), SourceId, Backfill.AddDays(400));

    private static CollectionConfiguration Configuration() => CongressBillSourceConfigurationTests.Configuration();

    private static CongressBillListItem Bill(int congress, int number) => new(congress, "hr", number, Backfill);

    private async Task SetCursorAsync(DateOnly day) => await ExecuteAsync(connectionString, $"""
        INSERT INTO bill_stream_cursors (source_id, last_processed_day, updated_at_utc_ticks)
        VALUES ('{SourceId}', '{day:yyyy-MM-dd}', 0)
        """);

    private async Task<DateOnly?> ReadCursorAsync()
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT last_processed_day FROM bill_stream_cursors", connection);
        return await command.ExecuteScalarAsync() is DateOnly day ? day : null;
    }

    private async Task<long> ReadNextDueAsync()
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT next_due_utc_ticks FROM collection_source_schedules", connection);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CivicLens.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Cannot locate CivicLens.slnx.");
    }
}
