using CivicLens.Application.Votes;
using CivicLens.Core.Votes;
using CivicLens.Tests.Fixtures;

namespace CivicLens.Tests.Application.Votes;

public sealed class VoteSourcesTests
{
    [Fact]
    public void BothSenateSourcesShareOneRecordKeySoASwitchBetweenThemKeepsTheSamePublicRecords()
    {
        var senateGov = SenateVoteFixtures.Configuration().Sources[0];
        var voteview = VoteviewVoteFixtures.Source(VoteviewVoteFixtures.SourceId, enabled: true);

        Assert.Equal(VoteSources.SenateRecordKey, VoteSources.RecordKeyOf(senateGov));
        Assert.Equal(VoteSources.SenateRecordKey, VoteSources.RecordKeyOf(voteview));
    }

    [Fact]
    public void AHouseSourceKeepsItsOwnIdAsRecordKeySoHouseRecordIdsDoNotChange()
    {
        var house = HouseVoteFixtures.Configuration().Sources[0];

        Assert.Equal(HouseVoteFixtures.SourceId, VoteSources.RecordKeyOf(house));
    }

    [Fact]
    public void EachVoteSourceMapsToItsOwnDataSource()
    {
        Assert.Equal([VoteSource.CongressGov, VoteSource.SenateGov, VoteSource.Voteview], new[]
        {
            HouseVoteFixtures.Configuration().Sources[0], SenateVoteFixtures.Configuration().Sources[0],
            VoteviewVoteFixtures.Source(VoteviewVoteFixtures.SourceId, enabled: true)
        }.Select(VoteSources.Of));
    }
}
