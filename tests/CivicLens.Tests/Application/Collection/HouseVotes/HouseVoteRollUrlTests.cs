using CivicLens.Application.Collection.HouseVotes;
using CivicLens.Tests.Fixtures;

namespace CivicLens.Tests.Application.Collection.HouseVotes;

public sealed class HouseVoteRollUrlTests
{
    private const string Source = HouseVoteFixtures.SourceUrl;

    [Fact]
    public void DetailAndMembersUrlsHaveTheirOwnExactFormsAndReadBackToTheSameRoll()
    {
        var roll = HouseVoteFixtures.Roll100;

        Assert.Equal(Source + "/119/1/100?format=json", HouseVoteRollUrl.Detail(Source, roll));
        Assert.Equal(Source + "/119/1/100/members?format=json", HouseVoteRollUrl.Members(Source, roll));
        Assert.Equal(roll, HouseVoteRollUrl.TryReadDetail(Source, HouseVoteRollUrl.Detail(Source, roll)));
        Assert.Equal(roll, HouseVoteRollUrl.TryReadMembers(Source, HouseVoteRollUrl.Members(Source, roll)));
    }

    [Fact]
    public void ADetailUrlIsNeverReadAsAMembersUrlAndTheOtherWayRound()
    {
        Assert.Null(HouseVoteRollUrl.TryReadMembers(Source, Source + "/119/1/100?format=json"));
        Assert.Null(HouseVoteRollUrl.TryReadDetail(Source, Source + "/119/1/100/members?format=json"));
    }

    [Theory]
    [InlineData(Source + "/119/1/100?format=json&api_key=secret")]
    [InlineData(Source + "/119/1/100?format=json&limit=250")]
    [InlineData(Source + "/119/1/100")]
    [InlineData(Source + "/119/1/0?format=json")]
    [InlineData(Source + "/119/1/0100?format=json")]
    [InlineData(Source + "/119/3/100?format=json")]
    [InlineData(Source + "/119/1?format=json")]
    [InlineData(Source + "/119/1/100/extra?format=json")]
    [InlineData("https://api.congress.gov/v3/bill/119/1/100?format=json")]
    public void AnyOtherDetailFormIsRejected(string url)
    {
        Assert.Null(HouseVoteRollUrl.TryReadDetail(Source, url));
    }

    [Theory]
    [InlineData(Source + "/119/1/100/members?format=json&api_key=secret")]
    [InlineData(Source + "/119/1/100/members?format=json&limit=3")]
    [InlineData(Source + "/119/1/100/members")]
    [InlineData(Source + "/119/1/0/members?format=json")]
    public void AnyOtherMembersFormIsRejected(string url)
    {
        Assert.Null(HouseVoteRollUrl.TryReadMembers(Source, url));
    }

    [Fact]
    public void AJobUrlOfTheWrongKindIsInvalidData()
    {
        Assert.Throws<InvalidDataException>(() => HouseVoteRollUrl.ReadDetail(Source, Source + "/119/1/100/members?format=json"));
        Assert.Throws<InvalidDataException>(() => HouseVoteRollUrl.ReadMembers(Source, Source + "/119/1/100?format=json"));
    }
}
