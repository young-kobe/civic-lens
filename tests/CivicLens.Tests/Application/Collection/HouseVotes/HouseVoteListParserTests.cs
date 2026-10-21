using System.Text;
using CivicLens.Application.Collection;
using CivicLens.Application.Collection.Bills;
using CivicLens.Application.Collection.HouseVotes;
using CivicLens.Application.Collection.Votes;
using CivicLens.Tests.Fixtures;

namespace CivicLens.Tests.Application.Collection.HouseVotes;

public sealed class HouseVoteListParserTests
{
    private const string Source = HouseVoteFixtures.SourceUrl;
    private const string FirstPage = Source + "/119/1?format=json&limit=250";

    [Fact]
    public void ValidPageYieldsEachRollCallWithItsExactUpdateTimeAndTheSessionOfThePage()
    {
        var next = Source + "/119/1?offset=250&limit=250&format=json";
        var page = Parse("""
            {"houseRollCallVotes":[
              {"congress":119,"identifier":11912025240,"legislationNumber":"3424","legislationType":"HR","result":"Passed",
               "rollCallNumber":240,"sessionNumber":1,"updateDate":"2025-09-09T18:53:19-04:00","url":"https://evil.test/x"},
              {"congress":119,"identifier":11912025306,"result":"Agreed to","rollCallNumber":306,"sessionNumber":1,
               "updateDate":"2025-12-19T09:53:45-05:00"}],
             "pagination":{"count":362,"next":"
            """ + next + "\"}}");

        Assert.Equal(new VoteSession(119, 1), page.Session);
        Assert.Equal([new HouseVoteListItem(new HouseVoteRoll(new VoteSession(119, 1), 240),
                new DateTimeOffset(2025, 9, 9, 22, 53, 19, TimeSpan.Zero)),
            new HouseVoteListItem(new HouseVoteRoll(new VoteSession(119, 1), 306),
                new DateTimeOffset(2025, 12, 19, 14, 53, 45, TimeSpan.Zero))], page.Votes);
        Assert.Equal(next, page.Next);
    }

    [Fact]
    public void LastPageHasNoNextLink()
    {
        Assert.Null(Parse("""{"houseRollCallVotes":[],"pagination":{"count":0}}""").Next);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("""{"pagination":{}}""")]
    [InlineData("""{"houseRollCallVotes":[]}""")]
    [InlineData("""{"houseRollCallVotes":[null],"pagination":{}}""")]
    [InlineData("""{"houseRollCallVotes":[{"congress":119,"sessionNumber":1,"rollCallNumber":5}],"pagination":{}}""")]
    [InlineData("""{"houseRollCallVotes":[{"congress":119,"sessionNumber":1,"updateDate":"2025-09-09T18:53:19-04:00"}],"pagination":{}}""")]
    [InlineData("""{"houseRollCallVotes":[{"congress":119,"sessionNumber":1,"rollCallNumber":0,"updateDate":"2025-09-09T18:53:19-04:00"}],"pagination":{}}""")]
    [InlineData("""{"houseRollCallVotes":[{"congress":119,"sessionNumber":3,"rollCallNumber":5,"updateDate":"2025-09-09T18:53:19-04:00"}],"pagination":{}}""")]
    [InlineData("""{"houseRollCallVotes":[{"congress":119,"sessionNumber":1,"rollCallNumber":5,"updateDate":"soon"}],"pagination":{}}""")]
    public void MalformedPagesAreInvalidDataBecauseAPartialSweepMustNotLookComplete(string json)
    {
        Assert.Throws<InvalidDataException>(() => Parse(json));
    }

    [Fact]
    public void AnItemOfAnotherSessionIsInvalidBecauseTheDetailJobsAreAdmittedFromThePageSession()
    {
        Assert.Throws<InvalidDataException>(() => Parse("""
            {"houseRollCallVotes":[{"congress":119,"sessionNumber":2,"rollCallNumber":5,"updateDate":"2025-09-09T18:53:19-04:00"}],
             "pagination":{}}
            """));
    }

    [Fact]
    public void MoreItemsThanTheRequestLimitAreInvalidBecauseAdmissionBudgetsAssumeTheLimit()
    {
        var items = Enumerable.Range(1, ApiPageQuery.PageLimit + 1)
            .Select(roll => $$"""{"congress":119,"sessionNumber":1,"rollCallNumber":{{roll}},"updateDate":"2025-09-09T18:53:19-04:00"}""");
        var json = "{\"houseRollCallVotes\":[" + string.Join(",", items) + "],\"pagination\":{}}";

        Assert.Throws<InvalidDataException>(() => Parse(json));
    }

    [Fact]
    public void ALinkToTheNextPageCannotLeaveTheSessionOrTheSource()
    {
        Assert.Throws<InvalidDataException>(() => Parse(PageWithNext(Source + "/119/2?offset=250&limit=250&format=json")));
        Assert.Throws<InvalidDataException>(() => Parse(PageWithNext("https://evil.test/v3/house-vote/119/1?offset=250&limit=250&format=json")));
        Assert.Throws<InvalidDataException>(() => Parse(PageWithNext(Source + "/119/1?offset=250&limit=250&format=json&api_key=secret")));
    }

    [Fact]
    public void AJobWhoseUrlIsNotAListPageIsInvalidData()
    {
        Assert.Throws<InvalidDataException>(() => HouseVoteListParser.Parse("""{"houseRollCallVotes":[],"pagination":{}}"""u8,
            HouseVoteFixtures.Scope(Source + "/119/1/100?format=json"), Source));
    }

    private static string PageWithNext(string next) => "{\"houseRollCallVotes\":[],\"pagination\":{\"next\":\"" + next + "\"}}";

    private static HouseVoteListPage Parse(string json) =>
        HouseVoteListParser.Parse(Encoding.UTF8.GetBytes(json), HouseVoteFixtures.Scope(FirstPage), Source);
}
