using CivicLens.Application.Collection;
using CivicLens.Application.Collection.VoteviewVotes;
using CivicLens.Tests.Fixtures;

namespace CivicLens.Tests.Application.Collection.VoteviewVotes;

public sealed class VoteviewUrlTests
{
    private const string Source = VoteviewVoteFixtures.SourceUrl;

    [Theory]
    [InlineData(ApiResource.VoteviewRollCalls, Source + "/rollcalls/S119_rollcalls.csv")]
    [InlineData(ApiResource.VoteviewVotes, Source + "/votes/S119_votes.csv")]
    [InlineData(ApiResource.VoteviewMembers, Source + "/members/S119_members.csv")]
    public void EachFileHasOneUrlThatReadsBackToItsCongress(ApiResource resource, string url)
    {
        Assert.Equal(url, VoteviewUrl.File(Source, resource, 119));
        Assert.Equal(119, VoteviewUrl.ReadCongress(Source, resource, url));
    }

    [Theory]
    [InlineData(ApiResource.VoteviewVotes, Source + "/rollcalls/S119_rollcalls.csv")]
    [InlineData(ApiResource.VoteviewRollCalls, Source + "/rollcalls/S119_votes.csv")]
    [InlineData(ApiResource.VoteviewRollCalls, Source + "/rollcalls/H119_rollcalls.csv")]
    [InlineData(ApiResource.VoteviewRollCalls, Source + "/rollcalls/S0_rollcalls.csv")]
    [InlineData(ApiResource.VoteviewRollCalls, Source + "/rollcalls/S0119_rollcalls.csv")]
    [InlineData(ApiResource.VoteviewRollCalls, Source + "/rollcalls/S119_rollcalls.csv?x=1")]
    [InlineData(ApiResource.VoteviewRollCalls, "https://other.example/rollcalls/S119_rollcalls.csv")]
    [InlineData(ApiResource.SenateVote, Source + "/rollcalls/S119_rollcalls.csv")]
    public void AJobUrlThatIsNotTheFileOfItsResourceIsRefusedSoAJobCannotFetchAnotherPath(ApiResource resource, string url)
    {
        Assert.Null(VoteviewUrl.TryReadCongress(Source, resource, url));
    }

    [Fact]
    public void EachLaterJobOfTheChainIsKeyedByItsParentJobSoTheVotesJobFindsItsSiblingsFromTheKeys()
    {
        var nextRollCalls = VoteviewJobKeys.RollCalls(VoteviewVoteFixtures.SourceId, "rollcalls-job");
        var members = VoteviewJobKeys.Members(VoteviewVoteFixtures.SourceId, "rollcalls-job");
        var votes = VoteviewJobKeys.Votes(VoteviewVoteFixtures.SourceId, "members-job");

        Assert.Equal(["rollcalls-job", "rollcalls-job", "members-job"], new[] { nextRollCalls, members, votes }.Select(ApiJobKeys.ParentJobId));
        Assert.Equal(3, new[] { nextRollCalls, members, votes }.Distinct().Count());
    }
}
