using System.Text;
using CivicLens.Application.Collection.Bills;

namespace CivicLens.Tests.Application.Collection.Bills;

public sealed class CongressBillSummariesParserTests
{
    private const string FirstPage = "https://api.congress.gov/v3/bill/117/s/3480/summaries?format=json&limit=1";
    private const string NextPage = "https://api.congress.gov/v3/bill/117/s/3480/summaries?offset=250&limit=1&format=json";
    private static readonly CongressBillListItem Expected = new(117, "s", 3480, default);

    private const string Row = """{"actionDate":"2022-02-18","actionDesc":"Passed Senate","text":"<p><b>Title</b> body</p>","updateDate":"2022-02-18T16:38:41Z","versionCode":"173"}""";

    [Fact]
    public void ValidPageYieldsEverySummaryWithItsExactHtmlAndTheValidatedNextLink()
    {
        var page = Parse($$$"""{"summaries":[{{{Row}}}],"pagination":{"count":1,"next":"{{{NextPage}}}"}}""");

        Assert.Equal([
            new CongressBillSummary("173", new DateOnly(2022, 2, 18), "Passed Senate",
                new DateTimeOffset(2022, 2, 18, 16, 38, 41, TimeSpan.Zero), "<p><b>Title</b> body</p>")],
            page.Summaries);
        Assert.Equal(NextPage, page.Next);
    }

    [Fact]
    public void AVersionCodeIsTrimmedAnExactOffsetTimeIsKeptAndEmptyTextIsValid()
    {
        var summary = Assert.Single(Parse("""{"summaries":[{"actionDate":"2022-03-01","actionDesc":"Introduced","text":"","updateDate":"2022-03-01T10:00:00-05:00","versionCode":" 00 "}],"pagination":{}}""").Summaries);

        Assert.Equal(("00", "", TimeSpan.FromHours(-5)), (summary.VersionCode, summary.Html, summary.UpdateDate.Offset));
    }

    [Fact]
    public void LastPageHasNoNextLinkSoTheChainEnds()
    {
        Assert.Null(Parse($$$"""{"summaries":[{{{Row}}}],"pagination":{"count":1}}""").Next);
    }

    [Fact]
    public void ABillWithNoSummariesYieldsAnEmptyPage()
    {
        Assert.Empty(Parse("""{"summaries":[],"pagination":{"count":0}}""").Summaries);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("""{"pagination":{}}""")]
    [InlineData("""{"summaries":[]}""")]
    [InlineData("""{"summaries":[null],"pagination":{}}""")]
    [InlineData("""{"summaries":[{"actionDate":"2022-02-18","actionDesc":"x","text":"t","updateDate":"2022-02-18T16:38:41Z"}],"pagination":{}}""")]
    [InlineData("""{"summaries":[{"actionDate":"2022-02-18","actionDesc":"x","text":"t","updateDate":"2022-02-18T16:38:41Z","versionCode":" "}],"pagination":{}}""")]
    [InlineData("""{"summaries":[{"actionDate":"2022-02-18","actionDesc":" ","text":"t","updateDate":"2022-02-18T16:38:41Z","versionCode":"1"}],"pagination":{}}""")]
    [InlineData("""{"summaries":[{"actionDesc":"x","text":"t","updateDate":"2022-02-18T16:38:41Z","versionCode":"1"}],"pagination":{}}""")]
    [InlineData("""{"summaries":[{"actionDate":"2022-02-18","actionDesc":"x","updateDate":"2022-02-18T16:38:41Z","versionCode":"1"}],"pagination":{}}""")]
    [InlineData("""{"summaries":[{"actionDate":"2022-02-18","actionDesc":"x","text":"t","versionCode":"1"}],"pagination":{}}""")]
    [InlineData("""{"summaries":[{"actionDate":"2022-02-18","actionDesc":"x","text":"t","updateDate":"2022-02-18","versionCode":"1"}],"pagination":{}}""")]
    [InlineData("""{"summaries":[{"actionDate":"yesterday","actionDesc":"x","text":"t","updateDate":"2022-02-18T16:38:41Z","versionCode":"1"}],"pagination":{}}""")]
    public void MalformedPagesAreInvalidDataBecauseAGuessedSummaryWouldBeFalseEvidence(string json)
    {
        Assert.Throws<InvalidDataException>(() => Parse(json));
    }

    [Fact]
    public void MoreRowsThanTheRequestLimitAreInvalid()
    {
        var rows = string.Join(",", Enumerable.Repeat(Row, BillPageQuery.SummariesPageLimit + 1));

        Assert.Throws<InvalidDataException>(() => Parse("{\"summaries\":[" + rows + "],\"pagination\":{}}"));
    }

    [Theory]
    [InlineData("https://evil.test/v3/bill/117/s/3480/summaries?offset=250&limit=1&format=json")]
    [InlineData("https://api.congress.gov/v3/bill/117/s/3480/summaries?offset=250&limit=1&format=json&api_key=secret")]
    [InlineData("https://api.congress.gov/v3/bill/117/s/3481/summaries?offset=250&limit=1&format=json")]
    [InlineData("https://api.congress.gov/v3/bill/117/s/3480/actions?offset=250&limit=1&format=json")]
    [InlineData("https://api.congress.gov/v3/bill/117/s/3480/summaries?offset=0&limit=1&format=json")]
    [InlineData("https://api.congress.gov/v3/bill/117/s/3480/summaries?offset=1&limit=250&format=json")]
    public void NextLinksOffTheSameBillKindAndAdvancingOffsetAreRejectedSoPagingCannotLeakOrLoop(string next)
    {
        Assert.Throws<InvalidDataException>(() => Parse($$$"""{"summaries":[],"pagination":{"next":"{{{next}}}"}}"""));
    }

    [Theory]
    [InlineData("https://api.congress.gov/v3/bill/117/s/3480/actions?format=json&limit=1")]
    [InlineData("https://api.congress.gov/v3/bill/117/s/3480/summaries?format=json")]
    [InlineData("https://api.congress.gov/v3/bill/117/s/3481/summaries?format=json&limit=1")]
    public void TheCurrentPageUrlMustBeASummariesPageOfTheExpectedBill(string current)
    {
        Assert.Throws<InvalidDataException>(() => Parse("""{"summaries":[],"pagination":{}}""", current));
    }

    private static CongressBillSummariesPage Parse(string json, string? url = null) =>
        CongressBillSummariesParser.Parse(Encoding.UTF8.GetBytes(json), BillPageScope.For(url ?? FirstPage), BillPageScope.SourceUrl, Expected);
}
