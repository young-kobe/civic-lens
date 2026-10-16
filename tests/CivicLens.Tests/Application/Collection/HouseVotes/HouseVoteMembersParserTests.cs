using System.Text;
using CivicLens.Application.Collection.HouseVotes;
using CivicLens.Tests.Fixtures;

namespace CivicLens.Tests.Application.Collection.HouseVotes;

public sealed class HouseVoteMembersParserTests
{
    [Fact]
    public void MembersYieldTheVersionOfTheVoteTheyBelongToSoItCanBeMatchedToTheDetail()
    {
        var vote = Parse(HouseVoteFixtures.Members());

        Assert.Equal(new HouseVote(HouseVoteFixtures.Roll100, HouseVoteFixtures.Updated, "hconres", 14), vote.Vote);
    }

    [Fact]
    public void MembersOfARollCallWithoutLegislationAreValid()
    {
        Assert.Null(Parse(HouseVoteFixtures.Members(legislation: "")).Vote.BillType);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("""{"houseRollCallVoteMemberVotes":null}""")]
    [InlineData("""{"houseRollCallVote":{}}""")]
    public void MalformedMembersAreInvalidData(string json)
    {
        Assert.Throws<InvalidDataException>(() => Parse(json));
    }

    [Theory]
    [InlineData("""[]""")]
    [InlineData("""null""")]
    [InlineData("""[null]""")]
    [InlineData("""[{"voteCast":"Yea"}]""")]
    [InlineData("""[{"bioguideID":"A000055"}]""")]
    [InlineData("""[{"bioguideID":" ","voteCast":"Yea"}]""")]
    public void AMembersListWithoutMembersOrWithAnIncompleteMemberIsInvalidBecauseTheVotesWouldBeIncomplete(string results)
    {
        var json = HouseVoteFixtures.Members();
        var start = json.IndexOf("\"results\":[", StringComparison.Ordinal);
        var end = json.IndexOf("]}", start, StringComparison.Ordinal) + 1;
        var broken = json[..start] + "\"results\":" + results + json[end..];

        Assert.Throws<InvalidDataException>(() => Parse(broken));
    }

    [Fact]
    public void MembersOfAnotherRollCallThanTheJobRequestedAreInvalid()
    {
        Assert.Throws<InvalidDataException>(() => Parse(HouseVoteFixtures.Members(roll: 101)));
    }

    private static HouseVoteMemberVotes Parse(string json) =>
        HouseVoteMembersParser.Parse(Encoding.UTF8.GetBytes(json), HouseVoteFixtures.Roll100);
}
