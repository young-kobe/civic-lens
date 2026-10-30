using CivicLens.Application.Votes;
using CivicLens.Core.Votes;
using CivicLens.Tests.Fixtures;

namespace CivicLens.Tests.Application.Votes;

public sealed class VoteSourcesTests
{
    [Fact]
    public void AHouseSourceKeepsItsOwnIdAsRecordKeySoHouseRecordIdsDoNotChange()
    {
        var house = HouseVoteFixtures.Configuration().Sources[0];

        Assert.Equal(HouseVoteFixtures.SourceId, VoteSources.RecordKeyOf(house));
    }

    [Fact]
    public void EachVoteSourceMapsToItsOwnDataSource()
    {
        Assert.Equal([VoteSource.CongressGov, VoteSource.Voteview], new[]
        {
            HouseVoteFixtures.Configuration().Sources[0], VoteviewVoteFixtures.Source(VoteviewVoteFixtures.SourceId, enabled: true)
        }.Select(VoteSources.Of));
    }

    [Fact]
    public void SenateVotesKeepTheRecordKeyOfTheRetiredSenateGovSourceSoPublicRecordIdsDoNotChange()
    {
        Assert.Equal("senate-votes", VoteSources.RecordKeyOf(VoteviewVoteFixtures.Source(VoteviewVoteFixtures.SourceId, enabled: true)));
    }

    [Fact]
    public void TheRetiredSenateGovParserIsNotAVoteSource()
    {
        Assert.Throws<ArgumentException>(() => VoteSources.Of(VoteviewVoteFixtures.RetiredSenateGovSource(enabled: false)));
    }
}
