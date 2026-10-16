using CivicLens.Application.Collection;
using CivicLens.Tests.Application.Collection.Bills;
using CivicLens.Tests.Fixtures;

namespace CivicLens.Tests.Application.Collection;

public sealed class ApiPageQueryTests
{
    private const string Source = BillPageScope.SourceUrl;
    private const string Current = Source + "/119/hr/1/actions?format=json&limit=250&offset=250";

    [Theory]
    [InlineData("0", 0)]
    [InlineData("119", 119)]
    [InlineData("007", null)]
    [InlineData("-1", null)]
    [InlineData("1.0", null)]
    [InlineData("", null)]
    public void NumbersMustBeCanonicalBecauseTwoSpellingsWouldAddressOneResourceTwice(string text, int? expected) =>
        Assert.Equal(expected, ApiPageQuery.TryReadNumber(text));

    [Fact]
    public void ThePathBetweenTheSourceAndTheQueryIsSplitIntoSegments()
    {
        var page = ApiPageQuery.TryReadPage(Source, Current, ApiPageQuery.PageLimit);

        Assert.Equal((new[] { "119", "hr", "1", "actions" }, 250), page);
    }

    [Theory]
    [InlineData("https://api.congress.gov/v3/other/119/hr/1/actions?format=json&limit=250")]
    [InlineData(Source + "/119/hr/1/actions")]
    [InlineData(Source + "/119/hr/1/actions?format=json&limit=250&offset=%32")]
    [InlineData(Source + "/119/hr/1/actions?format=json&limit=100")]
    [InlineData(Source + "/119/hr/1/actions?format=json&limit=250&offset=01")]
    public void AUrlOutsideTheExactPageFormIsNotAPage(string url) =>
        Assert.Null(ApiPageQuery.TryReadPage(Source, url, ApiPageQuery.PageLimit));

    [Fact]
    public void TheNextLinkMustStayInTheScopeInTheExactFormInTheSeriesAndAdvance()
    {
        var scope = BillPageScope.For(Current);
        var next = Source + "/119/hr/1/actions?offset=500&limit=250&format=json";
        (string Series, int Offset)? Read(string url) =>
            ApiPageQuery.TryReadPage(Source, url, ApiPageQuery.PageLimit) is var (segments, offset)
                ? (string.Join('/', segments), offset)
                : null;

        Assert.Equal(next, ApiPageQuery.ReadNext(next, scope, "119/hr/1/actions", 250, Read, "other series"));
        Assert.Null(ApiPageQuery.ReadNext(null, scope, "119/hr/1/actions", 250, Read, "other series"));
        Assert.Equal("Next page link is outside the source scope.", Assert.Throws<InvalidDataException>(() =>
            ApiPageQuery.ReadNext("https://example.com/v3/bill/x?offset=500", scope, "", 250, Read, "other series")).Message);
        Assert.Equal("Next page link does not have the exact page form.", Assert.Throws<InvalidDataException>(() =>
            ApiPageQuery.ReadNext(next + "&x=1", scope, "", 250, Read, "other series")).Message);
        Assert.Equal("other series", Assert.Throws<InvalidDataException>(() =>
            ApiPageQuery.ReadNext(next, scope, "119/hr/2/actions", 250, Read, "other series")).Message);
        Assert.Equal("Next page link does not advance the offset.", Assert.Throws<InvalidDataException>(() =>
            ApiPageQuery.ReadNext(next, scope, "119/hr/1/actions", 500, Read, "other series")).Message);
    }
}
