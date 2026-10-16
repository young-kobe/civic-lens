using System.Net;
using CivicLens.Tests.Fixtures;
using CivicLens.Tests.Infrastructure.Collection;
using static CivicLens.Tests.Host.ReviewWorkspaceHost;

namespace CivicLens.Tests.Host;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class ActionReviewWorkspaceTests(PostgresCollection postgres) : IAsyncLifetime
{
    private readonly ReviewWorkspaceHost host = new(postgres) { Issues = StatementGateFixtures.Configuration() };

    public Task InitializeAsync() => host.StartAsync();

    public Task DisposeAsync() => host.StopAsync();

    [Fact]
    public async Task TheRailLinksTheActionListWhichCountsEachFilterAndRefusesAnEmptyBulkApprovalAsync()
    {
        host.Client.DefaultRequestHeaders.Add("Cookie", host.CreateCookie("auth0|friend"));

        var page = await host.GetDocumentAsync("/Review/Actions");

        Assert.Equal("page", page.QuerySelector(".nav a[href='/Review/Actions']")!.GetAttribute("aria-current"));
        Assert.Null(page.QuerySelector(".nav a[href='/Review']")!.GetAttribute("aria-current"));
        Assert.Equal(["0", "0", "0"], page.QuerySelectorAll("[aria-labelledby='drafts'] .segmented-count").Select(count => count.TextContent));
        Assert.Equal(["/Review/Actions#drafts", "/Review/Actions?drafts=needs-action#drafts", "/Review/Actions?drafts=approved#drafts"],
            page.QuerySelectorAll("[aria-labelledby='drafts'] a.segmented-item").Select(tab => tab.GetAttribute("href")));
        Assert.Equal("No action records yet", page.QuerySelector("[aria-labelledby='drafts'] .empty h3")!.TextContent);
        using var empty = await host.Client.PostAsync("/Review/Actions", Form(page.QuerySelector("#approve-sponsorships")!));
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
    }

    [Fact]
    public async Task AnUnknownOrMalformedActionRecordIsNotFoundAsync()
    {
        host.Client.DefaultRequestHeaders.Add("Cookie", host.CreateCookie("auth0|owner"));

        foreach (var id in new[] { new string('a', 64), "not-a-record" })
        {
            using var missing = await host.Client.GetAsync($"/Review/Actions/{id}");
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        }
    }
}
