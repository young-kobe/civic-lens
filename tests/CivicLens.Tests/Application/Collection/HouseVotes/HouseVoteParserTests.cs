using System.Text;
using CivicLens.Application.Collection.HouseVotes;
using CivicLens.Tests.Fixtures;

namespace CivicLens.Tests.Application.Collection.HouseVotes;

public sealed class HouseVoteParserTests
{
    [Fact]
    public void DetailYieldsTheRollTheExactUpdateTimeAndTheBillWithItsTypeInTheStoredForm()
    {
        var vote = Parse(HouseVoteFixtures.Detail());

        Assert.Equal(new HouseVote(HouseVoteFixtures.Roll100, HouseVoteFixtures.Updated, "hconres", 14), vote.Vote);
    }

    [Fact]
    public void ARollCallWithoutLegislationIsAVoteWithNoBillBecauseEveryRollCallIsCollected()
    {
        var vote = Parse(HouseVoteFixtures.Detail(legislation: ""));

        Assert.Equal(new HouseVote(HouseVoteFixtures.Roll100, HouseVoteFixtures.Updated, null, null), vote.Vote);
    }

    [Fact]
    public void LegislationOfATypeTheBillsFeatureDoesNotStoreKeepsTheVoteButNoBillLink()
    {
        var vote = Parse(HouseVoteFixtures.Detail(legislation: "\"legislationNumber\":\"7\",\"legislationType\":\"HAMDT\","));

        Assert.Null(vote.Vote.BillType);
        Assert.Null(vote.Vote.BillNumber);
    }

    [Theory]
    [InlineData("\"legislationNumber\":\"7\",\"legislationType\":\"HAMDT\",")]
    [InlineData("\"legislationNumber\":\"0\",\"legislationType\":\"HAMDT\",")]
    [InlineData("\"legislationNumber\":\"S1\",\"legislationType\":\"HAMDT\",")]
    [InlineData("\"legislationType\":\"HAMDT\",")]
    public void AnUnknownLegislationTypeGivesNoBillWhateverItsNumberBecauseTheTypeDecidesWhetherANumberMeansABill(string legislation)
    {
        var vote = Parse(HouseVoteFixtures.Detail(legislation: legislation));

        Assert.Equal((null, null), (vote.Vote.BillType, vote.Vote.BillNumber));
    }

    [Theory]
    [InlineData("\"legislationType\":\"HR\",")]
    [InlineData("\"legislationNumber\":\"14\",")]
    [InlineData("\"legislationNumber\":\"0\",\"legislationType\":\"HR\",")]
    [InlineData("\"legislationNumber\":\"abc\",\"legislationType\":\"HR\",")]
    [InlineData("\"legislationNumber\":\"014\",\"legislationType\":\"HR\",")]
    public void LegislationWithOnlyOneHalfOrABadNumberIsInvalidBecauseTheBillLinkMustBeExact(string legislation)
    {
        Assert.Throws<InvalidDataException>(() => Parse(HouseVoteFixtures.Detail(legislation: legislation)));
    }

    [Theory]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("""{"unexpected":true}""")]
    [InlineData("""{"houseRollCallVote":{"congress":119,"rollCallNumber":100,"sessionNumber":1}}""")]
    public void MalformedDetailIsInvalidData(string json)
    {
        Assert.Throws<InvalidDataException>(() => Parse(json));
    }

    [Theory]
    [InlineData(101, 1)]
    [InlineData(100, 2)]
    public void ADetailOfAnotherRollCallThanTheJobRequestedIsInvalid(int roll, int session)
    {
        Assert.Throws<InvalidDataException>(() => Parse(HouseVoteFixtures.Detail(roll: roll, session: session)));
    }

    private static HouseVoteDetail Parse(string json) =>
        HouseVoteParser.Parse(Encoding.UTF8.GetBytes(json), HouseVoteFixtures.Roll100);
}
