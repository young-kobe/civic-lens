using CivicLens.Application.Collection.Votes;
using CivicLens.Application.Collection.HouseVotes;
using CivicLens.Tests.Fixtures;

namespace CivicLens.Tests.Application.Collection.HouseVotes;

public sealed class HouseVoteListUrlTests
{
    private const string Source = HouseVoteFixtures.SourceUrl;
    private const string First = Source + "/119/1?format=json&limit=250";

    [Fact]
    public void TheFirstPageOfASessionHasTheExactListForm()
    {
        Assert.Equal(First, HouseVoteListUrl.FirstPage(Source, new VoteSession(119, 1)));
        Assert.Equal(new HouseVoteListUrl(new VoteSession(119, 1), 0), HouseVoteListUrl.TryRead(Source, First));
    }

    [Fact]
    public void TheApiNextFormIsReadWithItsOffsetEvenThoughItOrdersTheParametersDifferently()
    {
        var url = HouseVoteListUrl.TryRead(Source, Source + "/119/2?offset=250&limit=250&format=json");

        Assert.Equal(new HouseVoteListUrl(new VoteSession(119, 2), 250), url);
    }

    [Theory]
    [InlineData(Source + "/119/1?format=json&limit=250&api_key=secret")]
    [InlineData(Source + "/119/1?format=json&limit=250&fromDateTime=2025-01-01T00:00:00Z")]
    [InlineData(Source + "/119/1?format=json&limit=20")]
    [InlineData(Source + "/119/1?format=json")]
    [InlineData(Source + "/119/1?limit=250")]
    [InlineData(Source + "/119/1?format=json&limit=250&offset=1&offset=2")]
    [InlineData(Source + "/119/1?format=json&limit=250&offset=-1")]
    [InlineData(Source + "/119/1?format=json&limit=250&offset=0250")]
    [InlineData(Source + "/119/1?%66ormat=json&limit=250")]
    [InlineData(Source + "/119/1?format=json&limit=2+50")]
    [InlineData(Source + "/119/3?format=json&limit=250")]
    [InlineData(Source + "/119/01?format=json&limit=250")]
    [InlineData(Source + "/0/1?format=json&limit=250")]
    [InlineData(Source + "/119?format=json&limit=250")]
    [InlineData(Source + "/119/1/100?format=json&limit=250")]
    [InlineData(Source + "/119/1/?format=json&limit=250")]
    [InlineData("https://api.congress.gov/v3/bill/119/1?format=json&limit=250")]
    [InlineData(Source + "/119/1")]
    [InlineData(null)]
    public void AnyOtherFormIsRejectedSoOnlyOneUrlExistsForEachListPage(string? url)
    {
        Assert.Null(HouseVoteListUrl.TryRead(Source, url));
    }

    [Fact]
    public void ANextLinkThatStaysInTheSessionAndAdvancesTheOffsetIsAccepted()
    {
        var next = Source + "/119/1?offset=250&limit=250&format=json";

        Assert.Equal(next, Current().ReadNext(next, HouseVoteFixtures.Scope(First), Source));
        Assert.Null(Current().ReadNext(null, HouseVoteFixtures.Scope(First), Source));
        Assert.Null(Current().ReadNext("", HouseVoteFixtures.Scope(First), Source));
    }

    [Theory]
    [InlineData("https://evil.test/v3/house-vote/119/1?offset=250&limit=250&format=json")]
    [InlineData("http://api.congress.gov/v3/house-vote/119/1?offset=250&limit=250&format=json")]
    [InlineData("https://api.congress.gov/v3/member/119/1?offset=250&limit=250&format=json")]
    [InlineData("/v3/house-vote/119/1?offset=250&limit=250&format=json")]
    public void NextLinksOutsideTheSourceScopeAreRejectedBeforeTheyBecomeJobs(string next)
    {
        Assert.Throws<InvalidDataException>(() => Current().ReadNext(next, HouseVoteFixtures.Scope(First), Source));
    }

    [Theory]
    [InlineData("?offset=250&limit=250&format=json&api_key=secret")]
    [InlineData("?offset=250&limit=250&format=json&API_KEY=secret")]
    public void NextLinksCarryingACredentialAreRejectedSoNoKeyIsStoredInAJobDefinition(string query)
    {
        Assert.Throws<InvalidDataException>(() =>
            Current().ReadNext(Source + "/119/1" + query, HouseVoteFixtures.Scope(First), Source));
    }

    [Theory]
    [InlineData("/119/2?offset=250&limit=250&format=json")]
    [InlineData("/119/1?offset=0&limit=250&format=json")]
    [InlineData("/119/1?limit=250&format=json")]
    public void ANextLinkForAnotherSessionOrWithoutAForwardOffsetIsRejectedSoPagingCannotLoop(string path)
    {
        Assert.Throws<InvalidDataException>(() => Current().ReadNext(Source + path, HouseVoteFixtures.Scope(First), Source));
    }

    [Fact]
    public void AJobUrlThatIsNotAListPageIsInvalidDataBecausePreparationSelectsByResourceKind()
    {
        Assert.Throws<InvalidDataException>(() => HouseVoteListUrl.Read(Source, Source + "/119/1/100?format=json"));
    }

    private static HouseVoteListUrl Current() => HouseVoteListUrl.Read(Source, First);
}
