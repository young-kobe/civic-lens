using CivicLens.Application.Collection;
using CivicLens.Application.Collection.Bills;
using CivicLens.Application.Collection.HouseVotes;
using CivicLens.Tests.Application.Collection.Bills;
using CivicLens.Tests.Fixtures;

namespace CivicLens.Tests.Infrastructure.Collection.HouseVotes;

public sealed partial class PostgresHouseVoteTests
{
    private const string BillSourceId = "congress-bills";
    private const string JobLockKey = "civic-lens-collection-jobs";

    [Fact]
    public async Task AVoteWhoseBillHasNoSavedDetailGetsNoQueueRowBecauseTheDraftNeedsTheBillTitle()
    {
        var vote = HouseVoteFixtures.Vote(100, Updated);
        var (_, members) = await DetailWithMembersJobAsync(vote);

        await AdmitMembersAsync(members, vote);

        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM house_vote_candidates"));
        Assert.Empty(await QueueAsync());
    }

    [Fact]
    public async Task TheBillDetailQueuesTheWaitingVoteInTheSameTransactionThatRecordsTheBill()
    {
        var vote = HouseVoteFixtures.Vote(100, Updated);
        var (_, members) = await DetailWithMembersJobAsync(vote);
        await AdmitMembersAsync(members, vote);

        await AdmitBillDetailAsync();

        var candidate = (await CandidateAsync()).CandidateId;
        Assert.Equal([(candidate, "vote-draft", "Pending", null)], await QueueAsync());
        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM bill_gates WHERE bill_number = 14"));
    }

    [Fact]
    public async Task ALaterVersionOfTheBillAddsNoSecondQueueRowBecauseALaterDetailDoesNotRedraftAVote()
    {
        var vote = HouseVoteFixtures.Vote(100, Updated);
        var (_, members) = await DetailWithMembersJobAsync(vote);
        await AdmitMembersAsync(members, vote);
        await AdmitBillDetailAsync();

        await AdmitBillDetailAsync(Updated.AddDays(1));

        Assert.Single(await QueueAsync());
    }

    [Fact]
    public async Task TwoVersionsOfOneVoteBothWaitingForTheBillAreQueuedTogetherAndTheOlderSeesTheNewerCandidateSoTheStepSettlesItAsSuperseded()
    {
        var older = HouseVoteFixtures.Vote(100, Updated);
        var newer = HouseVoteFixtures.Vote(100, Updated.AddDays(1));
        foreach (var vote in new[] { older, newer })
        {
            var (_, members) = await DetailWithMembersJobAsync(vote);
            await AdmitMembersAsync(members, vote);
        }
        Assert.Empty(await QueueAsync());

        await AdmitBillDetailAsync();

        var queued = await QueueAsync();
        Assert.Equal(2, queued.Count);
        var olderId = (string)(await ScalarAsync($"SELECT candidate_id FROM house_vote_candidates WHERE update_date_utc_ticks = {older.UpdateDate.UtcTicks}"))!;
        Assert.True((await VoteStore().ReadEvidenceAsync(olderId, default))!.HasNewerCandidate);
    }

    [Fact]
    public async Task AVoteWhoseBillDetailWasSavedBeforeTheVoteIsQueuedAtOnce()
    {
        await AdmitBillDetailAsync();
        var vote = HouseVoteFixtures.Vote(100, Updated);
        var (_, members) = await DetailWithMembersJobAsync(vote);

        await AdmitMembersAsync(members, vote);

        Assert.Equal("Pending", Assert.Single(await QueueAsync()).Status);
    }

    [Fact]
    public async Task AVoteWithoutABillIdentityIsQueuedAtOnceBecauseItHasNoTitleToWaitFor()
    {
        var vote = HouseVoteFixtures.Vote(100, Updated, billType: null, billNumber: null);
        var (_, members) = await DetailWithMembersJobAsync(vote);

        await AdmitMembersAsync(members, vote);

        Assert.Equal("Pending", Assert.Single(await QueueAsync()).Status);
    }

    [Fact]
    public async Task TheDetailOfAnotherBillQueuesNoWaitingVote()
    {
        var vote = HouseVoteFixtures.Vote(100, Updated);
        var (_, members) = await DetailWithMembersJobAsync(vote);
        await AdmitMembersAsync(members, vote);

        await AdmitBillDetailAsync(number: 15);
        await AdmitBillDetailAsync(congress: 118);

        Assert.Empty(await QueueAsync());
    }

    [Fact]
    public async Task EvidenceHoldsTheNewestSavedBillDetailOfTheVotedBillAndNoneForAVoteWithoutABill()
    {
        await AdmitBillDetailAsync(Updated.AddDays(1));
        var newest = (await AdmitBillDetailAsync(Updated.AddDays(2))).ExtractionId;
        await AdmitBillDetailAsync(Updated);
        await AdmitBillDetailAsync(Updated.AddDays(9), number: 15);
        var withBill = await PendingCandidateAsync();
        var noBill = HouseVoteFixtures.Vote(101, Updated, billType: null, billNumber: null);
        var (_, members) = await DetailWithMembersJobAsync(noBill);
        await AdmitMembersAsync(members, noBill);
        var noBillCandidate = (string)(await ScalarAsync("SELECT candidate_id FROM house_vote_candidates WHERE bill_type IS NULL"))!;

        var evidence = (await VoteStore().ReadEvidenceAsync(withBill, default))!;

        Assert.Equal(newest, evidence.BillDetail!.ExtractionId);
        Assert.StartsWith("Bill: 119 hconres 14\nTitle: ", evidence.BillDetail.Text, StringComparison.Ordinal);
        Assert.Null((await VoteStore().ReadEvidenceAsync(noBillCandidate, default))!.BillDetail);
    }

