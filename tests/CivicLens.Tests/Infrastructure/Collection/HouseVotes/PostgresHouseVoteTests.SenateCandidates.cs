using CivicLens.Application.Collection;
using CivicLens.Application.Collection.SenateVotes;
using CivicLens.Tests.Fixtures;

namespace CivicLens.Tests.Infrastructure.Collection.HouseVotes;

public sealed partial class PostgresHouseVoteTests
{
    [Fact]
    public async Task TheBillDetailQueuesAWaitingSenateCandidateAsItQueuesAHouseCandidate()
    {
        var source = new ConfiguredCollectionSource(SenateVoteFixtures.Configuration(), SenateVoteFixtures.SourceId, Backfill.AddDays(400));
        var item = new SenateVoteMenuItem(SenateVoteFixtures.Roll1, Backfill);
        var job = await SucceededJobs.SenateVoteAsync(store, connectionString, source, TestJobKeyPrefix + "senate", item);
        var vote = new SenateVote(SenateVoteFixtures.Roll1, Updated, "hconres", 14);
        await store.AdmitSenateVoteAsync(source, job, vote, await ExtractionAsync(job), default);

        Assert.Empty(await QueueAsync());

        await AdmitBillDetailAsync();

        var candidate = (string)(await ScalarAsync("SELECT candidate_id FROM vote_candidates WHERE chamber = 'Senate'"))!;
        Assert.Equal([(candidate, "vote-draft", "Pending", null)], await QueueAsync());
    }

    [Fact]
    public async Task TheBillDetailQueuesNoDraftForACandidateOfADisabledSourceSoASwitchedSourceNeverAppendsToTheSharedRecord()
    {
        var source = new ConfiguredCollectionSource(SenateVoteFixtures.Configuration(), SenateVoteFixtures.SourceId, Backfill.AddDays(400));
        var item = new SenateVoteMenuItem(SenateVoteFixtures.Roll1, Backfill);
        var job = await SucceededJobs.SenateVoteAsync(store, connectionString, source, TestJobKeyPrefix + "senate", item);
        await store.AdmitSenateVoteAsync(source, job, new SenateVote(SenateVoteFixtures.Roll1, Updated, "hconres", 14), await ExtractionAsync(job), default);
        await store.SynchronizeAsync(SenateVoteFixtures.Configuration(), default);
        await ExecuteAsync($"UPDATE collection_source_schedules SET enabled = false WHERE source_id = '{SenateVoteFixtures.SourceId}'");

        await AdmitBillDetailAsync();

        Assert.Empty(await QueueAsync());
    }
}
