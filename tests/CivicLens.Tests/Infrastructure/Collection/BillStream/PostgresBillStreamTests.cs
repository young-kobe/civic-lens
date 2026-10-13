using System.Text.Json;
using CivicLens.Application.Collection;
using CivicLens.Application.Configuration;
using CivicLens.Application.Collection.Bills;
using CivicLens.Application.Collection.Discovery;
using CivicLens.Application.Collection.Jobs;
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
    private const string ListJobKeyPrefix = "test-list/";
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
    public async Task ListPageAdmitsRecentBillsOnlyOnceAndLeavesTheCursorUntilAPassAdmitsNothingNew()
    {
        var source = Source();
        var window = new BillStreamWindow(Backfill, Backfill.AddDays(1));
        var next = window.RequestUrl(SourceUrl) + "&offset=250";
        var first = new CongressBillListPage([Bill(119, 1), Bill(118, 2), Bill(119, 1)], next, StartsPass: true);

        var result = await AdmitAsync(source, "list-one", window, first);

        Assert.Equal([$"https://api.congress.gov/v3/bill/119/hr/1?format=json", next], result.Jobs.Select(job => job.Url));
        Assert.Equal(0, result.DuplicateCount);
        Assert.Null(await ReadCursorAsync());

        var replay = await AdmitAsync(source, "list-one", window, first);
        Assert.Empty(replay.Jobs);
        Assert.Equal(2, replay.DuplicateCount);

        var last = await AdmitAsync(source, "list-two", window,
            new CongressBillListPage([Bill(119, 1), Bill(119, 3)], null, StartsPass: false));
        Assert.Equal(["https://api.congress.gov/v3/bill/119/hr/3?format=json"], last.Jobs.Select(job => job.Url));
        Assert.Equal(1, last.DuplicateCount);
        Assert.Null(await ReadCursorAsync());
        Assert.Equal(3, (await AdmittedJobsAsync()).Count);

        await AdmitAsync(source, "repeat-one", window, first);
        Assert.Null(await ReadCursorAsync());
        await AdmitAsync(source, "repeat-two", window, new CongressBillListPage([Bill(119, 3)], null, StartsPass: false));
        Assert.Equal(window.LastDay, await ReadCursorAsync());
    }

    [Fact]
    public async Task ABillAdmittedOnAnEarlierPageKeepsTheWindowOpenAfterAQuietLastPage()
    {
        var window = new BillStreamWindow(Backfill, Backfill);
        var next = window.RequestUrl(SourceUrl) + "&offset=250";

        await AdmitAsync(Source(), "page-one", window, new CongressBillListPage([Bill(119, 1)], next, StartsPass: true));
        await AdmitAsync(Source(), "page-two", window, new CongressBillListPage([], null, StartsPass: false));

        Assert.Null(await ReadCursorAsync());
        Assert.Equal(0, await ScalarAsync("SELECT pass_admitted_bills FROM bill_stream_cursors"));
    }

    [Fact]
    public async Task ALastPageProcessedTwiceDoesNotSettleAWindowWhosePassAdmittedBills()
    {
        var window = new BillStreamWindow(Backfill, Backfill);
        var next = window.RequestUrl(SourceUrl) + "&offset=250";
        await AdmitAsync(Source(), "page-one", window, new CongressBillListPage([Bill(119, 1)], next, StartsPass: true));
        var last = new CongressBillListPage([], null, StartsPass: false);
        await AdmitAsync(Source(), "page-two", window, last);

        await AdmitAsync(Source(), "page-two", window, last);

        Assert.Null(await ReadCursorAsync());
    }

    [Fact]
    public async Task AStoppedPassLeavesNoCountThatKeepsTheNextQuietPassFromSettling()
    {
        var window = new BillStreamWindow(Backfill, Backfill);
        await AdmitAsync(Source(), "stopped", window,
            new CongressBillListPage([Bill(119, 1)], window.RequestUrl(SourceUrl) + "&offset=250", StartsPass: true));

        await AdmitAsync(Source(), "quiet", window, new CongressBillListPage([Bill(119, 1)], null, StartsPass: true));

        Assert.Equal(window.LastDay, await ReadCursorAsync());
    }

    [Fact]
    public async Task AWindowThatAdmittedBillsIsListedAgainAtOnceEvenWhenCaughtUp()
    {
        await store.SynchronizeAsync(Configuration(), default);
        await ExecuteAsync(connectionString, $"UPDATE collection_source_schedules SET next_due_utc_ticks = {DateTimeOffset.UtcNow.AddDays(1).UtcTicks}");
        var caughtUp = new BillStreamWindow(Backfill, BillStreamWindow.LastClosedDay(DateTimeOffset.UtcNow));

        await AdmitAsync(Source(), "caught-up", caughtUp, new CongressBillListPage([Bill(119, 1)], null, StartsPass: true));

        Assert.True(await ReadNextDueAsync() <= DateTimeOffset.UtcNow.UtcTicks);
        Assert.Null(await ReadCursorAsync());
    }

    [Fact]
    public async Task AnUnfinishedNextPageJobBlocksANewCheckOfTheSource()
    {
        await store.SynchronizeAsync(Configuration(), default);
        var window = new BillStreamWindow(Backfill, Backfill);
        await AdmitAsync(Source(), "first-pass", window,
            new CongressBillListPage([Bill(119, 1)], window.RequestUrl(SourceUrl) + "&offset=250", StartsPass: true));
        await ExecuteAsync(connectionString, "UPDATE collection_source_schedules SET next_due_utc_ticks = 0");

        await store.AdvanceDueAsync(10, default);

        Assert.DoesNotContain(await store.ListAsync(10, default), job => job.IdempotencyKey.StartsWith("scheduled-check/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ABillWithANewUpdateDateIsAdmittedAgain()
    {
        var window = new BillStreamWindow(Backfill, Backfill);

        await AdmitAsync(Source(), "one", window, new CongressBillListPage([Bill(119, 1)], null, StartsPass: true));
        var again = await AdmitAsync(Source(), "two", window,
            new CongressBillListPage([Bill(119, 1) with { UpdateDate = Backfill.AddDays(1) }], null, StartsPass: true));

        Assert.Single(again.Jobs);
        Assert.Equal(2, (await AdmittedJobsAsync()).Count);
    }

    [Fact]
    public async Task AdmittedChildJobsCarryTheCredentialAndNeverTheDiscoveryAdmissionKeyPrefix()
    {
        var window = new BillStreamWindow(Backfill, Backfill);

        await AdmitAsync(Source(), "one", window, new CongressBillListPage([Bill(119, 1)], window.RequestUrl(SourceUrl) + "&offset=250", StartsPass: true));

        var jobs = await AdmittedJobsAsync();
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

        await AdmitAsync(Source(), "later", later, new CongressBillListPage([], null, StartsPass: true));
        await AdmitAsync(Source(), "earlier", earlier, new CongressBillListPage([], null, StartsPass: true));

        Assert.Equal(later.LastDay, await ReadCursorAsync());
    }

    [Fact]
    public async Task FinishingAWindowThatLeavesClosedDaysBehindMakesTheNextCheckDueAtOnce()
    {
        await store.SynchronizeAsync(Configuration(), default);
        await ExecuteAsync(connectionString, $"UPDATE collection_source_schedules SET next_due_utc_ticks = {DateTimeOffset.UtcNow.AddDays(1).UtcTicks}");
        var behind = new BillStreamWindow(Backfill, Backfill.AddDays(6));

        await AdmitAsync(Source(), "behind", behind, new CongressBillListPage([], null, StartsPass: true));

        Assert.True(await ReadNextDueAsync() <= DateTimeOffset.UtcNow.UtcTicks);
    }

    [Fact]
    public async Task FinishingTheLastClosedDayKeepsTheDailyCadence()
    {
        await store.SynchronizeAsync(Configuration(), default);
        var due = DateTimeOffset.UtcNow.AddDays(1).UtcTicks;
        await ExecuteAsync(connectionString, $"UPDATE collection_source_schedules SET next_due_utc_ticks = {due}");
        var caughtUp = new BillStreamWindow(Backfill, BillStreamWindow.LastClosedDay(DateTimeOffset.UtcNow));

        await AdmitAsync(Source(), "caught-up", caughtUp, new CongressBillListPage([], null, StartsPass: true));

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

        await Assert.ThrowsAnyAsync<Exception>(() => AdmitAsync(Source(), "failing", window,
            new CongressBillListPage([Bill(119, 1), Bill(119, 2), Bill(119, 3)], null, StartsPass: true)));

        Assert.Empty(await AdmittedJobsAsync());
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
            window.RequestUrl(SourceUrl) + "&offset=250", StartsPass: true);

        var result = await AdmitAsync(new ConfiguredCollectionSource(pilot, SourceId, Backfill.AddDays(400)),
            "full", window, page);

        Assert.Equal(BillStreamWindow.PageLimit + 1, result.AdmittedCount);
        Assert.Equal(0, result.DeferredCount);
    }

    [Fact]
    public async Task AFullPageStoresItsConfigurationRevisionOnceInsteadOfOncePerJob()
    {
        var window = new BillStreamWindow(Backfill, Backfill);
        var page = new CongressBillListPage([.. Enumerable.Range(1, BillStreamWindow.PageLimit).Select(number => Bill(119, number))],
            window.RequestUrl(SourceUrl) + "&offset=250", StartsPass: true);

        var result = await AdmitAsync(Source(), "full", window, page);

        Assert.Equal(BillStreamWindow.PageLimit + 1, result.AdmittedCount);
        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM collection_configuration_revisions"));
        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM collection_jobs WHERE definition_json::jsonb -> 'configurationRevision' IS NOT NULL"));
        var expected = CollectionConfigurationRevision.Create(Configuration());
        Assert.All(await AdmittedJobsAsync(), job => Assert.Equal(expected, job.Definition.ConfigurationRevision));
    }

    private async Task<DiscoveryAdmissionResult> AdmitAsync(ConfiguredCollectionSource source, string listJob,
        BillStreamWindow window, CongressBillListPage page)
    {
        var definition = source.CreateApiJobDefinition(ApiResource.BillList, window.RequestUrl(SourceUrl), Backfill);
        var job = await store.EnqueueAsync(definition, ListJobKeyPrefix + listJob, default);
        await ExecuteAsync(connectionString, $"UPDATE collection_jobs SET state = 'Succeeded' WHERE job_id = '{job.JobId}'");
        return await store.AdmitListPageAsync(source, job.JobId, window, page, default);
    }

    private async Task<IReadOnlyList<CollectionJobRecord>> AdmittedJobsAsync() =>
        [.. (await store.ListAsync(100, default)).Where(job => !job.IdempotencyKey.StartsWith(ListJobKeyPrefix, StringComparison.Ordinal))];

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