    [Fact]
    public async Task TheVoteWriteAndTheBillDetailWriteBothWaitForTheJobLockSoAVoteAndItsBillCannotRace()
    {
        var vote = HouseVoteFixtures.Vote(100, Updated);
        var (_, members) = await DetailWithMembersJobAsync(vote);
        var bill = await PrepareBillDetailAsync();
        Task voteWrite;
        await using (var holder = await HeldAdvisoryLock.AcquireAsync(connectionString, JobLockKey))
        {
            voteWrite = AdmitMembersAsync(members, vote);
            await HeldAdvisoryLock.WaitForWaiterAsync(connectionString, JobLockKey);
            Assert.False(voteWrite.IsCompleted);
        }
        await voteWrite;
        Task billWrite;
        await using (var holder = await HeldAdvisoryLock.AcquireAsync(connectionString, JobLockKey))
        {
            billWrite = AdmitPreparedBillDetailAsync(bill);
            await HeldAdvisoryLock.WaitForWaiterAsync(connectionString, JobLockKey);
            Assert.False(billWrite.IsCompleted);
        }
        await billWrite;

        Assert.Equal("Pending", Assert.Single(await QueueAsync()).Status);
    }

    [Fact]
    public async Task TheWaitingCountHoldsOnlyVotesOnABillWithNoQueueRowAndDropsWhenTheBillDetailQueuesThem()
    {
        var waiting = HouseVoteFixtures.Vote(100, Updated);
        var withoutBill = HouseVoteFixtures.Vote(101, Updated, billType: null, billNumber: null);
        foreach (var vote in new[] { waiting, withoutBill })
        {
            var (_, members) = await DetailWithMembersJobAsync(vote);
            await AdmitMembersAsync(members, vote);
        }

        await ExecuteAsync("DELETE FROM vote_analysis WHERE candidate_id IN (SELECT candidate_id FROM house_vote_candidates WHERE bill_type IS NULL)");

        var before = await VoteStore().CountWaitingForBillAsync(default);
        await AdmitBillDetailAsync();
        var after = await VoteStore().CountWaitingForBillAsync(default);

        Assert.Equal((1, 0), (before, after));
    }

    private async Task<PreparedBillDetail> AdmitBillDetailAsync(DateTimeOffset? updatedAt = null, int congress = 119, int number = 14,
        bool hasTrackedIssue = false)
    {
        var bill = await PrepareBillDetailAsync(updatedAt, congress, number);
        await AdmitPreparedBillDetailAsync(bill, hasTrackedIssue);
        return bill;
    }

    private async Task<PreparedBillDetail> PrepareBillDetailAsync(DateTimeOffset? updatedAt = null, int congress = 119, int number = 14)
    {
        var source = new ConfiguredCollectionSource(CongressBillSourceConfigurationTests.Configuration(), BillSourceId, Backfill.AddDays(400));
        var url = $"https://api.congress.gov/v3/bill/{congress}/hconres/{number}?format=json";
        var definition = source.CreateApiJobDefinition(ApiResource.Bill, url, Backfill);
        var job = await store.EnqueueAsync(definition, TestJobKeyPrefix + "bill-" + Guid.NewGuid().ToString("N"), default);
        await ExecuteAsync($"UPDATE collection_jobs SET state = 'Succeeded' WHERE job_id = '{job.JobId}'");
        var text = $"Bill: {congress} hconres {number}\nTitle: A concurrent resolution\n{Guid.NewGuid():N}";
        var extractionId = await extractions.SaveAsync(job.JobId, text, text, "parser", "normalizer");
        var bill = new CongressBill(congress, "hconres", number, "A concurrent resolution", Backfill, updatedAt ?? Updated,
            BillGateFixtures.UntrackedPolicyArea, [], 0, 1, 0);
        return new(source, job.JobId, bill, extractionId);
    }

    private Task AdmitPreparedBillDetailAsync(PreparedBillDetail prepared, bool hasTrackedIssue = false) =>
        store.AdmitDetailAsync(prepared.Source, prepared.JobId, prepared.Bill,
            hasTrackedIssue ? BillGateOutcome.Passed(BillGateReason.TrackedPolicyArea) with { HasTrackedIssue = true }
                : BillGateOutcome.NotRelevant(BillGateReason.UntrackedPolicyArea),
            BillGateFixtures.Revision().Id, prepared.ExtractionId, default);

    private sealed record PreparedBillDetail(ConfiguredCollectionSource Source, string JobId, CongressBill Bill, string ExtractionId);
}
