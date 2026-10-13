using System.Text;
using CivicLens.Application.Collection.Bills;
using CivicLens.Collection.Contracts;

namespace CivicLens.Tests.Application.Collection.Bills;

public sealed class CongressBillListParserTests
{
    private static readonly BillStreamWindow Window = new(new DateOnly(2025, 1, 3), new DateOnly(2025, 1, 4));
    private const string WindowQuery = "fromDateTime=2025-01-02T00:00:00Z&sort=updateDate asc&toDateTime=2025-01-05T00:00:00Z&limit=250";

    [Fact]
    public void ValidPageYieldsItemsWithDetailUrlsBuiltFromCongressTypeAndNumber()
    {
        var next = $"https://api.congress.gov/v3/bill?offset=250&{WindowQuery}&format=json";
        var page = Parse("""
            {"bills":[{"congress":119,"type":"HJRES","number":"12","updateDate":"2025-01-03","url":"https://evil.test/x"},
                      {"congress":118,"type":"S","number":7,"updateDate":"2025-01-04"}],
             "pagination":{"count":2,"next":"
            """ + next + "\"}}");

        Assert.Equal([new CongressBillListItem(119, "hjres", 12, new DateOnly(2025, 1, 3)),
            new CongressBillListItem(118, "s", 7, new DateOnly(2025, 1, 4))], page.Bills);
        Assert.Equal("https://api.congress.gov/v3/bill/119/hjres/12?format=json", page.Bills[0].DetailUrl("https://api.congress.gov/v3/bill"));
        Assert.Equal(next, page.Next);
    }

    [Fact]
    public void LastPageHasNoNextLink()
    {
        Assert.Null(Parse("""{"bills":[],"pagination":{"count":0}}""").Next);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("""{"pagination":{}}""")]
    [InlineData("""{"bills":[]}""")]
    [InlineData("""{"bills":[null],"pagination":{}}""")]
    [InlineData("""{"bills":[{"congress":119,"type":"HR","number":"1"}],"pagination":{}}""")]
    [InlineData("""{"bills":[{"congress":0,"type":"HR","number":"1","updateDate":"2025-01-03"}],"pagination":{}}""")]
    [InlineData("""{"bills":[{"congress":119,"type":"HR","number":"one","updateDate":"2025-01-03"}],"pagination":{}}""")]
    [InlineData("""{"bills":[{"congress":119,"type":"HR","number":"1","updateDate":"soon"}],"pagination":{}}""")]
    public void MalformedPagesAreInvalidDataBecauseAPartialPageMustNotMoveTheCursor(string json)
    {
        Assert.Throws<InvalidDataException>(() => Parse(json));
    }

    [Fact]
    public void UnknownBillTypeIsInvalidBecauseItWouldBuildAnUnknownDetailUrl()
    {
        Assert.Throws<InvalidDataException>(() => Parse(
            """{"bills":[{"congress":119,"type":"../x","number":"1","updateDate":"2025-01-03"}],"pagination":{}}"""));
    }

    [Fact]
    public void MoreItemsThanTheRequestLimitAreInvalidBecauseAdmissionBudgetsAssumeTheLimit()
    {
        var item = """{"congress":119,"type":"HR","number":"1","updateDate":"2025-01-03"}""";
        var json = "{\"bills\":[" + string.Join(",", Enumerable.Repeat(item, BillPageQuery.PageLimit + 1)) + "],\"pagination\":{}}";

        Assert.Throws<InvalidDataException>(() => Parse(json));
    }

    [Theory]
    [InlineData("https://evil.test/v3/bill?offset=250")]
    [InlineData("https://api.congress.gov/v3/member?offset=250")]
    [InlineData("http://api.congress.gov/v3/bill?offset=250")]
    [InlineData("/v3/bill?offset=250")]
    public void NextLinksOutsideTheSourceScopeAreRejectedBeforeTheyBecomeJobs(string next)
    {
        Assert.Throws<InvalidDataException>(() => Parse(PageWithNext($"{next}&{WindowQuery}")));
    }

    [Theory]
    [InlineData("api_key=secret")]
    [InlineData("API_KEY=secret")]
    public void NextLinksCarryingACredentialAreRejectedSoNoKeyIsStoredInAJobDefinition(string parameter)
    {
        Assert.Throws<InvalidDataException>(() => Parse(
            PageWithNext($"https://api.congress.gov/v3/bill?{parameter}&{WindowQuery}")));
    }

    [Fact]
    public void NextLinkForAnotherWindowIsRejectedBecauseTheCursorBelongsToOneWindow()
    {
        Assert.Throws<InvalidDataException>(() => Parse(PageWithNext(
            "https://api.congress.gov/v3/bill?fromDateTime=2025-02-02T00:00:00Z&sort=updateDate asc&toDateTime=2025-02-05T00:00:00Z")));
    }

    private static string PageWithNext(string next) => "{\"bills\":[],\"pagination\":{\"next\":\"" + next + "\"}}";

    private static CongressBillListPage Parse(string json)
    {
        var scope = new CollectionRequest
        {
            JobId = "job",
            SourceId = "congress-bills",
            Url = Window.RequestUrl("https://api.congress.gov/v3/bill"),
            AllowedOrigin = "https://api.congress.gov",
            AllowedPathPrefix = "/v3/bill",
            ArtifactDirectory = Path.GetFullPath("captures"),
            Mode = CollectionMode.Api,
            Credential = CollectionCredentials.CongressGov
        };
        return CongressBillListParser.Parse(Encoding.UTF8.GetBytes(json), scope, Window);
    }
}
