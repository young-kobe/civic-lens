using System.Text;
using CivicLens.Application.Collection.Bills;
using CivicLens.Collection.Contracts;

namespace CivicLens.Tests.Application.Collection.Bills;

public sealed class CongressBillCosponsorsParserTests
{
    private const string FirstPage = "https://api.congress.gov/v3/bill/119/hres/1610/cosponsors?format=json&limit=250";
    private const string NextPage = "https://api.congress.gov/v3/bill/119/hres/1610/cosponsors?offset=250&limit=250&format=json";
    private static readonly CongressBillListItem Expected = new(119, "hres", 1610, default);

    private const string Row = """{"bioguideId":"L000595","district":5,"firstName":"A","fullName":"Rep. A","isOriginalCosponsor":true,"lastName":"B","party":"R","sponsorshipDate":"2026-10-05","state":"LA","url":"https://evil.test/x"}""";

    [Fact]
    public void ValidPageYieldsWhoAndWhenAndTheValidatedNextLink()
    {
        var page = Parse($$$"""{"cosponsors":[{{{Row}}},{"bioguideId":"W000001","isOriginalCosponsor":false,"sponsorshipDate":"2026-10-06","sponsorshipWithdrawnDate":"2026-10-08"}],"pagination":{"count":2,"next":"{{{NextPage}}}"}}""");

        Assert.Equal([new CongressBillCosponsor("L000595", new DateOnly(2026, 10, 5), true, null),
            new CongressBillCosponsor("W000001", new DateOnly(2026, 10, 6), false, new DateOnly(2026, 10, 8))], page.Cosponsors);
        Assert.Equal(NextPage, page.Next);
    }

    [Fact]
    public void LastPageHasNoNextLinkSoTheChainEnds()
    {
        var page = Parse($$$"""{"cosponsors":[{{{Row}}}],"pagination":{"count":1}}""");

        Assert.Null(page.Next);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("""{"pagination":{}}""")]
    [InlineData("""{"cosponsors":[]}""")]
    [InlineData("""{"cosponsors":[null],"pagination":{}}""")]
    [InlineData("""{"cosponsors":[{"bioguideId":" ","isOriginalCosponsor":true,"sponsorshipDate":"2026-10-05"}],"pagination":{}}""")]
    [InlineData("""{"cosponsors":[{"bioguideId":" L000595","isOriginalCosponsor":true,"sponsorshipDate":"2026-10-05"}],"pagination":{}}""")]
    [InlineData("""{"cosponsors":[{"bioguideId":"l000595","isOriginalCosponsor":true,"sponsorshipDate":"2026-10-05"}],"pagination":{}}""")]
    [InlineData("""{"cosponsors":[{"bioguideId":"L00059","isOriginalCosponsor":true,"sponsorshipDate":"2026-10-05"}],"pagination":{}}""")]
    [InlineData("""{"cosponsors":[{"isOriginalCosponsor":true,"sponsorshipDate":"2026-10-05"}],"pagination":{}}""")]
    [InlineData("""{"cosponsors":[{"bioguideId":"L000595","isOriginalCosponsor":true}],"pagination":{}}""")]
    [InlineData("""{"cosponsors":[{"bioguideId":"L000595","sponsorshipDate":"2026-10-05"}],"pagination":{}}""")]
    [InlineData("""{"cosponsors":[{"bioguideId":"L000595","isOriginalCosponsor":"maybe","sponsorshipDate":"2026-10-05"}],"pagination":{}}""")]
    [InlineData("""{"cosponsors":[{"bioguideId":"L000595","isOriginalCosponsor":true,"sponsorshipDate":"soon"}],"pagination":{}}""")]
    [InlineData("""{"cosponsors":[{"bioguideId":"L000595","isOriginalCosponsor":true,"sponsorshipDate":"2026-10-05","sponsorshipWithdrawnDate":"later"}],"pagination":{}}""")]
    public void MalformedPagesAreInvalidDataBecauseAGuessedCosponsorWouldBeFalseEvidence(string json)
    {
        Assert.Throws<InvalidDataException>(() => Parse(json));
    }

    [Fact]
    public void MoreRowsThanTheRequestLimitAreInvalid()
    {
        var rows = string.Join(",", Enumerable.Repeat(Row, BillPageQuery.PageLimit + 1));

        Assert.Throws<InvalidDataException>(() => Parse("{\"cosponsors\":[" + rows + "],\"pagination\":{}}"));
    }

    [Theory]
    [InlineData("https://evil.test/v3/bill/119/hres/1610/cosponsors?offset=250&limit=250&format=json")]
    [InlineData("https://other.congress.gov/v3/bill/119/hres/1610/cosponsors?offset=250&limit=250&format=json")]
    [InlineData("https://API.congress.gov/v3/bill/119/hres/1610/cosponsors?offset=250&limit=250&format=json")]
    [InlineData("https://api.congress.gov:443/v3/bill/119/hres/1610/cosponsors?offset=250&limit=250&format=json")]
    [InlineData("https://api.congress.gov/v3/bill/119/hres/1610/cosponsors?offset=250&limit=250&format=json&api_key=secret")]
    [InlineData("https://api.congress.gov/v3/bill/119/hres/1611/cosponsors?offset=250&limit=250&format=json")]
    [InlineData("https://api.congress.gov/v3/bill/119/hres/1610/actions?offset=250&limit=250&format=json")]
    [InlineData("https://api.congress.gov/v3/bill/119/hres/1610/cosponsors?offset=250&limit=100&format=json")]
    [InlineData("https://api.congress.gov/v3/bill/119/hres/1610/cosponsors?offset=0&limit=250&format=json")]
    [InlineData("https://api.congress.gov/v3/bill/119/hres/1610/cosponsors?limit=250&format=json")]
    public void NextLinksOffTheSameBillKindAndAdvancingOffsetAreRejectedSoPagingCannotLeakOrLoop(string next)
    {
        Assert.Throws<InvalidDataException>(() => Parse($$$"""{"cosponsors":[],"pagination":{"next":"{{{next}}}"}}"""));
    }

    [Fact]
    public void ANextLinkMustAdvanceBeyondTheCurrentPageOffset()
    {
        var current = "https://api.congress.gov/v3/bill/119/hres/1610/cosponsors?offset=250&limit=250&format=json";
        var json = $$$"""{"cosponsors":[],"pagination":{"next":"{{{current}}}"}}""";

        Assert.Throws<InvalidDataException>(() => Parse(json, current));
        Assert.Equal("https://api.congress.gov/v3/bill/119/hres/1610/cosponsors?offset=500&limit=250&format=json",
            Parse(json.Replace("offset=250", "offset=500"), current).Next);
    }

    [Theory]
    [InlineData("https://api.congress.gov/v3/bill/119/hres/1610/actions?format=json&limit=250")]
    [InlineData("https://api.congress.gov/v3/bill/119/hres/1610/cosponsors?format=json")]
    public void TheCurrentPageUrlMustBeACosponsorPage(string current)
    {
        Assert.Throws<InvalidDataException>(() => Parse("""{"cosponsors":[],"pagination":{}}""", current));
    }

    [Fact]
    public void ThePageMustBeForTheExpectedBill()
    {
        Assert.Throws<InvalidDataException>(() => Parse("""{"cosponsors":[],"pagination":{}}""", FirstPage.Replace("1610", "1611")));
    }

    private static CongressBillCosponsorsPage Parse(string json, string? url = null) =>
        CongressBillCosponsorsParser.Parse(Encoding.UTF8.GetBytes(json), BillPageScope.For(url ?? FirstPage), BillPageScope.SourceUrl, Expected);
}
