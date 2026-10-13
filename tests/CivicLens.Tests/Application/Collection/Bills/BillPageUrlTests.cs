using System.Text;
using CivicLens.Application.Collection;
using CivicLens.Application.Collection.Bills;
using CivicLens.Collection.Contracts;

namespace CivicLens.Tests.Application.Collection.Bills;

public sealed class BillPageUrlTests
{
    private const string Source = "https://api.congress.gov/v3/bill";
    private static readonly CongressBillListItem Bill = new(119, "hres", 1610, default);

    [Theory]
    [InlineData(ApiResource.BillCosponsors, "https://api.congress.gov/v3/bill/119/hres/1610/cosponsors?format=json&limit=250")]
    [InlineData(ApiResource.BillActions, "https://api.congress.gov/v3/bill/119/hres/1610/actions?format=json&limit=250")]
    public void FirstPageUrlHasTheExactFormTheReaderAccepts(ApiResource resource, string expected)
    {
        var url = BillPageUrl.FirstPage(Source, resource, Bill);

        Assert.Equal(expected, url);
        Assert.Equal(new BillPageUrl(Bill, 0), BillPageUrl.TryRead(Source, resource, url));
    }

    [Fact]
    public void TheApiNextFormWithADifferentParameterOrderIsRead()
    {
        var page = BillPageUrl.TryRead(Source, ApiResource.BillCosponsors,
            "https://api.congress.gov/v3/bill/119/hres/1610/cosponsors?offset=250&limit=250&format=json");

        Assert.Equal(new BillPageUrl(Bill, 250), page);
    }

    [Theory]
    [InlineData(ApiResource.BillList)]
    [InlineData(ApiResource.Bill)]
    public void OnlyTheFollowOnKindsHavePageForms(ApiResource resource)
    {
        Assert.Null(BillPageUrl.TryRead(Source, resource, "https://api.congress.gov/v3/bill/119/hr/1/actions?format=json&limit=250"));
        Assert.Throws<ArgumentOutOfRangeException>(() => BillPageUrl.FirstPage(Source, resource, Bill));
    }

    [Theory]
    [InlineData("https://api.congress.gov/v3/bill/119/hr/1/actions?format=json&limit=250&api_key=secret")]
    [InlineData("https://api.congress.gov/v3/bill/119/hr/1/actions?format=json&limit=250&offset=1&offset=2")]
    [InlineData("https://api.congress.gov/v3/bill/119/hr/1/actions?format=json&limit=250&offset=-1")]
    [InlineData("https://api.congress.gov/v3/bill/119/hr/1/actions?format=json&limit=250&offset=")]
    [InlineData("https://api.congress.gov/v3/bill/119/hr/1/actions?format=json&limit=100")]
    [InlineData("https://api.congress.gov/v3/bill/119/hr/1/actions?format=json&limit=250&limit=250")]
    [InlineData("https://api.congress.gov/v3/bill/119/hr/1/actions?format=json")]
    [InlineData("https://api.congress.gov/v3/bill/119/hr/1/actions?limit=250")]
    [InlineData("https://api.congress.gov/v3/bill/119/hr/1/actions")]
    [InlineData("https://api.congress.gov/v3/bill/119/hr/01/actions?format=json&limit=250")]
    [InlineData("https://api.congress.gov/v3/bill/0119/hr/1/actions?format=json&limit=250")]
    [InlineData("https://api.congress.gov/v3/bill/119/xx/1/actions?format=json&limit=250")]
    [InlineData("https://api.congress.gov/v3/bill/119/HR/1/actions?format=json&limit=250")]
    [InlineData("https://api.congress.gov/v3/bill/119/hr/1/cosponsors?format=json&limit=250")]
    [InlineData("https://api.congress.gov/v3/bill/119/hr/1/actions/x?format=json&limit=250")]
    [InlineData("https://api.congress.gov/v3/member/119/hr/1/actions?format=json&limit=250")]
    [InlineData("https://api.congress.gov/v3/bill/119/hr/1/actions?%66ormat=json&limit=250")]
    [InlineData("https://api.congress.gov/v3/bill/119/hr/1/actions?format=json&limit=%32%350")]
    [InlineData("https://api.congress.gov/v3/bill/119/hr/1/actions?format=json&limit=250&offset=0250")]
    [InlineData("https://api.congress.gov/v3/bill/119/hr/1/actions?format=json&limit=250&offset=00")]
    [InlineData("https://api.congress.gov/v3/bill/119/hr/1/actions?format=json&limit=250#fragment")]
    [InlineData("https://api.congress.gov/v3/bill/119/hr/1/actions/?format=json&limit=250")]
    [InlineData("https://api.congress.gov/v3/bill/119/hr/1/actions?format=json&limit=250&offset=+1")]
    [InlineData("https://api.congress.gov/v3/bill/119/hr/1/actions?format=json&limit=2+50")]
    [InlineData(null)]
    public void AnyOtherFormIsRejectedSoOnlyOneUrlExistsForEachPage(string? url)
    {
        Assert.Null(BillPageUrl.TryRead(Source, ApiResource.BillActions, url));
    }
}
