using CivicLens.Application.Collection;
using CivicLens.Application.Collection.SenateVotes;
using CivicLens.Tests.Fixtures;

namespace CivicLens.Tests.Infrastructure.Collection.HouseVotes;

public sealed partial class PostgresHouseVoteTests
{
    [Fact]
    public async Task TheBillDetailQueuesNoSenateCandidateBecauseTheSenateReaderComesLater()
    {
        var source = new ConfiguredCollectionSource(SenateVoteFixtures.Configuration(), SenateVoteFixtures.SourceId, Backfill.AddDays(400));
        var url = SenateVoteUrl.Vote(SenateVoteFixtures.SourceUrl, SenateVoteFixtures.Roll1);
        var job = await store.EnqueueAsync(source.CreateApiJobDefinition(ApiResource.SenateVote, url, Backfill), TestJobKeyPrefix + "senate", default);
        await ExecuteAsync($"UPDATE collection_jobs SET state = 'Succeeded' WHERE job_id = '{job.JobId}'");
        var vote = new SenateVote(SenateVoteFixtures.Roll1, Updated, "hconres", 14);
        await store.AdmitSenateVoteAsync(source, job.JobId, vote, await ExtractionAsync(job.JobId), default);

        await AdmitBillDetailAsync();

        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM vote_candidates WHERE chamber = 'Senate'"));
        Assert.Empty(await QueueAsync());
    }
}
