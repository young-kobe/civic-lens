using System.Net;
using CivicLens.Application.Actions;
using CivicLens.Tests.Fixtures;
using CivicLens.Tests.Infrastructure.Collection;
using static CivicLens.Tests.Host.ReviewWorkspaceHost;

namespace CivicLens.Tests.Host;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class ActionReviewWorkspaceTests(PostgresCollection postgres) : IAsyncLifetime
{
    private readonly ReviewWorkspaceHost host = new(postgres)
    {
        Issues = StatementGateFixtures.Configuration(),
        ReviewOfficials = "warren,markey"
    };

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

    [Fact]
    public async Task TheActionListShowsTheTierOfEachRowAndKeepsTheFiltersInItsTabLinksAndFilterForm()
    {
        var seeder = new RelationshipWorldSeeder(host.ConnectionString);
        var paired = RelationshipFixtures.Action(1) with { RecordId = new string('1', 64), CandidateId = new string('1', 64), TrackedIssueIds = [StatementGateFixtures.RentIssue] };
        var other = RelationshipFixtures.Action(2) with { RecordId = new string('2', 64), CandidateId = new string('2', 64) };
        var tracked = RelationshipFixtures.Action(3) with { RecordId = new string('3', 64), CandidateId = new string('3', 64) };
        await seeder.ActionAsync(paired, "AwaitingReview");
        await seeder.ActionAsync(other, "AwaitingReview");
        await seeder.ActionAsync(tracked, "AwaitingReview");
        await seeder.PriorityAsync("action_analysis", paired.CandidateId, ActionDraftTask.Task, 1);
        await seeder.PriorityAsync("action_analysis", tracked.CandidateId, ActionDraftTask.Task, 2);
        host.Client.DefaultRequestHeaders.Add("Cookie", host.CreateCookie("auth0|friend"));

        var all = await host.GetDocumentAsync("/Review/Actions");
        var filtered = await host.GetDocumentAsync($"/Review/Actions?issue={StatementGateFixtures.RentIssue}&official=warren&from=2025-01-01&to=2025-12-31");

        var rows = all.QuerySelectorAll("[aria-labelledby='drafts'] tbody tr");
        Assert.Equal(3, rows.Length);
        Assert.Contains("Pairs with a statement", rows[0].TextContent, StringComparison.Ordinal);
        Assert.Contains("Tracked issue", rows[1].TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain("Pairs with", rows[2].TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain("Tracked issue", rows[2].TextContent, StringComparison.Ordinal);
        Assert.Single(filtered.QuerySelectorAll("[aria-labelledby='drafts'] tbody tr"));
        var query = $"issue={StatementGateFixtures.RentIssue}&official=warren&from=2025-01-01&to=2025-12-31";
        Assert.Equal([$"/Review/Actions?{query}#drafts", $"/Review/Actions?drafts=needs-action&{query}#drafts", $"/Review/Actions?drafts=approved&{query}#drafts"],
            filtered.QuerySelectorAll("[aria-labelledby='drafts'] a.segmented-item").Select(tab => tab.GetAttribute("href")));
        Assert.Equal(StatementGateFixtures.RentIssue, filtered.QuerySelector("#filter-issue option[selected]")!.GetAttribute("value"));
        Assert.Equal("warren", filtered.QuerySelector("#filter-official option[selected]")!.GetAttribute("value"));
        Assert.Equal("2025-01-01", filtered.QuerySelector("#filter-from")!.GetAttribute("value"));
        Assert.Equal("2025-12-31", filtered.QuerySelector("#filter-to")!.GetAttribute("value"));
    }

    [Theory]
    [InlineData("issue=not-configured")]
    [InlineData("official=not-configured")]
    [InlineData("from=2025-13-40")]
    [InlineData("to=yesterday")]
    [InlineData("from=2025-06-01&to=2025-01-01")]
    public async Task AFilterThatIsNotValidShowsTheSameErrorAsABadCursorAndNoRows(string query)
    {
        host.Client.DefaultRequestHeaders.Add("Cookie", host.CreateCookie("auth0|friend"));

        var page = await host.GetDocumentAsync($"/Review/Actions?{query}");

        Assert.Contains("This page link is no longer valid", page.QuerySelector(".notice")!.TextContent, StringComparison.Ordinal);
        Assert.Null(page.QuerySelector("[aria-labelledby='drafts']"));
    }

    [Fact]
    public async Task TheReviewHomeCountsTheStatementsActionsAndVotesThatNeedActionAndLinksEachList()
    {
        var seeder = new RelationshipWorldSeeder(host.ConnectionString);
        await seeder.StatementAsync(RelationshipFixtures.Statement(), "AwaitingReview");
        await seeder.StatementAsync(RelationshipFixtures.Statement() with { Revision = RelationshipFixtures.Statement().Revision with { CandidateId = new string('2', 64) } }, "Approved");
        await seeder.ActionAsync(RelationshipFixtures.Action(1), "AwaitingReview");
        await seeder.ActionAsync(RelationshipFixtures.Action(2) with { RecordId = new string('3', 64), CandidateId = new string('3', 64) }, "ChangesRequested");
        await seeder.VoteAsync(RelationshipFixtures.Vote(), "AwaitingReview");
        host.Client.DefaultRequestHeaders.Add("Cookie", host.CreateCookie("auth0|friend"));

        var home = await host.GetDocumentAsync("/Review");

        foreach (var (label, count, route) in new[]
                 {
                     ("Statements need action", "1", "/Review/Statements"), ("Actions need action", "2", "/Review/Actions"),
                     ("Votes need action", "1", "/Review/Votes")
                 })
        {
            var stat = home.QuerySelector($"a.stat[href='{route}?drafts=needs-action']")!;
            Assert.Equal([count, label], stat.Children.Select(child => child.TextContent));
        }
    }
}
