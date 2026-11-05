using CivicLens.Application.Actions;
using CivicLens.Application.Collection;
using CivicLens.Application.Collection.Bills;
using CivicLens.Tests.Fixtures;

namespace CivicLens.Tests.Infrastructure.Collection.BillStream;

public sealed partial class PostgresBillStreamTests
{
    private const string VersionsJobKeyPattern = "bill-stream/congress-bills/text/%";

    [Fact]
    public async Task TheBackfillAdmitsAVersionsJobUnderTheRevisionWithTheTextSourceAndItRunsThroughTextAdmissionToRead()
    {
        var detail = await EnqueueDetailAsync("detail");
        await AdmitDetailAsync(detail, DetailBill(UpdatedAt, cosponsors: 0), BillGateOutcome.Passed(BillGateReason.CoveredSponsor));
        var backfill = (IBillTextBackfillStore)store;
        var oldRevision = (await store.GetAsync(detail, default))!.Definition.ConfigurationRevision!;
        Assert.Equal(0L, await ScalarAsync($"SELECT count(*) FROM collection_jobs WHERE idempotency_key LIKE '{VersionsJobKeyPattern}'"));
        Assert.Null(oldRevision.Configuration.SourceOf(ApiParser.CongressBillText));

        Assert.Equal([new BillTextBackfillTarget(detail, SourceId, "119-hr-7")], await backfill.ListNewestBillsWithoutTextAsync(10, default));
        var admitted = await backfill.AdmitTextVersionsChainAsync(BillsWithText(), detail, default);

        Assert.True(admitted);
        var versions = (await AdmittedJobsAsync()).Single(job => job.Definition.Url == BillTextFixtures.VersionsUrl);
        Assert.Equal(BillsWithText().Revision.Id, versions.Definition.ConfigurationRevision!.Id);
        Assert.NotEqual(oldRevision.Id, versions.Definition.ConfigurationRevision.Id);
        Assert.Contains(await LinksAsync(), link => link.Key.Contains("/text/119/hr/7/") && link.DetailJobId == detail);
        var jobSource = new ConfiguredCollectionSource(versions.Definition.ConfigurationRevision, versions.Definition.SourceId, versions.Definition.CoverageAsOf);
        var textJob = Assert.Single((await store.AdmitTextVersionsAsync(jobSource, versions.JobId, IntroducedText, default)).Jobs).JobId;
        await AdmitTextAsync(textJob, 10, 10);
        Assert.Equal("Read", await ScalarAsync($"SELECT text_state FROM bill_gates WHERE detail_job_id = '{detail}'"));
        Assert.Empty(await backfill.ListNewestBillsWithoutTextAsync(10, default));
    }

    [Fact]
    public async Task ARerunAdmitsNoSecondVersionsJobWhileTheFirstOneIsUnread()
    {
        var detail = await EnqueueDetailAsync("detail");
        await AdmitDetailAsync(detail, DetailBill(UpdatedAt, cosponsors: 0), BillGateOutcome.Passed(BillGateReason.CoveredSponsor));
        var backfill = (IBillTextBackfillStore)store;

        var first = await backfill.AdmitTextVersionsChainAsync(BillsWithText(), detail, default);
        var rerun = await backfill.AdmitTextVersionsChainAsync(BillsWithText(), detail, default);

        Assert.True(first);
        Assert.False(rerun);
        Assert.Equal(1L, await ScalarAsync($"SELECT count(*) FROM collection_jobs WHERE idempotency_key LIKE '{VersionsJobKeyPattern}'"));
        Assert.Equal(1L, await ScalarAsync($"SELECT count(*) FROM bill_follow_on_jobs WHERE detail_job_id = '{detail}' AND job_id IN (SELECT job_id FROM collection_jobs WHERE idempotency_key LIKE '{VersionsJobKeyPattern}')"));
    }

    [Fact]
    public async Task ABillWithASummaryOrATextStateIsNeitherListedNorAdmittedAndOnlyTheNewestVersionIsListed()
    {
        var withSummary = await AdmitBillAsync("with-summary", 11, summaries: 2);
        var read = await AdmitBillAsync("read", 12);
        await ExecuteAsync(connectionString, $"UPDATE bill_gates SET text_state = 'NoText' WHERE detail_job_id = '{read}'");
        var older = await AdmitBillAsync("older", 13, UpdatedAt.AddDays(-1));
        var newest = await AdmitBillAsync("newest", 13);
        var plain = await AdmitBillAsync("plain", 14);
        var backfill = (IBillTextBackfillStore)store;

        var listed = await backfill.ListNewestBillsWithoutTextAsync(10, default);

        Assert.Equal(new[] { newest, plain }.Order(), listed.Select(target => target.DetailJobId).Order());
        foreach (var skipped in new[] { withSummary, read })
            Assert.False(await backfill.AdmitTextVersionsChainAsync(BillsWithText(), skipped, default));
        Assert.Equal(0L, await ScalarAsync($"SELECT count(*) FROM collection_jobs WHERE idempotency_key LIKE '{VersionsJobKeyPattern}'"));
        Assert.DoesNotContain(older, listed.Select(target => target.DetailJobId));
    }

