using CivicLens.Application.Collection.Votes;
using CivicLens.Application.Collection.HouseVotes;
using CivicLens.Tests.Fixtures;

namespace CivicLens.Tests.Application.Collection.HouseVotes;

public sealed class HouseVoteJobKeysTests
{
    private static readonly HouseVoteListItem Item = new(HouseVoteFixtures.Roll100, HouseVoteFixtures.Updated);

    [Fact]
    public void KeysNameTheSourceTheRollCallAndTheExactUtcUpdateTime()
    {
        Assert.Equal("house-vote/congress-house-votes/vote/119/1/100/2025-06-24T12:34:41Z",
            HouseVoteJobKeys.Detail(HouseVoteFixtures.SourceId, Item));
        Assert.Equal("house-vote/congress-house-votes/members/119/1/100/2025-06-24T12:34:41Z",
            HouseVoteJobKeys.Members(HouseVoteFixtures.SourceId, HouseVoteFixtures.Vote(100, HouseVoteFixtures.Updated)));
        Assert.Equal("house-vote/congress-house-votes/page/list-job", HouseVoteJobKeys.NextPage(HouseVoteFixtures.SourceId, "list-job"));
    }

    [Fact]
    public void ASecondsOldUpdateOfTheSameVoteHasANewKeySoAChangedVoteIsCollectedAgain()
    {
        var changed = Item with { UpdateDate = HouseVoteFixtures.Updated.AddSeconds(1) };

        Assert.NotEqual(HouseVoteJobKeys.Detail(HouseVoteFixtures.SourceId, Item), HouseVoteJobKeys.Detail(HouseVoteFixtures.SourceId, changed));
    }

    [Fact]
    public void TheSameInstantInAnotherOffsetHasTheSameKeySoAnUnchangedVoteCostsNothing()
    {
        var sameInstant = Item with { UpdateDate = HouseVoteFixtures.Updated.ToOffset(TimeSpan.FromHours(-4)) };

        Assert.Equal(HouseVoteJobKeys.Detail(HouseVoteFixtures.SourceId, Item), HouseVoteJobKeys.Detail(HouseVoteFixtures.SourceId, sameInstant));
    }

    [Fact]
    public void RollCallsOfTwoSessionsNeverShareAKey()
    {
        var secondSession = Item with { Roll = new HouseVoteRoll(new VoteSession(119, 2), 100) };

        Assert.NotEqual(HouseVoteJobKeys.Detail(HouseVoteFixtures.SourceId, Item), HouseVoteJobKeys.Detail(HouseVoteFixtures.SourceId, secondSession));
    }
}
