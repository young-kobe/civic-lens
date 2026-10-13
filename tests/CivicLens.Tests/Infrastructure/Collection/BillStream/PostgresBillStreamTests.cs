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
using CivicLens.Tests.Fixtures;
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
        var page = new CongressBillListPage([.. Enumerable.Range(1, BillPageQuery.PageLimit).Select(number => Bill(119, number))],
            window.RequestUrl(SourceUrl) + "&offset=250", StartsPass: true);

        var result = await AdmitAsync(new ConfiguredCollectionSource(pilot, SourceId, Backfill.AddDays(400)),
            "full", window, page);

        Assert.Equal(BillPageQuery.PageLimit + 1, result.AdmittedCount);
        Assert.Equal(0, result.DeferredCount);
    }

    [Fact]
    public async Task AFullPageStoresItsConfigurationRevisionOnceInsteadOfOncePerJob()
    {
        var window = new BillStreamWindow(Backfill, Backfill);
        var page = new CongressBillListPage([.. Enumerable.Range(1, BillPageQuery.PageLimit).Select(number => Bill(119, number))],
            window.RequestUrl(SourceUrl) + "&offset=250", StartsPass: true);

        var result = await AdmitAsync(Source(), "full", window, page);

        Assert.Equal(BillPageQuery.PageLimit + 1, result.AdmittedCount);
        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM collection_configuration_revisions"));
        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM collection_jobs WHERE definition_json::jsonb -> 'configurationRevision' IS NOT NULL"));
        var expected = CollectionConfigurationRevision.Create(Configuration());
        Assert.All(await AdmittedJobsAsync(), job => Assert.Equal(expected, job.Definition.ConfigurationRevision));
    }

    [Fact]
    public async Task APassedDetailAdmitsCosponsorAndActionsFirstPagesLinkedToItAndStoresTheGate()
    {
        var detail = await EnqueueDetailAsync("detail");
        var updatedAt = new DateTimeOffset(2025, 3, 3, 12, 30, 5, TimeSpan.Zero);

        var result = await AdmitDetailAsync(detail, DetailBill(updatedAt, cosponsors: 3),
            BillGateOutcome.Passed(BillGateReason.TrackedPolicyArea));

        Assert.Equal(
            ["https://api.congress.gov/v3/bill/119/hr/7/cosponsors?format=json&limit=250",
             "https://api.congress.gov/v3/bill/119/hr/7/actions?format=json&limit=250"],
            result.Jobs.Select(job => job.Url));
        Assert.Equal(
            ["bill-stream/congress-bills/actions/119/hr/7/2025-03-03T12:30:05Z",
             "bill-stream/congress-bills/cosponsors/119/hr/7/2025-03-03T12:30:05Z"],
            (await LinksAsync()).Select(link => link.Key));
        Assert.All(await LinksAsync(), link => Assert.Equal(detail, link.DetailJobId));
        Assert.Equal(("Passed", "TrackedPolicyArea"), await GateOfAsync(detail));
        Assert.Equal(updatedAt.UtcTicks, await ScalarAsync("SELECT update_date_utc_ticks FROM bill_gates"));
        Assert.Equal(BillGateFixtures.Revision().Id, (string?)await ScalarAsync("SELECT issue_revision_id FROM bill_gates"));
        Assert.Equal(BillGateFixtures.TrackedPolicyArea, (string?)await ScalarAsync("SELECT policy_area FROM bill_gates"));
        Assert.NotNull(await ScalarAsync("SELECT decided_at_utc_ticks FROM bill_gates"));
    }

    [Fact]
    public async Task ADetailWithoutCosponsorsAdmitsNoCosponsorPageAndANotRelevantBillAdmitsNoActions()
    {
        var detail = await EnqueueDetailAsync("detail");

        var result = await AdmitDetailAsync(detail, DetailBill(UpdatedAt, cosponsors: 0),
            BillGateOutcome.NotRelevant(BillGateReason.UntrackedPolicyArea));

        Assert.Empty(result.Jobs);
        Assert.Equal(("NotRelevant", "UntrackedPolicyArea"), await GateOfAsync(detail));
        Assert.Empty(await LinksAsync());
    }

    [Fact]
    public async Task APendingDetailAdmitsOnlyTheCosponsorPageAndLeavesTheGateUndecided()
    {
        var detail = await EnqueueDetailAsync("detail");

        var result = await AdmitDetailAsync(detail, DetailBill(UpdatedAt, cosponsors: 2), BillGateOutcome.Pending);

        Assert.Single(result.Jobs);
        Assert.Contains("/cosponsors?", result.Jobs.Single().Url);
        Assert.Equal(("Pending", (string?)null), await GateOfAsync(detail));
        Assert.Equal(DBNull.Value, await ScalarAsync("SELECT decided_at_utc_ticks FROM bill_gates"));
        Assert.Single(await LinksAsync());
    }

    [Fact]
    public async Task ReplayingADetailJobChangesNothing()
    {
        var detail = await EnqueueDetailAsync("detail");
        var bill = DetailBill(UpdatedAt, cosponsors: 2);
        await AdmitDetailAsync(detail, bill, BillGateOutcome.Passed(BillGateReason.CoveredSponsor));
        var before = await CountsAsync();

        var replay = await AdmitDetailAsync(detail, bill, BillGateOutcome.Passed(BillGateReason.CoveredSponsor));

        Assert.Empty(replay.Jobs);
        Assert.Equal(1, replay.DuplicateCount);
        Assert.Equal(before, await CountsAsync());
    }

    [Fact]
    public async Task ASecondDetailJobForTheSameBillVersionRecordsNothingAndAdmitsNoFollowOns()
    {
        var first = await EnqueueDetailAsync("first");
        var second = await EnqueueDetailAsync("second");
        var bill = DetailBill(UpdatedAt, cosponsors: 2);
        await AdmitDetailAsync(first, bill, BillGateOutcome.Passed(BillGateReason.CoveredSponsor));
        var before = await CountsAsync();

        var duplicate = await AdmitDetailAsync(second, bill, BillGateOutcome.NotRelevant(BillGateReason.NoPolicyArea));

        Assert.Empty(duplicate.Jobs);
        Assert.Equal(before, await CountsAsync());
        Assert.Equal(("Passed", "CoveredSponsor"), await GateOfAsync(first));
        Assert.Equal(0L, await ScalarAsync($"SELECT count(*) FROM bill_gates WHERE detail_job_id = '{second}'"));
    }

    [Fact]
    public async Task ANewUpdateOfTheSameBillGetsItsOwnGateAndFollowOns()
    {
        var first = await EnqueueDetailAsync("first");
        var second = await EnqueueDetailAsync("second");
        await AdmitDetailAsync(first, DetailBill(UpdatedAt, cosponsors: 2), BillGateOutcome.Pending);

        var later = await AdmitDetailAsync(second, DetailBill(UpdatedAt.AddSeconds(1), cosponsors: 2), BillGateOutcome.Pending);

        Assert.Single(later.Jobs);
        Assert.Equal(2L, await ScalarAsync("SELECT count(*) FROM bill_gates"));
    }

    [Fact]
    public async Task NoCoveredCosponsorOnAnyPageNotRelevantsTheBillOnlyAtTheLastPage()
    {
        var detail = await EnqueueDetailAsync("detail");
        var first = await AdmitDetailAsync(detail, DetailBill(UpdatedAt, cosponsors: 400), BillGateOutcome.Pending);
        var firstPage = first.Jobs.Single().JobId;

        var second = await AdmitCosponsorPageAsync(firstPage, Cosponsors(["Z000009"], next: CosponsorsNext(250)));

        Assert.Equal(("Pending", (string?)null), await GateOfAsync(detail));
        var nextJob = second.Jobs.Single();
        Assert.Equal(CosponsorsNext(250), nextJob.Url);
        Assert.Contains((BillStreamJobKeys.NextBillPage(SourceId, ApiResource.BillCosponsors, firstPage), detail),
            (await LinksAsync()).Select(link => (link.Key, link.DetailJobId)));

        var last = await AdmitCosponsorPageAsync(nextJob.JobId, Cosponsors(["Z000009"], next: null));

        Assert.Empty(last.Jobs);
        Assert.Equal(("NotRelevant", "UntrackedPolicyArea"), await GateOfAsync(detail));
        Assert.DoesNotContain(await LinksAsync(), link => link.Key.Contains("/actions/"));
    }

    [Fact]
    public async Task ALastCosponsorPageWithoutAPolicyAreaGivesTheNoPolicyAreaReason()
    {
        var detail = await EnqueueDetailAsync("detail");
        var first = await AdmitDetailAsync(detail, DetailBill(UpdatedAt, cosponsors: 1, policyArea: null), BillGateOutcome.Pending);

        await AdmitCosponsorPageAsync(first.Jobs.Single().JobId, Cosponsors(["Z000009"], next: null));

        Assert.Equal(("NotRelevant", "NoPolicyArea"), await GateOfAsync(detail));
    }

    [Fact]
    public async Task ACoveredCosponsorOnALaterPagePassesTheGateAndAdmitsTheActionsFirstPageOnce()
    {
        var detail = await EnqueueDetailAsync("detail");
        var first = await AdmitDetailAsync(detail, DetailBill(UpdatedAt, cosponsors: 400), BillGateOutcome.Pending);
        var second = await AdmitCosponsorPageAsync(first.Jobs.Single().JobId, Cosponsors(["Z000009"], next: CosponsorsNext(250)));
        var coveredPage = Cosponsors(["A000001"], next: null);

        var passed = await AdmitCosponsorPageAsync(second.Jobs.Single().JobId, coveredPage);

        Assert.Equal(("Passed", "CoveredCosponsor"), await GateOfAsync(detail));
        var actions = Assert.Single(passed.Jobs);
        Assert.Equal("https://api.congress.gov/v3/bill/119/hr/7/actions?format=json&limit=250", actions.Url);
        Assert.Contains(await LinksAsync(), link => link.Key == "bill-stream/congress-bills/actions/119/hr/7/2025-03-03T12:00:00Z" && link.DetailJobId == detail);

        var before = await CountsAsync();
        var replay = await AdmitCosponsorPageAsync(second.Jobs.Single().JobId, coveredPage);
        Assert.Empty(replay.Jobs);
        Assert.Equal(before, await CountsAsync());
        Assert.Equal(("Passed", "CoveredCosponsor"), await GateOfAsync(detail));
    }

    [Fact]
    public async Task ALaterCosponsorPageNeverChangesAGateThatAnEarlierStepDecided()
    {
        var detail = await EnqueueDetailAsync("detail");
        var first = await AdmitDetailAsync(detail, DetailBill(UpdatedAt, cosponsors: 400), BillGateOutcome.Passed(BillGateReason.TrackedPolicyArea));
        var cosponsorPage = first.Jobs.Single(job => job.Url.Contains("/cosponsors?")).JobId;

        await AdmitCosponsorPageAsync(cosponsorPage, Cosponsors(["Z000009"], next: null));

        Assert.Equal(("Passed", "TrackedPolicyArea"), await GateOfAsync(detail));
        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM bill_follow_on_jobs l JOIN collection_jobs j ON j.job_id = l.job_id WHERE j.idempotency_key LIKE '%/actions/%'"));
    }

    [Fact]
    public async Task AWithdrawnCoveredCosponsorDoesNotPassTheGate()
    {
        var detail = await EnqueueDetailAsync("detail");
        var first = await AdmitDetailAsync(detail, DetailBill(UpdatedAt, cosponsors: 1), BillGateOutcome.Pending);
        var withdrawn = new CongressBillCosponsorsPage(
            [new CongressBillCosponsor("A000001", Backfill, false, Backfill.AddDays(1))], null);

        await store.AdmitCosponsorPageAsync(Source(), first.Jobs.Single().JobId, withdrawn, default);

        Assert.Equal(("NotRelevant", "UntrackedPolicyArea"), await GateOfAsync(detail));
    }

    [Fact]
    public async Task CosponsorCoverageIsReadOnTheUtcDayOfTheGateUpdateDate()
    {
        var detail = await EnqueueDetailAsync("detail");
        var beforeBackfill = new DateTimeOffset(Backfill.AddDays(-1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).AddHours(12);
        var first = await AdmitDetailAsync(detail, DetailBill(beforeBackfill, cosponsors: 1), BillGateOutcome.Pending);

        await AdmitCosponsorPageAsync(first.Jobs.Single().JobId, Cosponsors(["A000001"], next: null));

        Assert.Equal(("NotRelevant", "UntrackedPolicyArea"), await GateOfAsync(detail));
    }

    [Fact]
    public async Task ActionPagesChainByTheirParentJobAndEveryPageIsLinkedToTheDetailJob()
    {
        var detail = await EnqueueDetailAsync("detail");
        var first = await AdmitDetailAsync(detail, DetailBill(UpdatedAt, cosponsors: 0), BillGateOutcome.Passed(BillGateReason.CoveredSponsor));
        var actionsJob = first.Jobs.Single().JobId;
        var page = new CongressBillActionsPage([], ActionsNext(250));

        var next = await store.AdmitActionsPageAsync(Source(), actionsJob, page, default);

        Assert.Equal(ActionsNext(250), next.Jobs.Single().Url);
        Assert.Contains((BillStreamJobKeys.NextBillPage(SourceId, ApiResource.BillActions, actionsJob), detail),
            (await LinksAsync()).Select(link => (link.Key, link.DetailJobId)));
        var replay = await store.AdmitActionsPageAsync(Source(), actionsJob, page, default);
        Assert.Empty(replay.Jobs);
        Assert.Equal(1, replay.DuplicateCount);
        var last = await store.AdmitActionsPageAsync(Source(), next.Jobs.Single().JobId, new CongressBillActionsPage([], null), default);
        Assert.Empty(last.Jobs);
    }

    [Fact]
    public async Task APageJobWithoutALinkRowIsInvalidData()
    {
        var orphan = await EnqueueDetailAsync("orphan");

        await Assert.ThrowsAsync<InvalidDataException>(() => store.AdmitCosponsorPageAsync(Source(), orphan, Cosponsors([], null), default));
        await Assert.ThrowsAsync<InvalidDataException>(() => store.AdmitActionsPageAsync(Source(), orphan, new CongressBillActionsPage([], null), default));
    }

    [Fact]
    public async Task AFailureWhileAdmittingFollowOnsLeavesNoGateAndNoLinks()
    {
        await ExecuteAsync(connectionString, """
            CREATE FUNCTION reject_actions() RETURNS trigger AS $$
            BEGIN RAISE EXCEPTION 'rejected'; END $$ LANGUAGE plpgsql;
            CREATE TRIGGER trg_reject_actions BEFORE INSERT ON collection_jobs
                FOR EACH ROW WHEN (NEW.idempotency_key LIKE '%/actions/%') EXECUTE FUNCTION reject_actions();
            """);
        var detail = await EnqueueDetailAsync("detail");

        await Assert.ThrowsAnyAsync<Exception>(() => AdmitDetailAsync(detail, DetailBill(UpdatedAt, cosponsors: 2),
            BillGateOutcome.Passed(BillGateReason.CoveredSponsor)));

        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM bill_gates"));
        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM bill_follow_on_jobs"));
        Assert.Empty(await AdmittedJobsAsync());
    }

    [Theory]
    [InlineData("Pending", "CoveredSponsor")]
    [InlineData("Passed", "NoPolicyArea")]
    [InlineData("Passed", "UntrackedPolicyArea")]
    [InlineData("NotRelevant", "TrackedPolicyArea")]
    [InlineData("NotRelevant", "CoveredCosponsor")]
    [InlineData("NotRelevant", null)]
    [InlineData("Passed", null)]
    public async Task TheDatabaseRefusesAReasonThatDoesNotBelongToItsDecision(string decision, string? reason)
    {
        var detail = await EnqueueDetailAsync("detail");
        await AdmitDetailAsync(detail, DetailBill(UpdatedAt, cosponsors: 1), BillGateOutcome.Pending);
        var reasonSql = reason is null ? "NULL" : $"'{reason}'";
        var decidedSql = decision == "Pending" ? "NULL" : "1";

        await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(connectionString,
            $"UPDATE bill_gates SET decision = '{decision}', reason = {reasonSql}, decided_at_utc_ticks = {decidedSql}"));
    }

    [Fact]
    public async Task TheDatabaseRefusesAnIssueRevisionThatIsNotAHash()
    {
        var detail = await EnqueueDetailAsync("detail");
        await AdmitDetailAsync(detail, DetailBill(UpdatedAt, cosponsors: 1), BillGateOutcome.Pending);

        await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(connectionString,
            "UPDATE bill_gates SET issue_revision_id = 'not-a-hash'"));
    }

    [Fact]
    public async Task ReplayingTheCosponsorPageThatPassedTheGateAddsNoSecondActionsJobAndKeepsTheGate()
    {
        var detail = await EnqueueDetailAsync("detail");
        var first = await AdmitDetailAsync(detail, DetailBill(UpdatedAt, cosponsors: 1), BillGateOutcome.Pending);
        var pageJob = first.Jobs.Single().JobId;
        var page = Cosponsors(["A000001"], next: null);
        await AdmitCosponsorPageAsync(pageJob, page);
        var before = await CountsAsync();

        var replay = await AdmitCosponsorPageAsync(pageJob, page);

        Assert.Empty(replay.Jobs);
        Assert.Equal(before, await CountsAsync());
        Assert.Equal(("Passed", "CoveredCosponsor"), await GateOfAsync(detail));
    }

    [Fact]
    public async Task ReplayingTheLastCosponsorPageAfterNotRelevantChangesNothing()
    {
        var detail = await EnqueueDetailAsync("detail");
        var first = await AdmitDetailAsync(detail, DetailBill(UpdatedAt, cosponsors: 1), BillGateOutcome.Pending);
        var pageJob = first.Jobs.Single().JobId;
        var page = Cosponsors(["Z000009"], next: null);
        await AdmitCosponsorPageAsync(pageJob, page);
        var before = await CountsAsync();
        var decidedAt = await ScalarAsync("SELECT decided_at_utc_ticks FROM bill_gates");

        await AdmitCosponsorPageAsync(pageJob, page);

        Assert.Equal(before, await CountsAsync());
        Assert.Equal(("NotRelevant", "UntrackedPolicyArea"), await GateOfAsync(detail));
        Assert.Equal(decidedAt, await ScalarAsync("SELECT decided_at_utc_ticks FROM bill_gates"));
    }

    [Fact]
    public async Task ReplayingAnActionsPageChangesNothing()
    {
        var detail = await EnqueueDetailAsync("detail");
        var first = await AdmitDetailAsync(detail, DetailBill(UpdatedAt, cosponsors: 0), BillGateOutcome.Passed(BillGateReason.CoveredSponsor));
        var actionsJob = first.Jobs.Single().JobId;
        var page = new CongressBillActionsPage([], ActionsNext(250));
        await store.AdmitActionsPageAsync(Source(), actionsJob, page, default);
        var before = await CountsAsync();

        var replay = await store.AdmitActionsPageAsync(Source(), actionsJob, page, default);

        Assert.Empty(replay.Jobs);
        Assert.Equal(before, await CountsAsync());
    }

    [Fact]
    public async Task ACoveredSponsorBillAdmitsBothFirstPagesAndALaterCosponsorPageKeepsItsReason()
    {
        var detail = await EnqueueDetailAsync("detail");

        var first = await AdmitDetailAsync(detail, DetailBill(UpdatedAt, cosponsors: 2), BillGateOutcome.Passed(BillGateReason.CoveredSponsor));

        Assert.Equal(2, first.Jobs.Count);
        Assert.Contains(first.Jobs, job => job.Url.Contains("/cosponsors?"));
        Assert.Contains(first.Jobs, job => job.Url.Contains("/actions?"));
        var cosponsorJob = first.Jobs.Single(job => job.Url.Contains("/cosponsors?")).JobId;
        await AdmitCosponsorPageAsync(cosponsorJob, Cosponsors(["A000001"], next: null));
        Assert.Equal(("Passed", "CoveredSponsor"), await GateOfAsync(detail));
        Assert.Equal(2, (await LinksAsync()).Count);
    }

    private static readonly DateTimeOffset UpdatedAt = new(2025, 3, 3, 12, 0, 0, TimeSpan.Zero);

    private static CongressBill DetailBill(DateTimeOffset updatedAt, int cosponsors,
        string? policyArea = BillGateFixtures.TrackedPolicyArea) =>
        new(119, "hr", 7, "A bill", Backfill, updatedAt, policyArea, ["Z000009"], cosponsors, 1);

    private static string CosponsorsNext(int offset) =>
        $"https://api.congress.gov/v3/bill/119/hr/7/cosponsors?offset={offset}&limit=250&format=json";

    private static string ActionsNext(int offset) =>
        $"https://api.congress.gov/v3/bill/119/hr/7/actions?offset={offset}&limit=250&format=json";

    private static CongressBillCosponsorsPage Cosponsors(string[] bioguideIds, string? next) =>
        new([.. bioguideIds.Select(id => new CongressBillCosponsor(id, Backfill, false, null))], next);

    private async Task<string> EnqueueDetailAsync(string name)
    {
        var definition = Source().CreateApiJobDefinition(ApiResource.Bill, "https://api.congress.gov/v3/bill/119/hr/7?format=json", Backfill);
        var job = await store.EnqueueAsync(definition, ListJobKeyPrefix + name, default);
        await ExecuteAsync(connectionString, $"UPDATE collection_jobs SET state = 'Succeeded' WHERE job_id = '{job.JobId}'");
        return job.JobId;
    }

    private Task<DiscoveryAdmissionResult> AdmitDetailAsync(string detailJobId, CongressBill bill, BillGateOutcome gate) =>
        store.AdmitDetailAsync(Source(), detailJobId, bill, gate, BillGateFixtures.Revision().Id, default);

    private Task<DiscoveryAdmissionResult> AdmitCosponsorPageAsync(string pageJobId, CongressBillCosponsorsPage page) =>
        store.AdmitCosponsorPageAsync(Source(), pageJobId, page, default);

    private async Task<(string Decision, string? Reason)> GateOfAsync(string detailJobId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"SELECT decision, reason FROM bill_gates WHERE detail_job_id = '{detailJobId}'", connection);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1));
    }

    private async Task<IReadOnlyList<(string Key, string DetailJobId)>> LinksAsync()
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            SELECT j.idempotency_key, l.detail_job_id FROM bill_follow_on_jobs l
            JOIN collection_jobs j ON j.job_id = l.job_id ORDER BY j.idempotency_key
            """, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var links = new List<(string, string)>();
        while (await reader.ReadAsync()) links.Add((reader.GetString(0), reader.GetString(1)));
        return links;
    }

    private async Task<(object? Jobs, object? Links, object? Gates)> CountsAsync() =>
        (await ScalarAsync("SELECT count(*) FROM collection_jobs"), await ScalarAsync("SELECT count(*) FROM bill_follow_on_jobs"),
            await ScalarAsync("SELECT count(*) FROM bill_gates"));

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
