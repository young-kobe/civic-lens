using System.Text;
using CivicLens.Application.Collection.Bills;
using CivicLens.Tests.Fixtures;

namespace CivicLens.Tests.Application.Collection.Bills;

public sealed class CongressBillSubjectsParserTests
{
    private const string FirstPage = "https://api.congress.gov/v3/bill/117/s/3480/subjects?format=json&limit=250";
    private const string NextPage = "https://api.congress.gov/v3/bill/117/s/3480/subjects?offset=250&limit=250&format=json";
    private static readonly CongressBillListItem Expected = new(117, "s", 3480, default);

    [Fact]
    public void ValidPageYieldsTheLegislativeSubjectNamesAndIgnoresThePolicyArea()
    {
        var page = Parse($$$"""{"subjects":{"legislativeSubjects":[{"name":" Abortion ","updateDate":"2022-02-15T19:09:50Z"},{"name":"Health"}],"policyArea":{"name":"Health"}},"pagination":{"count":2,"next":"{{{NextPage}}}"}}""");

        Assert.Equal(["Abortion", "Health"], page.Subjects);
        Assert.Equal(NextPage, page.Next);
    }

    [Fact]
    public void ABillWithNoLegislativeSubjectsYieldsAnEmptyPageWithNoNextLink()
    {
        var page = Parse("""{"subjects":{"legislativeSubjects":[]},"pagination":{"count":0}}""");

        Assert.Empty(page.Subjects);
        Assert.Null(page.Next);
    }

    [Fact]
    public void ABillWithOnlyAPolicyAreaHasAnEmptyListAndACountOfOneBecauseTheCountIncludesThePolicyArea()
    {
        var page = Parse("""{"subjects":{"legislativeSubjects":[],"policyArea":{"name":"Health","updateDate":"2025-05-28T10:28:28Z"}},"pagination":{"count":1}}""");

        Assert.Empty(page.Subjects);
        Assert.Null(page.Next);
    }

    [Fact]
    public void APageWithFewerNamesThanTheLimitCanStillHaveANextLink()
    {
        var page = Parse($$$"""{"subjects":{"legislativeSubjects":[{"name":"Health"}]},"pagination":{"count":240,"next":"{{{NextPage}}}"}}""");

        Assert.Equal(NextPage, page.Next);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("""{"pagination":{}}""")]
    [InlineData("""{"subjects":{},"pagination":{}}""")]
    [InlineData("""{"subjects":{"legislativeSubjects":[]}}""")]
    [InlineData("""{"subjects":{"legislativeSubjects":[null]},"pagination":{}}""")]
    [InlineData("""{"subjects":{"legislativeSubjects":[{"name":" "}]},"pagination":{}}""")]
    [InlineData("""{"subjects":{"legislativeSubjects":[{}]},"pagination":{}}""")]
    public void MalformedPagesAreInvalidDataBecauseAGuessedSubjectWouldBeFalseEvidence(string json)
    {
        Assert.Throws<InvalidDataException>(() => Parse(json));
    }

    [Fact]
    public void MoreRowsThanTheRequestLimitAreInvalid()
    {
        var rows = string.Join(",", Enumerable.Repeat("""{"name":"Health"}""", 251));

        Assert.Throws<InvalidDataException>(() => Parse("{\"subjects\":{\"legislativeSubjects\":[" + rows + "]},\"pagination\":{}}"));
    }

    [Theory]
    [InlineData("https://evil.test/v3/bill/117/s/3480/subjects?offset=250&limit=250&format=json")]
    [InlineData("https://api.congress.gov/v3/bill/117/s/3481/subjects?offset=250&limit=250&format=json")]
    [InlineData("https://api.congress.gov/v3/bill/117/s/3480/summaries?offset=250&limit=250&format=json")]
    [InlineData("https://api.congress.gov/v3/bill/117/s/3480/subjects?offset=0&limit=250&format=json")]
    public void NextLinksOffTheSameBillKindAndAdvancingOffsetAreRejectedSoPagingCannotLeakOrLoop(string next)
    {
        Assert.Throws<InvalidDataException>(() => Parse($$$"""{"subjects":{"legislativeSubjects":[]},"pagination":{"next":"{{{next}}}"}}"""));
    }

    private static CongressBillSubjectsPage Parse(string json) =>
        CongressBillSubjectsParser.Parse(Encoding.UTF8.GetBytes(json), BillPageScope.For(FirstPage), BillPageScope.SourceUrl, Expected);
}
