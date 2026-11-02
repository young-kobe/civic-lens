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
        OfficialId = "warren",
        OfficialName = "Elizabeth Warren"
    };

    public Task InitializeAsync() => host.StartAsync();

    public Task DisposeAsync() => host.StopAsync();

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

    [Theory]
    [InlineData("auth0|friend")]
    [InlineData("auth0|owner")]
    public async Task TheReviewHomeListsEachActionAndVoteThatCodeLeftForAPersonAndLinksItsEditorAsync(string subject)
    {
        var seeder = new RelationshipWorldSeeder(host.ConnectionString);
        var refused = RelationshipFixtures.Action(1);
        var changesRequested = RelationshipFixtures.Action(2) with { RecordId = new string('3', 64), CandidateId = new string('3', 64) };
        var approved = RelationshipFixtures.Action(4) with { RecordId = new string('4', 64), CandidateId = new string('4', 64) };
        var refusedVote = RelationshipFixtures.Vote();
        var approvedVote = RelationshipFixtures.Vote() with { RecordId = new string('5', 64), CandidateId = new string('5', 64), RollCallNumber = 77 };
        await seeder.ActionAsync(refused, "AwaitingReview");
        await seeder.ActionAsync(changesRequested, "ChangesRequested");
        await seeder.ActionAsync(approved, "Approved");
        await seeder.VoteAsync(refusedVote, "AwaitingReview");
        await seeder.VoteAsync(approvedVote, "Approved");
        host.Client.DefaultRequestHeaders.Add("Cookie", host.CreateCookie(subject));

        var home = await host.GetDocumentAsync("/Review");

        var links = home.QuerySelectorAll("[aria-labelledby='needs-a-person'] tbody a.cell-title").Select(link => link.GetAttribute("href"));
        Assert.Equivalent(new[] { $"/Review/Actions/{refused.RecordId}", $"/Review/Actions/{changesRequested.RecordId}", $"/Review/Votes/{refusedVote.RecordId}" }, links);
        Assert.Null(home.QuerySelector($"a[href='/Review/Actions/{approved.RecordId}']"));
        Assert.Null(home.QuerySelector($"a[href='/Review/Votes/{approvedVote.RecordId}']"));
        Assert.Null(home.QuerySelector(".nav a[href='/Review/Actions']"));
        Assert.Null(home.QuerySelector(".nav a[href='/Review/Votes']"));
    }

    [Fact]
    public async Task TheReviewHomeQueueLeavesOutActionsAndVotesThatCodeApprovedAsync()
    {
        var seeder = new RelationshipWorldSeeder(host.ConnectionString);
        await seeder.ActionAsync(RelationshipFixtures.Action(1), "Approved");
        await seeder.VoteAsync(RelationshipFixtures.Vote(), "Approved");
        host.Client.DefaultRequestHeaders.Add("Cookie", host.CreateCookie("auth0|friend"));

        var home = await host.GetDocumentAsync("/Review");

        Assert.Equal("No record needs a person", home.QuerySelector("[aria-labelledby='needs-a-person'] .empty h3")!.TextContent);
    }

    [Fact]
    public async Task OneUnreadableRevisionShowsARowErrorThatLinksToTheRecordAndTheOtherQueueRowsStayVisibleAsync()
    {
        var seeder = new RelationshipWorldSeeder(host.ConnectionString);
        var good = RelationshipFixtures.Action(1);
        var broken = RelationshipFixtures.Vote();
        await seeder.ActionAsync(good, "AwaitingReview", 100);
        await seeder.VoteAsync(broken, "AwaitingReview", 200);
        await seeder.ExecuteAsync("UPDATE vote_revisions SET revision_json = '{broken' WHERE record_id = @id", ("id", broken.RecordId));
        host.Client.DefaultRequestHeaders.Add("Cookie", host.CreateCookie("auth0|friend"));

        var home = await host.GetDocumentAsync("/Review");

        Assert.Null(home.QuerySelector(".notice-bad"));
        var rows = home.QuerySelectorAll("[aria-labelledby='needs-a-person'] tbody tr").ToArray();
        Assert.Equal(2, rows.Length);
        Assert.Contains("Cannot read", rows[0].TextContent, StringComparison.Ordinal);
        Assert.Contains("cannot be read", rows[0].TextContent, StringComparison.Ordinal);
        Assert.NotNull(rows[0].QuerySelector($"a[href='/Review/Votes/{broken.RecordId}']"));
        Assert.NotNull(rows[1].QuerySelector($"a[href='/Review/Actions/{good.RecordId}']"));
    }

    [Fact]
    public async Task TheReviewHomeSearchesApprovedRecordsByHeadlineAndLinksEachResultToItsEditorWithItsKindInPlainWordsAsync()
    {
        var seeder = new RelationshipWorldSeeder(host.ConnectionString);
        var action = RelationshipFixtures.Action(1);
        await seeder.ActionAsync(action, "Approved");
        var word = action.Headline.Split(' ').First(part => part.Length > 4 && part.All(char.IsLetter));
        host.Client.DefaultRequestHeaders.Add("Cookie", host.CreateCookie("auth0|friend"));

        var found = await host.GetDocumentAsync($"/Review?q={word}");
        var missing = await host.GetDocumentAsync("/Review?q=zzzunmatched");
        var blank = await host.GetDocumentAsync("/Review?q=%20");

        var row = Assert.Single(found.QuerySelectorAll("[aria-labelledby='search-results'] tbody tr"));
        Assert.NotNull(row.QuerySelector($"a[href='/Review/Actions/{action.RecordId}']"));
        Assert.Contains("Bill or resolution", row.TextContent, StringComparison.Ordinal);
        Assert.Equal("No approved record matches", missing.QuerySelector("[aria-labelledby='search-results'] .empty h3")!.TextContent);
        Assert.Contains("Type 1 to 300 characters", blank.QuerySelector(".notice-warn")!.TextContent, StringComparison.Ordinal);
        Assert.NotNull(found.QuerySelector("form[action='/Review'] input[name='find']"));
    }
}
