using System.Net;
using CivicLens.PublicApi.Pages;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.Infrastructure.Collection;
using CivicLens.Tests.PublicApi.Votes;

namespace CivicLens.Tests.PublicApi.Pages;

/// <summary>The permanent redirects of the old static paths that have no server page. Each names the public address of its target.</summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class OldPageRedirectTests(PostgresCollection postgres)
{
    private const string Root = "https://civic-lens.test/";

    private async Task<(HttpStatusCode Status, string? Location)> GetAsync(string path)
    {
        await using var host = await ApiHost.StartAsync(postgres, options => options with { PublicBaseUrl = PublicBaseUrl.Parse(Root, "test") });
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 4, new DateTimeOffset(2026, 6, 9, 12, 0, 0, TimeSpan.Zero));
        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = host.Client.BaseAddress };
        using var response = await client.GetAsync("/" + path);
        return (response.StatusCode, response.Headers.Location?.OriginalString);
    }

    [Theory]
    [InlineData(117, "")]
    [InlineData(118, "?tab=votes")]
    public async Task AnOlderCongressPageOfAnOfficialMovesToTheOfficialPageAndKeepsTheQuery(int congress, string query)
    {
        var (status, location) = await GetAsync(PublicationProtocol.OfficialCongressPagePath("jane-doe", congress) + query);

        Assert.Equal(HttpStatusCode.MovedPermanently, status);
        Assert.Equal(Root + "officials/jane-doe.html" + query, location);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public async Task EachPolicyAreaPageMovesToTheSearchFilteredByItsAreaAndKeepsTheOtherQueryValues(int page)
    {
        var (status, location) = await GetAsync(PublicationProtocol.PolicyAreaPagePath("health", page) + "?policyArea=taxation&q=clinic");

        Assert.Equal(HttpStatusCode.MovedPermanently, status);
        Assert.Equal(Root + "search.html?policyArea=health&q=clinic", location);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public async Task EachAllRecordsPageMovesToTheVotesListAndKeepsTheQuery(int page)
    {
        var (status, location) = await GetAsync(PublicationProtocol.AllRecordsPagePath(page) + "?chamber=house");

        Assert.Equal(HttpStatusCode.MovedPermanently, status);
        Assert.Equal(Root + "votes.html?chamber=house", location);
    }

    [Fact]
    public async Task ALaterIssuePageMovesToTheVotesListFilteredByTheIssueAndKeepsTheQuery()
    {
        var (status, location) = await GetAsync(PublicationProtocol.IssuePagePath("health-care", 2) + "?chamber=senate");

        Assert.Equal(HttpStatusCode.MovedPermanently, status);
        Assert.Equal(Root + "votes.html?issue=health-care&chamber=senate", location);
    }

    [Theory]
    [InlineData("officials/Jane_Doe/118.html")]
    [InlineData("officials/jane-doe/0.html")]
    [InlineData("policy-areas/Health.html")]
    [InlineData("policy-areas/health.page-1.html")]
    [InlineData("policy-areas/health.page-02.html")]
    [InlineData("policy-areas/health.page-x.html")]
    [InlineData("all-1.html")]
    [InlineData("issues/health-care.page-1.html")]
    [InlineData("issues/Health_Care.page-2.html")]
    public async Task APathThatTheStaticSiteNeverWroteAnswersTheNotFoundPage(string path)
    {
        var (status, location) = await GetAsync(path);

        Assert.Equal(HttpStatusCode.NotFound, status);
        Assert.Null(location);
    }
}
