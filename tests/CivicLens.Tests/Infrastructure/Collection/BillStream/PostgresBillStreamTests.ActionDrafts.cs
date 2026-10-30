using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CivicLens.Application.Actions;
using CivicLens.Application.Analysis;
using CivicLens.Application.Collection;
using CivicLens.Application.Collection.Bills;
using CivicLens.Application.Collection.Processing;
using CivicLens.Core.Review;
using CivicLens.Core.Actions;
using CivicLens.Core.Analysis;
using CivicLens.Core.Documents;
using CivicLens.Infrastructure.Actions;
using CivicLens.Infrastructure.Collection;
using CivicLens.Tests.Application.Actions;
using CivicLens.Tests.Application.Analysis;
using CivicLens.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;
using State = CivicLens.Application.Analysis.AnalysisState;

namespace CivicLens.Tests.Infrastructure.Collection.BillStream;

public sealed partial class PostgresBillStreamTests
{
    private static readonly TimeSpan Lease = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task TheCandidateWriteQueuesOnePendingDraftRowInTheSameTransactionAndReplayKeepsIt()
    {
        var bill = await StartPassedBillAsync(cosponsors: 0, summaries: 0);
        Assert.Empty(await QueueAsync());

        await AdmitActionsAsync(bill.Actions, new CongressBillActionsPage([], null));
        await AdmitActionsAsync(bill.Actions, new CongressBillActionsPage([], null));

        var candidate = Assert.Single(await CandidatesAsync());
        Assert.Equal([(candidate.CandidateId, "action-draft", "Pending", null, 0)], await QueueAsync());
    }

    [Fact]
    public async Task ABillWithoutACandidateHasNoQueueRowAndANotRelevantBillNeverGetsOne()
    {
        var detail = await EnqueueDetailAsync("not-relevant");
        _ = await AdmitDetailAsync(detail, DetailBill(UpdatedAt, 0, policyArea: BillGateFixtures.UntrackedPolicyArea),
            BillGateOutcome.NotRelevant(BillGateReason.UntrackedPolicyArea));

        Assert.Empty(await QueueAsync());
    }

