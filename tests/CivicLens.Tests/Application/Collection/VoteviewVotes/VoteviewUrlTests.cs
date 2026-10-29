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
    public void TheKeyOfTheCurrentCongressHoldsTheSweepDayAndTheKeyOfAnEarlierCongressIsFinal()
    {
        var today = new DateOnly(2026, 10, 29);

        Assert.Equal("voteview-senate/voteview-senate-votes/voteviewvotes/119/2026-10-29",
            VoteviewJobKeys.File(VoteviewVoteFixtures.SourceId, ApiResource.VoteviewVotes, 119, today));
        Assert.Equal("voteview-senate/voteview-senate-votes/voteviewvotes/118/final",
            VoteviewJobKeys.File(VoteviewVoteFixtures.SourceId, ApiResource.VoteviewVotes, 118, today));
    }

    [Fact]
    public void EachFileOfOneCongressHasItsOwnKeySoTheThreeJobsDoNotCollide()
    {
        var today = new DateOnly(2026, 10, 29);
        var keys = new[] { ApiResource.VoteviewRollCalls, ApiResource.VoteviewVotes, ApiResource.VoteviewMembers }
            .Select(resource => VoteviewJobKeys.File(VoteviewVoteFixtures.SourceId, resource, 119, today));

        Assert.Equal(3, keys.Distinct().Count());
    }
}