    [Fact]
    public async Task AGateWithANewerGateOfTheSameBillIsNotAdmitted()
    {
        var older = await AdmitBillAsync("older", 13, UpdatedAt.AddDays(-1));
        await AdmitBillAsync("newest", 13);

        Assert.False(await ((IBillTextBackfillStore)store).AdmitTextVersionsChainAsync(BillsWithText(), older, default));
        Assert.Equal(0L, await ScalarAsync($"SELECT count(*) FROM collection_jobs WHERE idempotency_key LIKE '{VersionsJobKeyPattern}'"));
    }

    [Fact]
    public async Task AVersionsJobThatDetailAdmissionMadeBlocksTheBackfillJob()
    {
        var detail = await EnqueueDetailAsync("detail");
        await AdmitDetailWithTextAsync(detail, DetailBill(UpdatedAt, cosponsors: 0), passed: true);

        Assert.False(await ((IBillTextBackfillStore)store).AdmitTextVersionsChainAsync(BillsWithText(), detail, default));
        Assert.Equal(1L, await ScalarAsync($"SELECT count(*) FROM collection_jobs WHERE idempotency_key LIKE '{VersionsJobKeyPattern}'"));
    }

    [Fact]
    public async Task TheLimitBoundsTheList()
    {
        foreach (var number in new[] { 11, 12, 13 }) await AdmitBillAsync("bill-" + number, number);

        Assert.Equal(2, (await ((IBillTextBackfillStore)store).ListNewestBillsWithoutTextAsync(2, default)).Count);
    }

    [Fact]
    public async Task TheIdListReachesOnlyTheListedBills()
    {
        var eleven = await AdmitBillAsync("bill-11", 11);
        var twelve = await AdmitBillAsync("bill-12", 12);
        await AdmitBillAsync("bill-13", 13);
        var summarized = await AdmitBillAsync("bill-14", 14, summaries: 1);
        var backfill = (IBillTextBackfillStore)store;
        CongressBillListItem[] listed = [new(119, "hr", 12, default), new(119, "hr", 11, default), new(119, "hr", 14, default), new(119, "s", 11, default)];

        var listing = await backfill.ListNewestBillsWithoutTextAsync(listed, default);

        Assert.Equal(new[] { eleven, twelve }.Order(), listing.Targets.Select(target => target.DetailJobId).Order());
        Assert.DoesNotContain(summarized, listing.Targets.Select(target => target.DetailJobId));
        Assert.Equal([new SkippedBill("119-hr-14", BillTextSkipReason.HasSummary), new SkippedBill("119-s-11", BillTextSkipReason.NoGate)],
            listing.Skipped.OrderBy(skipped => skipped.BillId));
    }

    [Theory]
    [InlineData("Pending", 1)]
    [InlineData("Running", 1)]
    [InlineData("RetryWaiting", 1)]
    [InlineData("WaitingForBudget", 1)]
    [InlineData("Succeeded", 0)]
    [InlineData("Failed", 0)]
    [InlineData("Blocked", 0)]
    public async Task OnlyQueuedOrLeasedActionDraftsAreCountedAsPending(string status, int expected)
    {
        Assert.Equal(0, (await ActionStore().CountAsync(default))[ActionDraftView.Pending]);
        var bill = await StartPassedBillAsync(cosponsors: 0, summaries: 0);
        await AdmitActionsAsync(bill.Actions, new CongressBillActionsPage([], null));
        Assert.Equal(1, (await ActionStore().CountAsync(default))[ActionDraftView.Pending]);
        var candidate = Assert.Single(await CandidatesAsync());
        if (status == "Running") Assert.NotNull(await ActionStore().TryClaimAsync(candidate.CandidateId, Lease, default));
        else await ExecuteAsync(connectionString, $"UPDATE action_analysis SET status = '{status}', error_code = {(status == "Succeeded" ? "NULL" : "'x'")}, attempts = 2");

        Assert.Equal(expected, (await ActionStore().CountAsync(default))[ActionDraftView.Pending]);
    }

    private async Task<string> AdmitBillAsync(string name, int number, DateTimeOffset? updatedAt = null, int summaries = 0)
    {
        var detail = await EnqueueDetailAsync(name, number);
        await AdmitDetailAsync(detail, DetailBill(updatedAt ?? UpdatedAt, cosponsors: 0, number: number, summaries: summaries),
            BillGateOutcome.Passed(BillGateReason.CoveredSponsor));
        return detail;
    }
}