    [Theory]
    [InlineData("Pending")]
    [InlineData("Running")]
    [InlineData("Succeeded")]
    [InlineData("Failed")]
    [InlineData("Blocked")]
    public async Task SummariesThatArriveLaterQueueANewCandidateAndLeaveTheOlderOneAsItWas(string status)
    {
        var bill = await StartPassedBillAsync(cosponsors: 0, summaries: 1);
        await AdmitActionsAsync(bill.Actions, new CongressBillActionsPage([], null));
        var older = Assert.Single(await CandidatesAsync());
        var store = ActionStore();
        if (status == "Running") Assert.NotNull(await store.TryClaimAsync(older.CandidateId, Lease, default));
        else await ExecuteAsync(connectionString, $"UPDATE action_analysis SET status = '{status}', error_code = 'x', attempts = 2");
        var before = Assert.Single(await QueueAsync());

        await AdmitSummariesAsync(bill.Summaries!, new CongressBillSummariesPage([], null));

        var newer = (await CandidatesAsync()).Single(candidate => candidate.IncludesSummaries);
        Assert.Equal(older with { CandidateId = newer.CandidateId, CreatedTicks = newer.CreatedTicks, IncludesSummaries = true }, newer);
        var queue = await QueueAsync();
        Assert.Equal(before, queue.Single(row => row.CandidateId == older.CandidateId));
        Assert.Equal((newer.CandidateId, "action-draft", "Pending", null, 0), queue.Single(row => row.CandidateId == newer.CandidateId));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SubjectsAndSummariesThatCloseLaterGiveAtMostThreeCandidatesWhicheverComesFirstAndReplayAddsNoMore(bool subjectsFirst)
    {
        var bill = await StartPassedBillAsync(cosponsors: 0, summaries: 1, subjects: 2);
        await AdmitActionsAsync(bill.Actions, new CongressBillActionsPage([], null));
        var empty = new CongressBillSummariesPage([], null);
        var emptySubjects = new CongressBillSubjectsPage([], null);

        if (subjectsFirst) await AdmitSubjectsAsync(bill.Subjects!, emptySubjects);
        else await AdmitSummariesAsync(bill.Summaries!, empty);
        var afterFirst = await CandidatesAsync();
        if (subjectsFirst) await AdmitSummariesAsync(bill.Summaries!, empty);
        else await AdmitSubjectsAsync(bill.Subjects!, emptySubjects);
        var all = await CandidatesAsync();
        await AdmitSubjectsAsync(bill.Subjects!, emptySubjects);
        await AdmitSummariesAsync(bill.Summaries!, empty);
        await AdmitActionsAsync(bill.Actions, new CongressBillActionsPage([], null));

        Assert.Equal(2, afterFirst.Count);
        Assert.Equal(3, all.Count);
        Assert.All(afterFirst, candidate => Assert.Contains(candidate, all));
        Assert.Equal(all, await CandidatesAsync());
        Assert.Equal(new[] { (false, false), (!subjectsFirst, subjectsFirst), (true, true) }.Order(),
            all.Select(candidate => (candidate.IncludesSummaries, candidate.IncludesSubjects)).Order());
        Assert.Equal(3L, await ScalarAsync("SELECT count(*) FROM action_analysis"));
    }

    [Fact]
    public async Task ASubjectsChainThatClosesBeforeTheGatePassesIsIncludedInTheFirstCandidate()
    {
        var detail = await EnqueueDetailAsync("pending");
        var result = await AdmitDetailAsync(detail, DetailBill(UpdatedAt, cosponsors: 1, subjects: 2), BillGateOutcome.Pending);
        var subjects = result.Jobs.Single(job => job.Url.Contains("/subjects?")).JobId;
        var cosponsors = result.Jobs.Single(job => job.Url.Contains("/cosponsors?")).JobId;
        await AdmitSubjectsAsync(subjects, new CongressBillSubjectsPage([], null));
        Assert.Empty(await CandidatesAsync());

        var passed = await AdmitCosponsorPageAsync(cosponsors, Cosponsors(["A000001"], next: null));
        await AdmitActionsAsync(passed.Jobs.Single(job => job.Url.Contains("/actions?")).JobId, new CongressBillActionsPage([], null));

        var candidate = Assert.Single(await CandidatesAsync());
        Assert.True(candidate.IncludesSubjects);
        Assert.False(candidate.IncludesSummaries);
    }

    [Fact]
    public async Task AnOlderCandidateIsSupersededByTheOneThatCoversItAndEvidenceListsOnlyThePagesItWasBuiltFrom()
    {
        var bill = await StartPassedBillAsync(cosponsors: 0, summaries: 1, subjects: 2);
        await AdmitActionsAsync(bill.Actions, new CongressBillActionsPage([], null));
        await AdmitSummariesAsync(bill.Summaries!, new CongressBillSummariesPage([], null));
        await AdmitSubjectsAsync(bill.Subjects!, new CongressBillSubjectsPage([], null));
        var candidates = await CandidatesAsync();
        var store = ActionStore();

        async Task<ActionDraftEvidence> EvidenceOf(bool summaries, bool subjects) => (await store.ReadEvidenceAsync(
            candidates.Single(item => item.IncludesSummaries == summaries && item.IncludesSubjects == subjects).CandidateId, default))!;
        var none = await EvidenceOf(false, false);
        var summariesOnly = await EvidenceOf(true, false);
        var both = await EvidenceOf(true, true);

        Assert.Equal([true, true, false], [none.HasNewerCandidate, summariesOnly.HasNewerCandidate, both.HasNewerCandidate]);
        Assert.Equal([ApiResource.Bill], none.Pages.Select(page => page.Resource));
        Assert.Equal([ApiResource.Bill, ApiResource.BillSummaries], summariesOnly.Pages.Select(page => page.Resource));
        Assert.Equal([ApiResource.Bill, ApiResource.BillSummaries, ApiResource.BillSubjects], both.Pages.Select(page => page.Resource));
        Assert.True(both.Candidate.IncludesSubjects);
        Assert.False(summariesOnly.Candidate.IncludesSubjects);
    }

    [Fact]
    public async Task ASubjectsOnlyCandidateIsTheNewestWhileTheSummariesChainStaysOpen()
    {
        var bill = await StartPassedBillAsync(cosponsors: 0, summaries: 1, subjects: 2);
        await AdmitActionsAsync(bill.Actions, new CongressBillActionsPage([], null));
        await AdmitSubjectsAsync(bill.Subjects!, new CongressBillSubjectsPage([], null));
        var subjectsOnly = (await CandidatesAsync()).Single(item => item.IncludesSubjects);

        var evidence = (await ActionStore().ReadEvidenceAsync(subjectsOnly.CandidateId, default))!;

        Assert.False(evidence.HasNewerCandidate);
        Assert.Equal([ApiResource.Bill, ApiResource.BillSubjects], evidence.Pages.Select(page => page.Resource));
    }

    [Fact]
    public async Task TheCandidateIdsOfTheOldInputCombinationsStayTheSameAndTheSubjectsSuffixComesLast()
    {
        var bill = await StartPassedBillAsync(cosponsors: 0, summaries: 1, subjects: 2);
        await AdmitActionsAsync(bill.Actions, new CongressBillActionsPage([], null));
        await AdmitSummariesAsync(bill.Summaries!, new CongressBillSummariesPage([], null));
        await AdmitSubjectsAsync(bill.Subjects!, new CongressBillSubjectsPage([], null));

        string IdOf(string suffix) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{SourceId.Length}:{SourceId}119:hr:7:{UpdatedAt.UtcTicks}{suffix}")));

        Assert.Equal(new[] { IdOf(""), IdOf(":summaries"), IdOf(":summaries:subjects") }.Order(), (await CandidatesAsync()).Select(item => item.CandidateId).Order());
    }

    [Fact]
    public async Task TheBackfillAdmitsTheChainOfTheNewestVersionOnceStoresTheCountAndNeverListsAnExaminedBillAgain()
    {
        var older = await EnqueueDetailAsync("older");
        await AdmitDetailAsync(older, DetailBill(UpdatedAt.AddDays(-1), cosponsors: 0), BillGateOutcome.Passed(BillGateReason.CoveredSponsor));
        var newest = await EnqueueDetailAsync("newest");
        await AdmitDetailAsync(newest, DetailBill(UpdatedAt, cosponsors: 0), BillGateOutcome.Passed(BillGateReason.CoveredSponsor));
        await ExecuteAsync(connectionString, "UPDATE bill_gates SET subject_count = NULL");
        var backfill = (IBillSubjectsBackfillStore)store;

        Assert.Equal([newest], await backfill.ListNewestBillsWithoutSubjectCountAsync(10, default));
        var admitted = await backfill.AdmitSubjectsChainAsync(Source(), newest, 5, default);
        var replay = await backfill.AdmitSubjectsChainAsync(Source(), newest, 5, default);

        Assert.True(admitted);
        Assert.False(replay);
        Assert.Equal(5, await ScalarAsync($"SELECT subject_count FROM bill_gates WHERE detail_job_id = '{newest}'"));
        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM bill_follow_on_jobs l JOIN collection_jobs j ON j.job_id = l.job_id WHERE j.definition_json::jsonb ->> 'apiResource' = 'billSubjects'"));
        Assert.Empty(await backfill.ListNewestBillsWithoutSubjectCountAsync(10, default));
    }

    [Fact]
    public async Task TheBackfillOfAPolicyOnlyBillAdmitsNothingButRecordsTheCountSoItIsNotListedAgain()
    {
        var first = await EnqueueDetailAsync("first");
        await AdmitDetailAsync(first, DetailBill(UpdatedAt, cosponsors: 0), BillGateOutcome.Passed(BillGateReason.CoveredSponsor));
        await ExecuteAsync(connectionString, "UPDATE bill_gates SET subject_count = NULL");
        var backfill = (IBillSubjectsBackfillStore)store;

        var admitted = await backfill.AdmitSubjectsChainAsync(Source(), first, 1, default);

        Assert.False(admitted);
        Assert.Equal(1, await ScalarAsync($"SELECT subject_count FROM bill_gates WHERE detail_job_id = '{first}'"));
        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM bill_follow_on_jobs l JOIN collection_jobs j ON j.job_id = l.job_id WHERE j.definition_json::jsonb ->> 'apiResource' = 'billSubjects'"));
        Assert.Empty(await backfill.ListNewestBillsWithoutSubjectCountAsync(10, default));
    }

    [Fact]
    public async Task TheBackfillExaminesEveryRowWhateverTheLimitBecauseARealZeroIsStoredAndLeavesTheList()
    {
        var counts = new Dictionary<string, int>();
        foreach (var (number, count) in new[] { (11, 0), (12, 0), (13, 0), (14, 3) })
        {
            var detail = await EnqueueDetailAsync("bill-" + number, number);
            await AdmitDetailAsync(detail, DetailBill(UpdatedAt, cosponsors: 0, number: number), BillGateOutcome.Passed(BillGateReason.CoveredSponsor));
            counts[detail] = count;
        }
        await ExecuteAsync(connectionString, "UPDATE bill_gates SET subject_count = NULL");
        var backfill = (IBillSubjectsBackfillStore)store;

        var examined = new List<string>();
        for (var ids = await backfill.ListNewestBillsWithoutSubjectCountAsync(2, default); ids.Count > 0;
            ids = await backfill.ListNewestBillsWithoutSubjectCountAsync(2, default))
        {
            Assert.True(ids.Count <= 2);
            foreach (var id in ids) await backfill.AdmitSubjectsChainAsync(Source(), id, counts[id], default);
            examined.AddRange(ids);
        }

        Assert.Equal(counts.Keys.Order(), examined.Order());
        Assert.Empty(await backfill.ListNewestBillsWithoutSubjectCountAsync(2, default));
        Assert.Equal(3L, await ScalarAsync("SELECT count(*) FROM bill_gates WHERE subject_count = 0"));
        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM bill_follow_on_jobs l JOIN collection_jobs j ON j.job_id = l.job_id WHERE j.definition_json::jsonb ->> 'apiResource' = 'billSubjects'"));
    }

    [Fact]
    public async Task ASubjectsChainThatTheBackfillClosesAddsTheCandidateWithSubjectsToAPassedBillThatAlreadyHasOne()
    {
        var bill = await StartPassedBillAsync(cosponsors: 0, summaries: 0);
        await AdmitActionsAsync(bill.Actions, new CongressBillActionsPage([], null));
        Assert.Single(await CandidatesAsync());
        await ExecuteAsync(connectionString, "UPDATE bill_gates SET subject_count = NULL");
        var backfill = (IBillSubjectsBackfillStore)store;
        await backfill.AdmitSubjectsChainAsync(Source(), bill.Detail, 4, default);
        var subjectsJob = (await AdmittedJobsAsync()).Single(job => job.Definition.Url.Contains("/subjects?")).JobId;

        await AdmitSubjectsAsync(subjectsJob, new CongressBillSubjectsPage([], null));

        Assert.Equal([false, true], (await CandidatesAsync()).Select(candidate => candidate.IncludesSubjects).Order());
    }

    [Fact]
    public async Task RollingTheSubjectsMigrationBackRefusesWithAClearMessageWhenARevisionCameFromACandidateWithSubjectsAndChangesNothing()
    {
        var candidate = await ThreeCandidatesWithSubjectsAsync();
        var claim = (await ActionStore().TryClaimAsync(candidate, Lease, default))!;
        var run = await ReserveAsync(ActionStore(), claim);
        Assert.True(await ActionStore().CheckpointAsync(ActionDraftCheckpoint.From(claim, Revision(candidate, 1, includesSummaries: true, includesSubjects: true)),
            DraftedRun(run, candidate), default));

        var exception = await Assert.ThrowsAnyAsync<Exception>(RollBackAsync);

        Assert.Contains("Cannot roll back BillSubjects", exception.ToString(), StringComparison.Ordinal);
        Assert.Equal(3L, await ScalarAsync("SELECT count(*) FROM action_candidates"));
        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM bill_pages WHERE role = 'Subjects'"));
        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM action_revisions"));
    }

    [Fact]
    public async Task RollingTheSubjectsMigrationBackDeletesAChainOfRunsLinkedByThePreviousRun()
    {
        var candidate = await ThreeCandidatesWithSubjectsAsync();
        var store = ActionStore();
        var first = (await store.TryClaimAsync(candidate, Lease, default))!;
        var firstRun = await ReserveAsync(store, first);
        Assert.True(await store.CheckpointAsync(AnalysisCheckpoint.From(first, State.Blocked, ActionDraftErrorCodes.ExtractionVersionChanged),
            DraftedRun(firstRun, candidate), default));
        Assert.Equal(1, await store.RequeueBlockedAsync(ActionDraftErrorCodes.ExtractionVersionChanged, default));
        var second = (await store.TryClaimAsync(candidate, Lease, default))!;
        var secondRun = await ReserveAsync(store, second);
        Assert.True(await store.CheckpointAsync(AnalysisCheckpoint.From(second, State.Blocked, ActionDraftErrorCodes.ExtractionVersionChanged),
            DraftedRun(secondRun, candidate, previousRunId: firstRun), default));
        Assert.Equal(2L, await ScalarAsync($"SELECT count(*) FROM action_analysis_runs WHERE candidate_id = '{candidate}'"));

        await RollBackAsync();

        Assert.Equal(2L, await ScalarAsync("SELECT count(*) FROM action_candidates"));
        Assert.Equal(0L, await ScalarAsync($"SELECT count(*) FROM action_analysis_runs WHERE candidate_id = '{candidate}'"));
        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM bill_pages WHERE role = 'Subjects'"));
    }

    private async Task<string> ThreeCandidatesWithSubjectsAsync()
    {
        var bill = await StartPassedBillAsync(cosponsors: 0, summaries: 1, subjects: 2);
        await AdmitActionsAsync(bill.Actions, new CongressBillActionsPage([], null));
        await AdmitSummariesAsync(bill.Summaries!, new CongressBillSummariesPage([], null));
        await AdmitSubjectsAsync(bill.Subjects!, new CongressBillSubjectsPage([], null));
        return (await CandidatesAsync()).Single(candidate => candidate.IncludesSummaries && candidate.IncludesSubjects).CandidateId;
    }

    private async Task RollBackAsync()
    {
        await using var db = new CollectionAttemptDbContext(new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options);
        await db.Database.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>().MigrateAsync("20261030001114_DropRosterLisId");
    }

    [Fact]
    public async Task RollingTheSubjectsMigrationBackDropsTheSubjectRowsFirstSoTheOldIndexCanBeBuiltOnRealData()
    {
        await ThreeCandidatesWithSubjectsAsync();
        Assert.Equal(3L, await ScalarAsync("SELECT count(*) FROM action_candidates"));

        await RollBackAsync();

        Assert.Equal(2L, await ScalarAsync("SELECT count(*) FROM action_candidates"));
        Assert.Equal(2L, await ScalarAsync("SELECT count(*) FROM action_analysis"));
        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM bill_pages WHERE role = 'Subjects'"));
    }

    [Fact]
    public async Task TheOlderCandidateReadsNoSummaryPageEvenAfterTheSummariesArrive()
    {
        var bill = await StartPassedBillAsync(cosponsors: 0, summaries: 1);
        await AdmitActionsAsync(bill.Actions, new CongressBillActionsPage([], null));
        await AdmitSummariesAsync(bill.Summaries!, new CongressBillSummariesPage([], null));
        var candidates = await CandidatesAsync();

        var older = (await ActionStore().ReadEvidenceAsync(candidates.Single(item => !item.IncludesSummaries).CandidateId, default))!;
        var newer = (await ActionStore().ReadEvidenceAsync(candidates.Single(item => item.IncludesSummaries).CandidateId, default))!;

        Assert.Equal([ApiResource.Bill], older.Pages.Select(page => page.Resource));
        Assert.False(older.Candidate.IncludesSummaries);
        Assert.Equal([ApiResource.Bill, ApiResource.BillSummaries], newer.Pages.Select(page => page.Resource));
        Assert.True(newer.Candidate.IncludesSummaries);
    }

    [Fact]
    public async Task ANewerCandidateThatArrivesAfterTheClaimSupersedesTheAppendSoTheOlderDraftNeverBecomesARevision()
    {
        var bill = await StartPassedBillAsync(cosponsors: 0, summaries: 1);
        await AdmitActionsAsync(bill.Actions, new CongressBillActionsPage([], null));
        var older = Assert.Single(await CandidatesAsync()).CandidateId;
        var store = ActionStore();
        var olderClaim = (await store.TryClaimAsync(older, Lease, default))!;
        var olderRun = await ReserveAsync(store, olderClaim);
        await AdmitSummariesAsync(bill.Summaries!, new CongressBillSummariesPage([], null));

        var settled = await store.CheckpointAsync(ActionDraftCheckpoint.From(olderClaim, Revision(older, 1)),
            DraftedRun(olderRun, older), default);

        Assert.True(settled);
        Assert.Equal(("Succeeded", ActionDraftErrorCodes.SupersededByNewerCandidate),
            (await QueueAsync()).Where(row => row.CandidateId == older).Select(row => (row.Status, row.ErrorCode)).Single());
        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM action_revisions"));
        Assert.Equal("DraftDiscarded", await ScalarAsync($"SELECT outcome FROM action_analysis_runs WHERE run_id = '{olderRun}'"));
        Assert.Equal(1_000L, await ScalarAsync("SELECT charged_tokens FROM action_analysis_budget WHERE task = 'action-draft'"));
    }

    [Fact]
    public async Task ADelayedOlderCandidateIsSupersededSoItsStaleContentNeverBecomesTheNewestRevision()
    {
        var march3 = await ReadableCandidateAsync(sponsor: "A000001");
        await RunActionWorkerAsync(new FakeHeadlineSummaryModel().Draft(DraftableOutput));
        var march4 = await ReadableCandidateAsync(sponsor: "B000002", UpdatedAt.AddDays(1));
        var march5 = await ReadableCandidateAsync(sponsor: "A000001", UpdatedAt.AddDays(2));
        await ExecuteAsync(connectionString,
            $"UPDATE action_analysis SET status = 'RetryWaiting', retry_at = 3155378975999999999 WHERE candidate_id = '{march4}'");
        Assert.Empty((await RunActionWorkerAsync(new FakeHeadlineSummaryModel())).Requests);
        await ExecuteAsync(connectionString, $"UPDATE action_analysis SET status = 'Pending', retry_at = NULL WHERE candidate_id = '{march4}'");

        var model = await RunActionWorkerAsync(new FakeHeadlineSummaryModel());

        Assert.Empty(model.Requests);
        var queue = (await QueueAsync()).ToDictionary(row => row.CandidateId, row => (row.Status, row.ErrorCode));
        Assert.Equal(("Succeeded", (string?)null), queue[march3]);
        Assert.Equal(("Succeeded", ActionDraftErrorCodes.SupersededByNewerCandidate), queue[march4]);
        Assert.Equal(("Succeeded", ActionDraftErrorCodes.Unchanged), queue[march5]);
        Assert.Equal(march3, (await ActionStore().ReadEvidenceAsync(march4, default))!.Current!.CandidateId);
        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM action_revisions"));
    }

    [Fact]
    public async Task ALaterUpdateDateWithoutSummariesSupersedesAnEarlierCandidateWithSummariesBecauseTheDateDecidesFirst()
    {
        var (_, earlierWithSummaries) = await BothCandidatesPendingAsync();
        await ReadableCandidateAsync(sponsor: "A000001", UpdatedAt.AddDays(1));

        var evidence = (await ActionStore().ReadEvidenceAsync(earlierWithSummaries, default))!;

        Assert.True(evidence.Candidate.IncludesSummaries);
        Assert.True(evidence.HasNewerCandidate);
    }

    [Fact]
    public async Task ABackfilledBillClaimsItsOlderCandidateFirstButOnlyTheCandidateWithSummariesCallsTheModel()
    {
        var (older, newer) = await BothCandidatesPendingAsync();
        var store = ActionStore();
        Assert.Equal([older, newer], (await store.GetEligibleAsync(10, default)).Select(row => row.SubjectId));

        var model = await RunActionWorkerAsync(new FakeHeadlineSummaryModel().Draft(DraftableOutput));

        Assert.Single(model.Requests);
        var current = (await store.ReadEvidenceAsync(older, default))!.Current!;
        Assert.Equal((newer, UpdatedAt, true), (current.CandidateId, current.CandidateUpdateDate, current.CandidateIncludesSummaries));
        Assert.Equal([(newer, "action-draft", "Succeeded", null, 0), (older, "action-draft", "Succeeded", ActionDraftErrorCodes.SupersededByNewerCandidate, 0)],
            (await QueueAsync()).OrderBy(row => row.CandidateId == older));
        Assert.Equal(0L, await ScalarAsync($"SELECT count(*) FROM action_analysis_runs WHERE candidate_id = '{older}'"));
    }

    [Fact]
    public async Task ABillWithNoTrackedOfficialSettlesWithABypassedRunThatKeepsTheSnapshotContext()
    {
        var candidate = await ReadableCandidateAsync(sponsor: "Z000009");

        var model = await RunActionWorkerAsync(new FakeHeadlineSummaryModel());

        Assert.Empty(model.Requests);
        Assert.Equal([(candidate, "action-draft", "Succeeded", ActionDraftErrorCodes.NoTrackedOfficial, 0)], await QueueAsync());
        var (reason, contextJson) = await TupleAsync($"SELECT reason, context_json FROM action_analysis_runs WHERE candidate_id = '{candidate}' AND outcome = 'Bypassed'");
        Assert.Equal(ActionDraftErrorCodes.NoTrackedOfficial, reason);
        using var context = JsonDocument.Parse(contextJson);
        Assert.Empty(context.RootElement.GetProperty("officials").EnumerateArray());
        Assert.Equal(ActionFingerprint.Compute([]), context.RootElement.GetProperty("fingerprint").GetString());
        Assert.Equal(BillGateFixtures.Revision().Id, context.RootElement.GetProperty("issueRevisionId").GetString());
    }

    [Fact]
    public async Task AnUnchangedBillSettlesWithABypassedRunThatKeepsTheSameContextAsTheDraftedRun()
    {
        var candidate = await ReadableCandidateAsync(sponsor: "A000001");
        await RunActionWorkerAsync(new FakeHeadlineSummaryModel().Draft(DraftableOutput));
        await ExecuteAsync(connectionString, $"UPDATE action_analysis SET status = 'Pending' WHERE candidate_id = '{candidate}'");

        var model = await RunActionWorkerAsync(new FakeHeadlineSummaryModel());

        Assert.Empty(model.Requests);
        Assert.Equal([(candidate, "action-draft", "Succeeded", ActionDraftErrorCodes.Unchanged, 0)], await QueueAsync());
        var drafted = await ScalarAsync("SELECT context_json FROM action_analysis_runs WHERE outcome = 'Drafted'");
        Assert.Equal((ActionDraftErrorCodes.Unchanged, drafted!.ToString()!),
            await TupleAsync("SELECT reason, context_json FROM action_analysis_runs WHERE outcome = 'Bypassed'"));
    }

    [Fact]
    public async Task AnUnchangedOrNoOfficialSettlementWithoutItsRunIsRefused()
    {
        var candidate = await CandidateWithoutSummariesAsync();
        var store = ActionStore();
        var claim = (await store.TryClaimAsync(candidate, Lease, default))!;

        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.CheckpointAsync(AnalysisCheckpoint.From(claim, State.Succeeded, ActionDraftErrorCodes.Unchanged), null, default));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.CheckpointAsync(AnalysisCheckpoint.From(claim, State.Succeeded, ActionDraftErrorCodes.NoTrackedOfficial), null, default));
        Assert.Equal([(candidate, "action-draft", "Running", null, 0)], await QueueAsync());
    }

    [Fact]
    public async Task WhenTheCandidateWithSummariesBlocksTheOlderCandidateIsStillSupersededAndNeverDrafts()
    {
        var (older, newer) = await BothCandidatesPendingAsync();
        var store = ActionStore();
        var claim = (await store.TryClaimAsync(newer, Lease, default))!;
        Assert.True(await store.CheckpointAsync(AnalysisCheckpoint.From(claim, State.Blocked, ActionDraftErrorCodes.ExtractionVersionChanged),
            null, default));

        var model = await RunActionWorkerAsync(new FakeHeadlineSummaryModel());

        Assert.Empty(model.Requests);
        Assert.Equal(("Succeeded", ActionDraftErrorCodes.SupersededByNewerCandidate),
            (await QueueAsync()).Where(row => row.CandidateId == older).Select(row => (row.Status, row.ErrorCode)).Single());
        Assert.Null((await store.ReadEvidenceAsync(older, default))!.Current);
    }

    [Theory]
    [InlineData(ActionDraftErrorCodes.TitleTooLong)]
    [InlineData(nameof(InvalidDataException))]
    public async Task RequeueBlockedMovesOnlyBlockedEntriesWithTheGivenCodeAndResetsTheirAttempts(string code)
    {
        var requeued = await ReadableCandidateAsync(sponsor: "A000001");
        var overByteCap = await ReadableCandidateAsync(sponsor: "A000001", UpdatedAt.AddDays(1));
        var failed = await ReadableCandidateAsync(sponsor: "A000001", UpdatedAt.AddDays(2));
        await ExecuteAsync(connectionString, $"""
            UPDATE action_analysis SET attempts = 2, status = CASE candidate_id WHEN '{failed}' THEN 'Failed' ELSE 'Blocked' END,
                error_code = CASE candidate_id WHEN '{overByteCap}' THEN '{ActionDraftErrorCodes.OverByteCap}' ELSE '{code}' END
            """);

        Assert.Equal(1, await ActionStore().RequeueBlockedAsync(code, default));

        var queue = (await QueueAsync()).ToDictionary(row => row.CandidateId, row => (row.Status, row.ErrorCode, row.Attempts));
        Assert.Equal(("Pending", (string?)null, 0), queue[requeued]);
        Assert.Equal(("Blocked", ActionDraftErrorCodes.OverByteCap, 2), queue[overByteCap]);
        Assert.Equal(("Failed", code, 2), queue[failed]);
    }

    [Fact]
    public async Task RedraftMovesASettledEntryWhoseNewestDraftRunUsedAnOlderPromptAndLeavesASupersededEntryAndResetsAttempts()
    {
        var (older, newer) = await TwoSettledCandidatesAsync(ActionDraftErrorCodes.SupersededByNewerCandidate, null);
        await InsertRunAsync(older, "action-draft-prompt-v1", 1);
        await InsertRunAsync(newer, "action-draft-prompt-v1", 2);

        Assert.Equal(1, await ActionStore().RequeueOlderPromptAsync(default));

        var queue = (await QueueAsync()).ToDictionary(row => row.CandidateId, row => (row.Status, row.Attempts));
        Assert.Equal(("Succeeded", 2), queue[older]);
        Assert.Equal(("Pending", 0), queue[newer]);
    }

    [Fact]
    public async Task RedraftMovesNothingWhenTheNewestDraftRunOfTheBillUsedTheCurrentPrompt()
    {
        var (older, newer) = await TwoSettledCandidatesAsync(ActionDraftErrorCodes.Unchanged, null);
        await InsertRunAsync(older, "action-draft-prompt-v1", 1);
        await InsertRunAsync(newer, ActionDraftTask.PromptVersion, 2);

        Assert.Equal(0, await ActionStore().RequeueOlderPromptAsync(default));

        Assert.All(await QueueAsync(), row => Assert.Equal(("Succeeded", 2), (row.Status, row.Attempts)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(ActionDraftErrorCodes.Unchanged)]
    public async Task RedraftIgnoresANewerBypassedRunBecauseOnlyTheNewestDraftedRunNamesThePromptTheDraftCameFrom(string? settledCode)
    {
        var (older, newer) = await TwoSettledCandidatesAsync(ActionDraftErrorCodes.SupersededByNewerCandidate, settledCode);
        await InsertRunAsync(newer, "action-draft-prompt-v1", 1);
        await InsertRunAsync(newer, ActionDraftTask.PromptVersion, 2, "Bypassed");

        Assert.Equal(1, await ActionStore().RequeueOlderPromptAsync(default));

        var queue = (await QueueAsync()).ToDictionary(row => row.CandidateId, row => row.Status);
        Assert.Equal("Pending", queue[newer]);
        Assert.Equal("Succeeded", queue[older]);
    }

    private async Task<(string Older, string Newer)> TwoSettledCandidatesAsync(string? olderCode, string? newerCode)
    {
        var older = await ReadableCandidateAsync(sponsor: "A000001");
        var newer = await ReadableCandidateAsync(sponsor: "A000001", UpdatedAt.AddDays(1));
        await ExecuteAsync(connectionString, $"""
            UPDATE action_analysis SET status = 'Succeeded', attempts = 2,
                error_code = CASE candidate_id WHEN '{older}' THEN {SqlLiteral(olderCode)} ELSE {SqlLiteral(newerCode)} END
            """);
        return (older, newer);
    }

    private static string SqlLiteral(string? value) => value is null ? "NULL" : $"'{value}'";

    private async Task InsertRunAsync(string candidate, string promptVersion, long startedAtTicks, string outcome = "Drafted") => await ExecuteAsync(connectionString, $"""
        INSERT INTO action_analysis_runs (run_id, candidate_id, task, task_version, model, prompt_version, schema_version, input_hash, outcome,
            input_tokens, output_tokens, cache_read_tokens, cache_write_tokens, charged_tokens, started_at_utc_ticks, finished_at_utc_ticks)
        VALUES ('{Guid.NewGuid():N}', '{candidate}', '{ActionDraftTask.Task}', '{ActionDraftTask.TaskVersion}', '{ActionDraftTask.Model}', '{promptVersion}',
            '{ActionDraftTask.SchemaVersion}', '{new string('1', 64)}', '{outcome}', 0, 0, 0, 0, 0, {startedAtTicks}, {startedAtTicks})
        """);

    [Fact]
    public async Task ADraftedSettlementWritesTheDraftTheRevisionTheRunAndTheBudgetInOneTransaction()
    {
        var candidate = await CandidateWithoutSummariesAsync();
        var store = ActionStore();
        var claim = (await store.TryClaimAsync(candidate, Lease, default))!;
        var runId = await ReserveAsync(store, claim);

        var settled = await store.CheckpointAsync(ActionDraftCheckpoint.From(claim, Revision(candidate, 1)),
            DraftedRun(runId, candidate), default);

        Assert.True(settled);
        Assert.Equal([(candidate, "action-draft", "Succeeded", null, 0)], await QueueAsync());
        Assert.Equal(1, await ScalarAsync($"SELECT current_revision_number FROM action_drafts WHERE record_id = '{RecordId}'"));
        Assert.Equal(("Analysis", runId), await TupleAsync($"SELECT author_kind, run_id FROM action_revisions WHERE record_id = '{RecordId}' AND revision_number = 1"));
        Assert.Equal(1_000L, await ScalarAsync("SELECT charged_tokens FROM action_analysis_budget WHERE task = 'action-draft'"));
        Assert.Equal(("hr", "7"), await TupleAsync($"SELECT bill_type, bill_number FROM action_drafts WHERE record_id = '{RecordId}'"));
    }

    [Fact]
    public async Task ASecondCandidateOfTheSameBillAppendsRevisionTwoToTheSameDraft()
    {
        var candidate = await CandidateWithoutSummariesAsync();
        await SettleAsync(candidate, Revision(candidate, 1));
        await ExecuteAsync(connectionString, $"UPDATE action_analysis SET status = 'Pending' WHERE candidate_id = '{candidate}'");

        await SettleAsync(candidate, Revision(candidate, 2));

        Assert.Equal(2, await ScalarAsync($"SELECT current_revision_number FROM action_drafts WHERE record_id = '{RecordId}'"));
        Assert.Equal(2L, await ScalarAsync($"SELECT count(*) FROM action_revisions WHERE record_id = '{RecordId}'"));
        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM action_drafts"));
    }

    [Fact]
    public async Task APersonsConcurrentRevisionFailsTheExpectedVersionCheckSoTheRunRetriesWithBackoffAndAppendsNothing()
    {
        var candidate = await CandidateWithoutSummariesAsync();
        var store = ActionStore();
        var claim = (await store.TryClaimAsync(candidate, Lease, default))!;
        var runId = await ReserveAsync(store, claim);
        await ExecuteAsync(connectionString, $"""
            INSERT INTO action_drafts (record_id, source_id, congress, bill_type, bill_number, current_revision_number, review_state_version,
                review_status, created_at_utc_ticks, last_revised_at_utc_ticks)
            VALUES ('{RecordId}', '{SourceId}', 119, 'hr', 7, 1, 0, 'AwaitingReview', 0, 0);
            INSERT INTO action_revisions (record_id, revision_number, author_subject, author_kind, run_id, revision_json, created_at_utc_ticks)
            VALUES ('{RecordId}', 1, 'auth0|editor', 'Person', NULL, 'x', 0)
            """);

        var settled = await store.CheckpointAsync(ActionDraftCheckpoint.From(claim, Revision(candidate, 1)),
            DraftedRun(runId, candidate), default);

        Assert.True(settled);
        var row = Assert.Single(await QueueAsync());
        Assert.Equal(("RetryWaiting", ActionDraftErrorCodes.RevisionConflict, 1), (row.Status, row.ErrorCode, row.Attempts));
        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM action_revisions"));
        Assert.Equal("DraftDiscarded", await ScalarAsync($"SELECT outcome FROM action_analysis_runs WHERE run_id = '{runId}'"));
        Assert.True((long)(await ScalarAsync("SELECT retry_at FROM action_analysis"))! > 0);
        Assert.Equal(1_000L, await ScalarAsync("SELECT charged_tokens FROM action_analysis_budget WHERE task = 'action-draft'"));
    }

    [Fact]
    public async Task ABypassedRunStaysBypassedWhenItsRevisionConflictsSoTheEntryRetries()
    {
        var candidate = await CandidateWithoutSummariesAsync();
        var store = ActionStore();
        var claim = (await store.TryClaimAsync(candidate, Lease, default))!;
        await ExecuteAsync(connectionString, $"""
            INSERT INTO action_drafts (record_id, source_id, congress, bill_type, bill_number, current_revision_number, review_state_version,
                review_status, created_at_utc_ticks, last_revised_at_utc_ticks)
            VALUES ('{RecordId}', '{SourceId}', 119, 'hr', 7, 1, 0, 'AwaitingReview', 0, 0);
            INSERT INTO action_revisions (record_id, revision_number, author_subject, author_kind, run_id, revision_json, created_at_utc_ticks)
            VALUES ('{RecordId}', 1, 'auth0|editor', 'Person', NULL, 'x', 0)
            """);
        var run = DraftedRun(Guid.NewGuid().ToString("N"), candidate);
        var bypassed = new ActionDraftRun(run.Run with { Outcome = AnalysisRunOutcome.Bypassed, Usage = AnalysisTokenUsage.None, ChargedTokens = 0 },
            ActionDraftErrorCodes.SponsorshipsChanged);

        Assert.True(await store.CheckpointAsync(ActionDraftCheckpoint.From(claim, Revision(candidate, 1)), bypassed, default));

        var row = Assert.Single(await QueueAsync());
        Assert.Equal(("RetryWaiting", ActionDraftErrorCodes.RevisionConflict), (row.Status, row.ErrorCode));
        Assert.Equal("Bypassed", await ScalarAsync($"SELECT outcome FROM action_analysis_runs WHERE run_id = '{run.Run.RunId}'"));
        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM action_revisions"));
    }

    [Fact]
    public async Task AnAppendWaitsForTheActionReviewLockSoAHumanSaveAndTheWorkerNeverInterleave()
    {
        var candidate = await CandidateWithoutSummariesAsync();
        var store = ActionStore();
        var claim = (await store.TryClaimAsync(candidate, Lease, default))!;
        var runId = await ReserveAsync(store, claim);
        Task<bool> settling;
        await using (var holder = await HeldAdvisoryLock.AcquireAsync(connectionString, "civic-lens-action-review"))
        {
            settling = store.CheckpointAsync(ActionDraftCheckpoint.From(claim, Revision(candidate, 1)),
                DraftedRun(runId, candidate), default);
            await HeldAdvisoryLock.WaitForWaiterAsync(connectionString, "civic-lens-action-review");
            Assert.False(settling.IsCompleted);
        }

        Assert.True(await settling);
    }

    [Fact]
    public async Task EvidenceHoldsTheDetailCosponsorAndSummaryPagesInOrderButNoActionsPageAndTheCurrentRevision()
    {
        var bill = await StartPassedBillAsync(cosponsors: 1, summaries: 1);
        await AdmitCosponsorPageAsync(bill.Cosponsors!, Cosponsors(["Z000009"], next: null));
        await AdmitSummariesAsync(bill.Summaries!, new CongressBillSummariesPage([], null));
        await AdmitActionsAsync(bill.Actions, new CongressBillActionsPage([], null));
        var candidate = Assert.Single(await CandidatesAsync());
        await SettleAsync(candidate.CandidateId, Revision(candidate.CandidateId, 1, includesSummaries: true));

        var evidence = (await ActionStore().ReadEvidenceAsync(candidate.CandidateId, default))!;

        Assert.Equal([ApiResource.Bill, ApiResource.BillCosponsors, ApiResource.BillSummaries], evidence.Pages.Select(page => page.Resource));
        Assert.Equal(RecordId, evidence.Candidate.RecordId);
        Assert.Equal(Source().Revision.Id, evidence.ConfigurationRevision.Id);
        Assert.Equal(1, evidence.Current!.RevisionNumber);
        Assert.True(evidence.Candidate.IncludesSummaries);
        Assert.Equal(UpdatedAt, evidence.Candidate.UpdateDate);
    }

    [Fact]
    public async Task TheRecordIdIsTheSha256OfTheLengthFramedSourceAndBillWithoutTheUpdateDate()
    {
        var identity = $"{SourceId.Length}:{SourceId}119:hr:7";
        var expected = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        var candidate = await CandidateWithoutSummariesAsync();

        var evidence = (await ActionStore().ReadEvidenceAsync(candidate, default))!;

        Assert.Equal(expected, evidence.Candidate.RecordId);
        Assert.Equal(RecordId, expected);
    }

    [Fact]
    public async Task TheDatabaseRefusesAnAnalysisRevisionWithoutARunAndAPersonRevisionWithOne()
    {
        await CandidateWithoutSummariesAsync();
        await ExecuteAsync(connectionString, $"""
            INSERT INTO action_drafts (record_id, source_id, congress, bill_type, bill_number, current_revision_number, review_state_version,
                review_status, created_at_utc_ticks, last_revised_at_utc_ticks)
            VALUES ('{RecordId}', '{SourceId}', 119, 'hr', 7, 1, 0, 'AwaitingReview', 0, 0)
            """);

        await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(connectionString, $"""
            INSERT INTO action_revisions (record_id, revision_number, author_subject, author_kind, run_id, revision_json, created_at_utc_ticks)
            VALUES ('{RecordId}', 1, 'civic-lens:analysis', 'Analysis', NULL, 'x', 0)
            """));
    }

    [Fact]
    public async Task TheDatabaseAllowsOneDraftForEachBillAndRefusesABadRecordId()
    {
        await CandidateWithoutSummariesAsync();
        const string insert = "INSERT INTO action_drafts (record_id, source_id, congress, bill_type, bill_number, current_revision_number, " +
            "review_state_version, review_status, created_at_utc_ticks, last_revised_at_utc_ticks)";
        await ExecuteAsync(connectionString, $"{insert} VALUES ('{RecordId}', '{SourceId}', 119, 'hr', 7, 1, 0, 'AwaitingReview', 0, 0)");

        await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(connectionString, $"{insert} VALUES ('{new string('a', 64)}', '{SourceId}', 119, 'hr', 7, 1, 0, 'AwaitingReview', 0)"));
        await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(connectionString, $"{insert} VALUES ('NOT-A-HASH', '{SourceId}', 119, 'hr', 8, 1, 0, 'AwaitingReview', 0)"));
    }

    private static readonly string DraftableOutput = HeadlineSummaryOutputs.Output(headline: "A sponsored a housing measure", summary: "A filed this measure.", issueIds: ["rent"]);

    private static string RecordId => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{SourceId.Length}:{SourceId}119:hr:7")));

    private PostgresActionDraftStore ActionStore() => new(new PooledDbContextFactory<CollectionAttemptDbContext>(
        new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options));

    private async Task<string> CandidateWithoutSummariesAsync()
    {
        var bill = await StartPassedBillAsync(cosponsors: 0, summaries: 0);
        await AdmitActionsAsync(bill.Actions, new CongressBillActionsPage([], null));
        return Assert.Single(await CandidatesAsync()).CandidateId;
    }

    private async Task<(string Older, string Newer)> BothCandidatesPendingAsync()
    {
        var bill = await StartReadableBillAsync(["A000001"], summaries: 1);
        await AdmitActionsAsync(bill.Actions, new CongressBillActionsPage([], null));
        var summaries = ActionBillWorld.SummaryBody(new SummarySpec("00", "2025-01-03", "2025-01-05T10:00:00Z", "<p>It funds rent aid.</p>"), null);
        await store.AdmitSummariesPageAsync(Source(), bill.Summaries!, new CongressBillSummariesPage([], null),
            await ReadableExtractionAsync(bill.Summaries!, ApiResource.BillSummaries, summaries), default);
        var candidates = await CandidatesAsync();
        var older = candidates.Single(item => !item.IncludesSummaries).CandidateId;
        await ExecuteAsync(connectionString,
            $"UPDATE action_analysis SET created_at_utc_ticks = CASE candidate_id WHEN '{older}' THEN 1 ELSE 2 END");
        return (older, candidates.Single(item => item.IncludesSummaries).CandidateId);
    }

    private async Task<string> ReadableCandidateAsync(string sponsor, DateTimeOffset? updatedAt = null, string? cosponsor = null,
        string? title = null)
    {
        var bill = await StartReadableBillAsync([sponsor], summaries: 0, updatedAt, title, cosponsors: cosponsor is null ? 0 : 1);
        if (cosponsor is not null)
        {
            var body = ActionBillWorld.CosponsorBody([new(cosponsor, Backfill.ToString("yyyy-MM-dd"))], null);
            _ = await store.AdmitCosponsorPageAsync(Source(), bill.Cosponsors!, Cosponsors([cosponsor], next: null),
                await ReadableExtractionAsync(bill.Cosponsors!, ApiResource.BillCosponsors, body), default);
        }
        await AdmitActionsAsync(bill.Actions, new CongressBillActionsPage([], null));
        return (await CandidatesAsync()).Single(candidate => candidate.DetailJobId == bill.Detail).CandidateId;
    }

    private async Task<StartedBill> StartReadableBillAsync(string[] sponsors, int summaries, DateTimeOffset? updatedAt = null,
        string? title = null, int cosponsors = 0)
    {
        var updated = updatedAt ?? UpdatedAt;
        var detail = await EnqueueDetailAsync("detail-" + updated.UtcTicks);
        var bill = DetailBill(updated, cosponsors, summaries: summaries) with { SponsorBioguideIds = sponsors };
        if (title is not null) bill = bill with { Title = title };
        var result = await store.AdmitDetailAsync(Source(), detail, bill, BillGateOutcome.Passed(BillGateReason.CoveredSponsor),
            BillGateFixtures.Revision().Id, await ReadableExtractionAsync(detail, ApiResource.Bill, ActionBillWorld.DetailBody(bill)), default);
        string? JobOf(string resource) => result.Jobs.SingleOrDefault(job => job.Url.Contains($"/{resource}?"))?.JobId;
        return new StartedBill(detail, JobOf("cosponsors"), JobOf("actions")!, JobOf("summaries"));
    }

    private async Task<string> ReadableExtractionAsync(string jobId, ApiResource resource, string body)
    {
        var url = (await store.GetAsync(jobId, default))!.Definition.Url;
        return await ExtractionAsync(jobId, body, ActionBillWorld.RenderedText(resource, url, body), ActionBillWorld.ExtractionVersionOf(resource));
    }

    private async Task<FakeHeadlineSummaryModel> RunActionWorkerAsync(FakeHeadlineSummaryModel model)
    {
        var reader = new ActionBillReader(store, new ApiResponseReader(PostgresCollectionAttemptStore.FromConnectionString(connectionString),
            new CaptureBodyReader()), new ActionBillWorld.FragmentExtractor(), captureRoot);
        await AnalysisWorkers.ActionDrafting(ActionStore(), new FakeAnalysisStatusStore(), reader, model, new ActionDraftSettings(10_000_000, concurrency: 1),
            BillGateFixtures.Revision(), TimeProvider.System).ExecuteAsync(once: true);
        return model;
    }

    private async Task SettleAsync(string candidate, ActionDraftRevision revision)
    {
        var store = ActionStore();
        var claim = (await store.TryClaimAsync(candidate, Lease, default))!;
        var runId = await ReserveAsync(store, claim);
        Assert.True(await store.CheckpointAsync(ActionDraftCheckpoint.From(claim, revision),
            DraftedRun(runId, candidate), default));
    }

    private static async Task<string> ReserveAsync(PostgresActionDraftStore store, AnalysisClaim claim)
    {
        var runId = Guid.NewGuid().ToString("N");
        Assert.Equal(AnalysisReservation.Reserved, await store.ReserveAsync(claim, runId, new string('1', 64), 5_000, 1_000_000, default));
        return runId;
    }

    private static ActionDraftRun DraftedRun(string runId, string candidate, string? previousRunId = null) => new(new AnalysisRun(runId, candidate, ActionDraftTask.Task,
        ActionDraftTask.TaskVersion, ActionDraftTask.Model, ActionDraftTask.PromptVersion, ActionDraftTask.SchemaVersion, new string('1', 64), previousRunId,
        AnalysisRunOutcome.Drafted, new(900, 100, 0, 0), 1_000, "end_turn", "req", "{}", [], "{}", DateTimeOffset.UnixEpoch,
        DateTimeOffset.UnixEpoch), null);

    private static ActionDraftRevision Revision(string candidate, int number, bool includesSummaries = false, bool includesSubjects = false)
    {
        var extraction = new string('f', 64);
        return new(RecordId, number, "civic-lens:analysis", UpdatedAt.AddMinutes(number), "Headline", "Summary.", ReviewTextOrigin.Model, null,
            SourceId, 119, "hr", 7, candidate, UpdatedAt, includesSummaries, includesSubjects, new string('d', 64), null, null, null, null, null, [], [], BillGateFixtures.Revision().Id,
            [new("warren", "W000817", ActionSponsorshipRole.Sponsor, false, new DateOnly(2025, 1, 3), null,
                [new SourceSpan(extraction, 0, 5)])], "A bill");
    }

    private async Task<IReadOnlyList<(string CandidateId, string Task, string Status, string? ErrorCode, int Attempts)>> QueueAsync()
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT candidate_id, task, status, error_code, attempts FROM action_analysis ORDER BY candidate_id", connection);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<(string CandidateId, string Task, string Status, string? ErrorCode, int Attempts)>();
        while (await reader.ReadAsync())
            rows.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3), reader.GetInt32(4)));
        return rows;
    }

    private async Task<(string, string)> TupleAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (reader.GetValue(0).ToString()!, reader.GetValue(1).ToString()!);
    }
}
