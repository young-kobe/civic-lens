using System.Text;
using CivicLens.Application.Collection.Bills;

namespace CivicLens.Tests.Application.Collection.Bills;

public sealed class CongressBillActionsParserTests
{
    private const string FirstPage = "https://api.congress.gov/v3/bill/119/hr/1/actions?format=json&limit=250";
    private const string NextPage = "https://api.congress.gov/v3/bill/119/hr/1/actions?offset=250&limit=250&format=json";
    private static readonly CongressBillListItem Expected = new(119, "hr", 1, default);

    private const string Row = """{"actionCode":"E40000","actionDate":"2025-07-04","sourceSystem":{"code":9,"name":"Library of Congress"},"text":"Became Public Law No: 119-21.","type":"President"}""";

    [Fact]
    public void ValidPageYieldsWhenAndWhatAndTheValidatedNextLink()
    {
        var page = Parse($$$"""{"actions":[{{{Row}}},{"actionDate":"2025-07-03","actionTime":"13:45:10","text":"Passed House."}],"pagination":{"count":2,"next":"{{{NextPage}}}"}}""");

        Assert.Equal([new CongressBillAction(new DateOnly(2025, 7, 4), null, "Became Public Law No: 119-21.", "President",
                "E40000", 9, "Library of Congress"),
            new CongressBillAction(new DateOnly(2025, 7, 3), new TimeOnly(13, 45, 10), "Passed House.", null, null, null, null)],
            page.Actions);
        Assert.Equal(NextPage, page.Next);
    }

    [Fact]
    public void LastPageHasNoNextLinkSoTheChainEnds()
    {
        Assert.Null(Parse($$$"""{"actions":[{{{Row}}}],"pagination":{"count":1}}""").Next);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("""{"pagination":{}}""")]
    [InlineData("""{"actions":[]}""")]
    [InlineData("""{"actions":[null],"pagination":{}}""")]
    [InlineData("""{"actions":[{"text":"x"}],"pagination":{}}""")]
    [InlineData("""{"actions":[{"actionDate":"2025-07-04"}],"pagination":{}}""")]
    [InlineData("""{"actions":[{"actionDate":"2025-07-04","text":"  "}],"pagination":{}}""")]
    [InlineData("""{"actions":[{"actionDate":"yesterday","text":"x"}],"pagination":{}}""")]
    [InlineData("""{"actions":[{"actionDate":"2025-07-04","actionTime":"noon","text":"x"}],"pagination":{}}""")]
    public void MalformedPagesAreInvalidDataBecauseAGuessedActionWouldBeFalseEvidence(string json)
    {
        Assert.Throws<InvalidDataException>(() => Parse(json));
    }

    [Fact]
    public void MoreRowsThanTheRequestLimitAreInvalid()
    {
        var rows = string.Join(",", Enumerable.Repeat(Row, BillPageQuery.PageLimit + 1));

        Assert.Throws<InvalidDataException>(() => Parse("{\"actions\":[" + rows + "],\"pagination\":{}}"));
    }

    [Theory]
    [InlineData("https://evil.test/v3/bill/119/hr/1/actions?offset=250&limit=250&format=json")]
    [InlineData("https://other.congress.gov/v3/bill/119/hr/1/actions?offset=250&limit=250&format=json")]
    [InlineData("https://API.congress.gov/v3/bill/119/hr/1/actions?offset=250&limit=250&format=json")]
    [InlineData("https://api.congress.gov:443/v3/bill/119/hr/1/actions?offset=250&limit=250&format=json")]
    [InlineData("https://api.congress.gov/v3/bill/119/hr/1/actions?offset=250&limit=250&format=json&api_key=secret")]
    [InlineData("https://api.congress.gov/v3/bill/119/hr/2/actions?offset=250&limit=250&format=json")]
    [InlineData("https://api.congress.gov/v3/bill/119/hr/1/cosponsors?offset=250&limit=250&format=json")]
    [InlineData("https://api.congress.gov/v3/bill/119/hr/1/actions?offset=250&offset=500&limit=250&format=json")]
    [InlineData("https://api.congress.gov/v3/bill/119/hr/1/actions?offset=0&limit=250&format=json")]
    public void NextLinksOffTheSameBillKindAndAdvancingOffsetAreRejectedSoPagingCannotLeakOrLoop(string next)
    {
        Assert.Throws<InvalidDataException>(() => Parse($$$"""{"actions":[],"pagination":{"next":"{{{next}}}"}}"""));
    }

    [Fact]
    public void ANextLinkMustAdvanceBeyondTheCurrentPageOffset()
    {
        var json = $$$"""{"actions":[],"pagination":{"next":"{{{NextPage}}}"}}""";

        Assert.Throws<InvalidDataException>(() => Parse(json, NextPage));
    }

    [Theory]
    [InlineData("https://api.congress.gov/v3/bill/119/hr/1/cosponsors?format=json&limit=250")]
    [InlineData("https://api.congress.gov/v3/bill/119/hr/1/actions?format=json")]
    [InlineData("https://api.congress.gov/v3/bill/119/hr/2/actions?format=json&limit=250")]
    public void TheCurrentPageUrlMustBeAnActionPageOfTheExpectedBill(string current)
    {
        Assert.Throws<InvalidDataException>(() => Parse("""{"actions":[],"pagination":{}}""", current));
    }

    [Fact]
    public void ActionTextIsKeptExactlyAsReceivedBecauseItIsEvidence()
    {
        var page = Parse("""{"actions":[{"actionDate":"2025-07-04","text":"  Passed  House.\n"}],"pagination":{}}""");

        Assert.Equal("  Passed  House.\n", page.Actions.Single().Text);
    }

    private static CongressBillActionsPage Parse(string json, string? url = null) =>
        CongressBillActionsParser.Parse(Encoding.UTF8.GetBytes(json), BillPageScope.For(url ?? FirstPage), BillPageScope.SourceUrl, Expected);
}
