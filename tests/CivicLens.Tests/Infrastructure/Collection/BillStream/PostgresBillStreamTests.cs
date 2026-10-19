using System.Text.Json;
using System.Text.RegularExpressions;
using CivicLens.Application.Collection;
using CivicLens.Application.Configuration;
using CivicLens.Application.Collection.Bills;
using CivicLens.Application.Collection.Discovery;
using CivicLens.Application.Collection.Jobs;
using CivicLens.Collection.Contracts;
using CivicLens.Infrastructure.Collection;
using CivicLens.Core.Documents;
using CivicLens.Infrastructure.Collection.Jobs;
using CivicLens.Infrastructure.Documents;
using CivicLens.Tests.Application.Collection.Bills;
using CivicLens.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.Collection.BillStream;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed partial class PostgresBillStreamTests(PostgresCollection postgres) : IAsyncLifetime
{
    private const string SourceId = "congress-bills";
    private const string SourceUrl = "https://api.congress.gov/v3/bill";
    private const string ListJobKeyPrefix = "test-list/";
    private static readonly DateOnly Backfill = new(2025, 1, 3);
    private string connectionString = null!;
    private PostgresCollectionJobStore store = null!;
    private PostgresDocumentExtractionStore extractions = null!;
    private StatementCandidateSeeder seeder = null!;
    private JobExtractionSeeder jobExtractions = null!;
    private readonly string captureRoot = Path.Combine(Path.GetTempPath(), "bill-stream-" + Guid.NewGuid().ToString("N"));

    public async Task InitializeAsync()
    {
        connectionString = await postgres.CreateMigratedDatabaseAsync();
        store = PostgresCollectionJobStore.FromConnectionString(connectionString);
        var attempts = PostgresCollectionAttemptStore.FromConnectionString(connectionString);
        extractions = PostgresDocumentExtractionStore.FromConnectionString(connectionString);
        Directory.CreateDirectory(captureRoot);
        seeder = new StatementCandidateSeeder(attempts, extractions, connectionString, captureRoot);
        jobExtractions = new JobExtractionSeeder(store, extractions, seeder, connectionString, captureRoot);
    }

    public async Task DisposeAsync()
    {
        await postgres.DropDatabaseAsync(connectionString);
        Directory.Delete(captureRoot, recursive: true);
    }

    [Fact]
    public async Task FirstDueCheckRequestsTheBackfillWindowWithOneDayOfOverlap()
    {
        await store.SynchronizeAsync(Configuration(), default);
        await ExecuteAsync(connectionString, "UPDATE collection_source_schedules SET next_due_utc_ticks = 0");

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
        await ExecuteAsync(connectionString, "UPDATE collection_source_schedules SET next_due_utc_ticks = 0");
        await SetCursorAsync(BillStreamWindow.LastClosedDay(DateTimeOffset.UtcNow));

        Assert.Equal(1, await store.AdvanceDueAsync(10, default));

        Assert.Empty(await store.ListAsync(10, default));
        Assert.True(await ReadNextDueAsync() > DateTimeOffset.UtcNow.UtcTicks);
    }

    [Fact]
    public async Task DueCheckStartsTheDayAfterTheCursorAndStopsAtTheLastClosedDay()
    {
        await store.SynchronizeAsync(Configuration(), default);
        await ExecuteAsync(connectionString, "UPDATE collection_source_schedules SET next_due_utc_ticks = 0");
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
    public async Task ListPageRecordsHowManyUnknownBillTypesItSkippedSoTheExclusionStaysVisible()
    {
        var window = new BillStreamWindow(Backfill, Backfill.AddDays(1));

        await AdmitAsync(Source(), "odd-types", window,
            new CongressBillListPage([Bill(119, 1)], null, StartsPass: true, UnknownTypeCount: 2));

        Assert.Equal(2, await ScalarAsync("SELECT unknown_type_count FROM bill_stream_pages"));
    }

    [Fact]
    public async Task AListPageWithOnlyUnknownBillTypesStillSettlesTheWindowAndMovesTheCursor()
    {
        var window = new BillStreamWindow(Backfill, Backfill.AddDays(1));

        await AdmitAsync(Source(), "only-odd", window, new CongressBillListPage([], null, StartsPass: true, UnknownTypeCount: 3));

        Assert.Equal(window.LastDay, await ReadCursorAsync());
        Assert.Equal(3, await ScalarAsync("SELECT unknown_type_count FROM bill_stream_pages"));
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
            File.ReadAllText(Path.Combine(RepositoryRoot.FullName, "config", "federal-pilot.json")), ConfigurationJson.Options)!;
        pilot = pilot with { Sources = [.. pilot.Sources.Where(source => source.Id == SourceId).Select(source => source with { Enabled = true })] };
        var window = new BillStreamWindow(Backfill, Backfill);
        var page = new CongressBillListPage([.. Enumerable.Range(1, ApiPageQuery.PageLimit).Select(number => Bill(119, number))],
            window.RequestUrl(SourceUrl) + "&offset=250", StartsPass: true);

        var result = await AdmitAsync(new ConfiguredCollectionSource(pilot, SourceId, Backfill.AddDays(400)),
            "full", window, page);

        Assert.Equal(ApiPageQuery.PageLimit + 1, result.AdmittedCount);
        Assert.Equal(0, result.DeferredCount);
    }

    [Fact]
    public async Task AFullPageStoresItsConfigurationRevisionOnceInsteadOfOncePerJob()
    {
        var window = new BillStreamWindow(Backfill, Backfill);
        var page = new CongressBillListPage([.. Enumerable.Range(1, ApiPageQuery.PageLimit).Select(number => Bill(119, number))],
            window.RequestUrl(SourceUrl) + "&offset=250", StartsPass: true);

        var result = await AdmitAsync(Source(), "full", window, page);

        Assert.Equal(ApiPageQuery.PageLimit + 1, result.AdmittedCount);
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

        await AdmitCosponsorPageAsync(first.Jobs.Single().JobId, withdrawn);

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

    [Theory]
    [InlineData("Actions")]
    [InlineData("Summaries")]
    public async Task ABillPageChainsByItsParentJobIsLinkedToTheDetailJobAndChangesNothingWhenReplayed(string role)
    {
        var bill = await StartPassedBillAsync(cosponsors: 0, summaries: 2);
        var (firstPageJob, nextUrl, resource) = role == "Actions"
            ? (bill.Actions, ActionsNext(250), ApiResource.BillActions)
            : (bill.Summaries!, SummariesNext(250), ApiResource.BillSummaries);
        Task<DiscoveryAdmissionResult> Admit(string pageJob, string? next) => role == "Actions"
            ? AdmitActionsAsync(pageJob, new CongressBillActionsPage([], next))
            : AdmitSummariesAsync(pageJob, new CongressBillSummariesPage([], next));

        var next = await Admit(firstPageJob, nextUrl);

        Assert.Equal(nextUrl, next.Jobs.Single().Url);
        Assert.Contains((BillStreamJobKeys.NextBillPage(SourceId, resource, firstPageJob), bill.Detail),
            (await LinksAsync()).Select(link => (link.Key, link.DetailJobId)));
        Assert.Contains(await PagesAsync(), page => page.Role == role);
        var counts = await CountsAsync();
        var pages = await PagesAsync();
        var replay = await Admit(firstPageJob, nextUrl);
        Assert.Empty(replay.Jobs);
        Assert.Equal(1, replay.DuplicateCount);
        Assert.Equal(counts, await CountsAsync());
        Assert.Equal(pages, await PagesAsync());
        var last = await Admit(next.Jobs.Single().JobId, null);
        Assert.Empty(last.Jobs);
    }

    [Fact]
    public async Task APageJobWithoutALinkRowIsInvalidData()
    {
        var orphan = await EnqueueDetailAsync("orphan");

        await Assert.ThrowsAsync<InvalidDataException>(async () => await AdmitCosponsorPageAsync(orphan, Cosponsors([], null)));
        await Assert.ThrowsAsync<InvalidDataException>(async () => await AdmitActionsAsync(orphan, new CongressBillActionsPage([], null)));
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

    [Fact]
    public async Task ACoveredSponsorBillWithSummariesAdmitsASummariesFirstPageBesideItsActionsPage()
    {
        var detail = await EnqueueDetailAsync("detail");

        var result = await AdmitDetailAsync(detail, DetailBill(UpdatedAt, cosponsors: 0, summaries: 2),
            BillGateOutcome.Passed(BillGateReason.CoveredSponsor));

        Assert.Equal(["https://api.congress.gov/v3/bill/119/hr/7/actions?format=json&limit=250",
            "https://api.congress.gov/v3/bill/119/hr/7/summaries?format=json&limit=1"],
            result.Jobs.Select(job => job.Url).Order());
        Assert.Equal(2, (await LinksAsync()).Count(link => link.DetailJobId == detail));
        Assert.Equal(2, await ScalarAsync("SELECT summary_count FROM bill_gates"));
    }

    [Fact]
    public async Task APassedBillWithNoSummariesAdmitsNoSummariesPage()
    {
        var detail = await EnqueueDetailAsync("detail");

        var result = await AdmitDetailAsync(detail, DetailBill(UpdatedAt, cosponsors: 0, summaries: 0),
            BillGateOutcome.Passed(BillGateReason.CoveredSponsor));

        Assert.DoesNotContain(result.Jobs, job => job.Url.Contains("/summaries?"));
        Assert.Equal(0, await ScalarAsync("SELECT summary_count FROM bill_gates"));
    }

    [Theory]
    [InlineData(BillGateReason.NoPolicyArea)]
    [InlineData(BillGateReason.UntrackedPolicyArea)]
    public async Task ABillTheGateMarksNotRelevantNeverAdmitsSummariesEvenWhenItHasThem(BillGateReason reason)
    {
        var detail = await EnqueueDetailAsync("detail");

        var result = await AdmitDetailAsync(detail, DetailBill(UpdatedAt, cosponsors: 0, summaries: 4),
            BillGateOutcome.NotRelevant(reason));

        Assert.Empty(result.Jobs);
    }

    [Fact]
    public async Task ABillThatPassesAtACosponsorPageAdmitsSummariesFromTheCountStoredAtDetail()
    {
        var detail = await EnqueueDetailAsync("detail");
        var first = await AdmitDetailAsync(detail, DetailBill(UpdatedAt, cosponsors: 1, summaries: 3), BillGateOutcome.Pending);
        Assert.Equal("cosponsors", Assert.Single(first.Jobs).Url.Split('/')[^1].Split('?')[0]);

        var passed = await AdmitCosponsorPageAsync(first.Jobs.Single().JobId, Cosponsors(["A000001"], next: null));

        Assert.Equal(("Passed", "CoveredCosponsor"), await GateOfAsync(detail));
        Assert.Equal(["actions", "summaries"], passed.Jobs.Select(job => job.Url.Split('/')[^1].Split('?')[0]).Order());
    }

    [Fact]
    public async Task ABillThatPassesAtACosponsorPageWithoutSummariesAdmitsOnlyActions()
    {
        var detail = await EnqueueDetailAsync("detail");
        var first = await AdmitDetailAsync(detail, DetailBill(UpdatedAt, cosponsors: 1, summaries: 0), BillGateOutcome.Pending);

        var passed = await AdmitCosponsorPageAsync(first.Jobs.Single().JobId, Cosponsors(["A000001"], next: null));

        Assert.Contains("/actions?", Assert.Single(passed.Jobs).Url);
    }

    [Fact]
    public async Task ABillThatStaysNotRelevantAtTheLastCosponsorPageAdmitsNoSummaries()
    {
        var detail = await EnqueueDetailAsync("detail");
        var first = await AdmitDetailAsync(detail, DetailBill(UpdatedAt, cosponsors: 1, summaries: 3), BillGateOutcome.Pending);

        var settled = await AdmitCosponsorPageAsync(first.Jobs.Single().JobId, Cosponsors(["Z000009"], next: null));

        Assert.Empty(settled.Jobs);
        Assert.DoesNotContain(await LinksAsync(), link => link.Key.Contains("/summaries/"));
    }

    [Fact]
    public async Task TheDatabaseRefusesANegativeSummaryCount()
    {
        var detail = await EnqueueDetailAsync("detail");
        await AdmitDetailAsync(detail, DetailBill(UpdatedAt, cosponsors: 1), BillGateOutcome.Pending);

        await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(connectionString, "UPDATE bill_gates SET summary_count = -1"));
    }

    [Fact]
    public async Task EveryAdmittedPageRecordsItsExtractionItsOrderAndWhetherItClosesItsChain()
    {
        var bill = await StartPassedBillAsync(cosponsors: 2, summaries: 0);
        var secondCosponsorPage = await AdmitCosponsorPageAsync(bill.Cosponsors!, Cosponsors(["Z000009"], CosponsorsNext(250)));
        await AdmitCosponsorPageAsync(secondCosponsorPage.Jobs.Single().JobId, Cosponsors(["Z000009"], next: null));

        var pages = await PagesAsync();

        Assert.Equal([("Cosponsors", 0), ("Cosponsors", 1), ("Detail", 0)],
            pages.Select(page => (page.Role, page.Order)).Order());
        Assert.Equal([("Cosponsors", 0, false), ("Cosponsors", 1, true), ("Detail", 0, true)],
            pages.Select(page => (page.Role, page.Order, page.Closes)).Order());
        Assert.Equal(pages.Count, pages.Select(page => page.ExtractionId).Distinct().Count());
        Assert.All(pages, page => Assert.Equal(64, page.ExtractionId.Length));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ACandidateIsWrittenOnceWhenTheLastOfTheCosponsorAndActionsChainsCloses(bool cosponsorsCloseLast)
    {
        var bill = await StartPassedBillAsync(cosponsors: 2, summaries: 0);
        var cosponsorsNext = await AdmitCosponsorPageAsync(bill.Cosponsors!, Cosponsors(["Z000009"], CosponsorsNext(250)));
        var actionsNext = await AdmitActionsAsync(bill.Actions, new CongressBillActionsPage([], ActionsNext(250)));
        Assert.Equal(0, await CandidateCountAsync());

        var closeCosponsors = () => AdmitCosponsorPageAsync(cosponsorsNext.Jobs.Single().JobId, Cosponsors(["Z000009"], next: null));
        var closeActions = async () => await AdmitActionsAsync(actionsNext.Jobs.Single().JobId, new CongressBillActionsPage([], null));
        if (cosponsorsCloseLast)
        {
            await closeActions();
            Assert.Equal(0, await CandidateCountAsync());
            await closeCosponsors();
        }
        else
        {
            await closeCosponsors();
            Assert.Equal(0, await CandidateCountAsync());
            await closeActions();
        }

        var candidate = Assert.Single(await CandidatesAsync());
        Assert.Equal(bill.Detail, candidate.DetailJobId);
        Assert.Equal((SourceId, 119, "hr", 7, UpdatedAt.UtcTicks), (candidate.SourceId, candidate.Congress, candidate.BillType, candidate.BillNumber, candidate.UpdateTicks));
        Assert.Matches("^[0-9a-f]{64}$", candidate.CandidateId);
        Assert.Equal(Source().Revision.Id, candidate.RevisionId);
        Assert.False(candidate.IncludesSummaries);
    }

    [Fact]
    public async Task ABillWithoutCosponsorsGetsItsCandidateWhenTheActionsChainCloses()
    {
        var bill = await StartPassedBillAsync(cosponsors: 0, summaries: 0);
        Assert.Null(bill.Cosponsors);

        await AdmitActionsAsync(bill.Actions, new CongressBillActionsPage([], null));

        Assert.Equal(1, await CandidateCountAsync());
    }

    [Fact]
    public async Task ABillThatPassesAtALaterCosponsorPageGetsItsCandidateAfterBothChainsClose()
    {
        var detail = await EnqueueDetailAsync("detail");
        var first = await AdmitDetailAsync(detail, DetailBill(UpdatedAt, cosponsors: 2), BillGateOutcome.Pending);
        var second = await AdmitCosponsorPageAsync(first.Jobs.Single().JobId, Cosponsors(["Z000009"], CosponsorsNext(250)));
        var passed = await AdmitCosponsorPageAsync(second.Jobs.Single().JobId, Cosponsors(["A000001"], next: null));
        Assert.Equal(0, await CandidateCountAsync());

        await AdmitActionsAsync(passed.Jobs.Single().JobId, new CongressBillActionsPage([], null));

        Assert.Equal(1, await CandidateCountAsync());
    }

    [Fact]
    public async Task ReplayingAnyAdmissionOfACompleteBillWritesNothingNew()
    {
        var bill = await StartPassedBillAsync(cosponsors: 1, summaries: 0);
        var cosponsors = Cosponsors(["A000001"], next: null);
        var actions = new CongressBillActionsPage([], null);
        await AdmitCosponsorPageAsync(bill.Cosponsors!, cosponsors);
        await AdmitActionsAsync(bill.Actions, actions);
        var candidates = await CandidatesAsync();
        var pages = await PagesAsync();

        await AdmitCosponsorPageAsync(bill.Cosponsors!, cosponsors);
        await AdmitActionsAsync(bill.Actions, actions);
        await AdmitDetailAsync(bill.Detail, DetailBill(UpdatedAt, cosponsors: 1), BillGateOutcome.Passed(BillGateReason.CoveredSponsor));

        Assert.Equal(candidates, await CandidatesAsync());
        Assert.Equal(pages, await PagesAsync());
    }

    [Fact]
    public async Task NoCandidateIsWrittenForABillTheGateMarksNotRelevant()
    {
        var detail = await EnqueueDetailAsync("detail");
        var first = await AdmitDetailAsync(detail, DetailBill(UpdatedAt, cosponsors: 1, policyArea: null), BillGateOutcome.Pending);

        await AdmitCosponsorPageAsync(first.Jobs.Single().JobId, Cosponsors(["Z000009"], next: null));

        Assert.Equal(("NotRelevant", "NoPolicyArea"), await GateOfAsync(detail));
        Assert.Equal(0, await CandidateCountAsync());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AFailedOrBlockedPageLeavesTheChainOpenSoNoCandidateIsWritten(bool cosponsorsFail)
    {
        var bill = await StartPassedBillAsync(cosponsors: 1, summaries: 0);

        if (cosponsorsFail) await AdmitActionsAsync(bill.Actions, new CongressBillActionsPage([], null));
        else await AdmitCosponsorPageAsync(bill.Cosponsors!, Cosponsors(["A000001"], next: null));

        Assert.Equal(0, await CandidateCountAsync());
        await ExecuteAsync(connectionString, """
            UPDATE collection_jobs SET state = 'Failed' WHERE job_id IN (
                SELECT l.job_id FROM bill_follow_on_jobs l LEFT JOIN bill_pages p ON p.job_id = l.job_id WHERE p.job_id IS NULL)
            """);
        var stuck = Assert.Single(await StuckBillsAsync());
        Assert.Equal(cosponsorsFail ? "billCosponsors" : "billActions", stuck.Resource);
    }

    [Fact]
    public async Task AChainWhoseClosingPageHasAGapBeforeItNeverCompletes()
    {
        var bill = await StartPassedBillAsync(cosponsors: 1, summaries: 0);
        var next = await AdmitActionsAsync(bill.Actions, new CongressBillActionsPage([], ActionsNext(250)));
        await AdmitActionsAsync(next.Jobs.Single().JobId, new CongressBillActionsPage([], null));
        await ExecuteAsync(connectionString, "UPDATE bill_pages SET page_order = 5 WHERE role = 'Actions' AND closes_chain");

        await AdmitCosponsorPageAsync(bill.Cosponsors!, Cosponsors(["A000001"], next: null));

        Assert.Equal(0, await CandidateCountAsync());
    }

    [Fact]
    public async Task SummariesThatCloseBeforeTheEvidenceChainLeaveOnlyTheCandidateWithSummaries()
    {
        var bill = await StartPassedBillAsync(cosponsors: 0, summaries: 2);
        var summariesNext = await AdmitSummariesAsync(bill.Summaries!, new CongressBillSummariesPage([], SummariesNext(1)));
        await AdmitSummariesAsync(summariesNext.Jobs.Single().JobId, new CongressBillSummariesPage([], null));
        Assert.Equal(0, await CandidateCountAsync());

        await AdmitActionsAsync(bill.Actions, new CongressBillActionsPage([], null));

        Assert.True(Assert.Single(await CandidatesAsync()).IncludesSummaries);
    }

    [Fact]
    public async Task SummariesThatCloseAfterTheEvidenceChainAddASecondCandidateAndReplayAddsNoThird()
    {
        var bill = await StartPassedBillAsync(cosponsors: 0, summaries: 2);
        await AdmitActionsAsync(bill.Actions, new CongressBillActionsPage([], null));
        var older = Assert.Single(await CandidatesAsync());
        Assert.False(older.IncludesSummaries);
        var last = new CongressBillSummariesPage([], null);

        await AdmitSummariesAsync(bill.Summaries!, last);
        var both = await CandidatesAsync();
        await AdmitSummariesAsync(bill.Summaries!, last);
        await AdmitActionsAsync(bill.Actions, new CongressBillActionsPage([], null));

        Assert.Equal([false, true], both.Select(candidate => candidate.IncludesSummaries).Order());
        Assert.Contains(older, both);
        Assert.Equal(both, await CandidatesAsync());
    }

    [Fact]
    public async Task ABillWithNoSummariesGetsOnlyACandidateWithoutSummaries()
    {
        var bill = await StartPassedBillAsync(cosponsors: 0, summaries: 0);

        await AdmitActionsAsync(bill.Actions, new CongressBillActionsPage([], null));

        Assert.False(Assert.Single(await CandidatesAsync()).IncludesSummaries);
    }

    [Fact]
    public async Task ABillVersionWithANewUpdateDateGetsItsOwnCandidate()
    {
        var first = await StartPassedBillAsync(cosponsors: 0, summaries: 0);
        await AdmitActionsAsync(first.Actions, new CongressBillActionsPage([], null));
        var detail = await EnqueueDetailAsync("detail-two");
        var second = await AdmitDetailAsync(detail, DetailBill(UpdatedAt.AddDays(1), cosponsors: 0),
            BillGateOutcome.Passed(BillGateReason.CoveredSponsor));

        await AdmitActionsAsync(second.Jobs.Single().JobId, new CongressBillActionsPage([], null));

        Assert.Equal(2, (await CandidatesAsync()).Select(candidate => candidate.CandidateId).Distinct().Count());
    }

    [Fact]
    public async Task AnExtractionOfAnotherJobsAttemptIsInvalidDataAndWritesNoPageRowOrCandidate()
    {
        var bill = await StartPassedBillAsync(cosponsors: 0, summaries: 0);
        var otherJob = await EnqueueDetailAsync("other");
        var foreign = await ExtractionAsync(otherJob);

        await Assert.ThrowsAsync<InvalidDataException>(() => store.AdmitActionsPageAsync(Source(), bill.Actions,
            new CongressBillActionsPage([], null), foreign, default));

        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM bill_pages WHERE role = 'Actions'"));
        Assert.Equal(0, await CandidateCountAsync());
    }

    [Fact]
    public async Task ADetailWithAnExtractionOfAnotherJobIsInvalidDataAndSavesNoGate()
    {
        var detail = await EnqueueDetailAsync("detail");
        var foreign = await ExtractionAsync(await EnqueueDetailAsync("other"));

        await Assert.ThrowsAsync<InvalidDataException>(() => store.AdmitDetailAsync(Source(), detail,
            DetailBill(UpdatedAt, cosponsors: 0), BillGateOutcome.Passed(BillGateReason.CoveredSponsor),
            BillGateFixtures.Revision().Id, foreign, default));

        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM bill_gates"));
        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM bill_pages"));
    }

    [Fact]
    public async Task AnUnknownExtractionIdIsInvalidData()
    {
        var detail = await EnqueueDetailAsync("detail");

        await Assert.ThrowsAsync<InvalidDataException>(() => store.AdmitDetailAsync(Source(), detail,
            DetailBill(UpdatedAt, cosponsors: 0), BillGateOutcome.Passed(BillGateReason.CoveredSponsor),
            BillGateFixtures.Revision().Id, new string('a', 64), default));
    }

    [Theory]
    [InlineData("UPDATE bill_pages SET page_order = 1 WHERE role = 'Detail'")]
    [InlineData("UPDATE bill_pages SET closes_chain = false WHERE role = 'Detail'")]
    [InlineData("UPDATE bill_pages SET role = 'Unknown' WHERE role = 'Detail'")]
    [InlineData("UPDATE bill_pages SET page_order = -1 WHERE role = 'Detail'")]
    public async Task TheDatabaseRefusesAnImpossibleBillPageRow(string corruption)
    {
        await StartPassedBillAsync(cosponsors: 0, summaries: 0);

        var refused = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(connectionString, corruption));

        Assert.Equal(PostgresErrorCodes.CheckViolation, refused.SqlState);
    }

    [Fact]
    public async Task TheDatabaseRefusesADetailRowWhoseJobIsNotTheRoot()
    {
        var bill = await StartPassedBillAsync(cosponsors: 0, summaries: 0);
        await AdmitActionsAsync(bill.Actions, new CongressBillActionsPage([], null));

        var refused = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(connectionString,
            $"UPDATE bill_pages SET role = 'Detail', page_order = 0, closes_chain = true WHERE job_id = '{bill.Actions}'"));

        Assert.Equal(PostgresErrorCodes.CheckViolation, refused.SqlState);
    }

    [Fact]
    public async Task AGateWrittenBeforeThePageRowsExistedNeverGetsACandidate()
    {
        var bill = await StartPassedBillAsync(cosponsors: 0, summaries: 0);
        await ExecuteAsync(connectionString, "DELETE FROM bill_pages WHERE role = 'Detail'");

        await AdmitActionsAsync(bill.Actions, new CongressBillActionsPage([], null));

        Assert.Equal(0, await CandidateCountAsync());
    }

    [Fact]
    public async Task TheCandidateIdIsTheSha256OfTheLengthFramedSourceAndTheBillVersion()
    {
        var bill = await StartPassedBillAsync(cosponsors: 0, summaries: 0);
        await AdmitActionsAsync(bill.Actions, new CongressBillActionsPage([], null));

        var identity = $"{SourceId.Length}:{SourceId}119:hr:7:{UpdatedAt.UtcTicks}";
        var expected = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(identity)));
        Assert.Equal(expected, Assert.Single(await CandidatesAsync()).CandidateId);
    }

    [Fact]
    public async Task TheCandidateWithSummariesHashesTheSameIdentityWithASummariesSuffix()
    {
        var bill = await StartPassedBillAsync(cosponsors: 0, summaries: 1);
        await AdmitSummariesAsync(bill.Summaries!, new CongressBillSummariesPage([], null));
        await AdmitActionsAsync(bill.Actions, new CongressBillActionsPage([], null));

        var identity = $"{SourceId.Length}:{SourceId}119:hr:7:{UpdatedAt.UtcTicks}:summaries";
        var expected = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(identity)));
        Assert.Equal(expected, Assert.Single(await CandidatesAsync()).CandidateId);
    }

    [Theory]
    [InlineData("UPDATE bill_pages SET closes_chain = true WHERE role = 'Actions' AND page_order = 0")]
    [InlineData("UPDATE bill_pages SET closes_chain = NOT closes_chain WHERE role = 'Actions'")]
    public async Task AChainWithTwoClosingRowsOrAClosingRowBelowTheHighestOrderNeverCompletes(string corruption)
    {
        var bill = await StartPassedBillAsync(cosponsors: 1, summaries: 0);
        var next = await AdmitActionsAsync(bill.Actions, new CongressBillActionsPage([], ActionsNext(250)));
        await AdmitActionsAsync(next.Jobs.Single().JobId, new CongressBillActionsPage([], null));
        await ExecuteAsync(connectionString, corruption);

        await AdmitCosponsorPageAsync(bill.Cosponsors!, Cosponsors(["A000001"], next: null));

        Assert.Equal(0, await CandidateCountAsync());
    }

    private sealed record StartedBill(string Detail, string? Cosponsors, string Actions, string? Summaries);

    private async Task<StartedBill> StartPassedBillAsync(int cosponsors, int summaries, bool hasTrackedIssue = false)
    {
        var detail = await EnqueueDetailAsync("detail");
        var result = await AdmitDetailAsync(detail, DetailBill(UpdatedAt, cosponsors, summaries: summaries),
            BillGateOutcome.Passed(BillGateReason.CoveredSponsor) with { HasTrackedIssue = hasTrackedIssue });
        string? JobOf(string resource) => result.Jobs.SingleOrDefault(job => job.Url.Contains($"/{resource}?"))?.JobId;
        return new StartedBill(detail, JobOf("cosponsors"), JobOf("actions")!, JobOf("summaries"));
    }

    private sealed record PageRecord(string Role, int Order, bool Closes, string ExtractionId);

    private async Task<IReadOnlyList<PageRecord>> PagesAsync()
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT role, page_order, closes_chain, extraction_id FROM bill_pages ORDER BY role, page_order", connection);
        await using var reader = await command.ExecuteReaderAsync();
        var pages = new List<PageRecord>();
        while (await reader.ReadAsync())
            pages.Add(new PageRecord(reader.GetString(0), reader.GetInt32(1), reader.GetBoolean(2), reader.GetString(3)));
        return pages;
    }

    private sealed record CandidateRecord(string CandidateId, string SourceId, int Congress, string BillType, int BillNumber,
        long UpdateTicks, string DetailJobId, string RevisionId, long CreatedTicks, bool IncludesSummaries);

    private async Task<IReadOnlyList<CandidateRecord>> CandidatesAsync()
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            SELECT candidate_id, source_id, congress, bill_type, bill_number, update_date_utc_ticks, detail_job_id,
                   configuration_revision_id, created_at_utc_ticks, includes_summaries
            FROM action_candidates ORDER BY candidate_id
            """, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var candidates = new List<CandidateRecord>();
        while (await reader.ReadAsync())
            candidates.Add(new CandidateRecord(reader.GetString(0), reader.GetString(1), reader.GetInt32(2), reader.GetString(3),
                reader.GetInt32(4), reader.GetInt64(5), reader.GetString(6), reader.GetString(7), reader.GetInt64(8),
                reader.GetBoolean(9)));
        return candidates;
    }

    private async Task<long> CandidateCountAsync() => (long)(await ScalarAsync("SELECT count(*) FROM action_candidates"))!;

    private sealed record StuckBill(string DetailJobId, string Resource);

    private async Task<IReadOnlyList<StuckBill>> StuckBillsAsync()
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(StuckBillQueryFromOperationsGuide(), connection);
        await using var reader = await command.ExecuteReaderAsync();
        var stuck = new List<StuckBill>();
        while (await reader.ReadAsync()) stuck.Add(new StuckBill(reader.GetString(0), reader.GetString(6)));
        return stuck;
    }

    private static string StuckBillQueryFromOperationsGuide()
    {
        var guide = File.ReadAllText(Path.Combine(RepositoryRoot.FullName, "docs", "operations.md"));
        return Regex.Matches(guide, @"```sql\r?\n(.*?)```", RegexOptions.Singleline)
            .Select(match => match.Groups[1].Value)
            .Single(query => query.Contains("g.decision = 'Passed'", StringComparison.Ordinal));
    }

    private static readonly DateTimeOffset UpdatedAt = new(2025, 3, 3, 12, 0, 0, TimeSpan.Zero);

    private static CongressBill DetailBill(DateTimeOffset updatedAt, int cosponsors,
        string? policyArea = BillGateFixtures.TrackedPolicyArea, int summaries = 0) =>
        new(119, "hr", 7, "A bill", Backfill, updatedAt, policyArea, ["Z000009"], cosponsors, 1, summaries);

    private static string SummariesNext(int offset) =>
        $"https://api.congress.gov/v3/bill/119/hr/7/summaries?offset={offset}&limit=1&format=json";

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

    private async Task<DiscoveryAdmissionResult> AdmitDetailAsync(string detailJobId, CongressBill bill, BillGateOutcome gate) =>
        await store.AdmitDetailAsync(Source(), detailJobId, bill, gate, BillGateFixtures.Revision().Id,
            await ExtractionAsync(detailJobId), default);

    private async Task<DiscoveryAdmissionResult> AdmitCosponsorPageAsync(string pageJobId, CongressBillCosponsorsPage page) =>
        await store.AdmitCosponsorPageAsync(Source(), pageJobId, page, await ExtractionAsync(pageJobId), default);

    private async Task<DiscoveryAdmissionResult> AdmitActionsAsync(string pageJobId, CongressBillActionsPage page) =>
        await store.AdmitActionsPageAsync(Source(), pageJobId, page, await ExtractionAsync(pageJobId), default);

    private async Task<DiscoveryAdmissionResult> AdmitSummariesAsync(string pageJobId, CongressBillSummariesPage page) =>
        await store.AdmitSummariesPageAsync(Source(), pageJobId, page, await ExtractionAsync(pageJobId), default);

    private Task<string> ExtractionAsync(string jobId)
    {
        var text = "Bill: 119 hr 7\n" + Guid.NewGuid().ToString("N");
        return ExtractionAsync(jobId, text, text, "parser", "normalizer");
    }

    private Task<string> ExtractionAsync(string jobId, string body, string text, string parserVersion, string normalizationVersion) =>
        jobExtractions.SaveAsync(jobId, body, text, parserVersion, normalizationVersion);

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
}
