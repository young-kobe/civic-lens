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

        var stat = home.QuerySelector("a.stat[href='#fact-exceptions']")!;
        Assert.Equal(["3", "Actions and votes need action"], stat.Children.Select(child => child.TextContent));
        var links = home.QuerySelectorAll("[aria-labelledby='fact-exceptions'] tbody a.cell-title").Select(link => link.GetAttribute("href"));
        Assert.Equivalent(new[] { $"/Review/Actions/{refused.RecordId}", $"/Review/Actions/{changesRequested.RecordId}", $"/Review/Votes/{refusedVote.RecordId}" }, links);
        Assert.Null(home.QuerySelector($"a[href='/Review/Actions/{approved.RecordId}']"));
        Assert.Null(home.QuerySelector($"a[href='/Review/Votes/{approvedVote.RecordId}']"));
        Assert.Null(home.QuerySelector(".nav a[href='/Review/Actions']"));
        Assert.Null(home.QuerySelector(".nav a[href='/Review/Votes']"));
    }

    [Fact]
    public async Task TheReviewHomeShowsNoUnapprovedFactWhenCodeApprovedEveryActionAndVoteAsync()
    {
        var seeder = new RelationshipWorldSeeder(host.ConnectionString);
        await seeder.ActionAsync(RelationshipFixtures.Action(1), "Approved");
        await seeder.VoteAsync(RelationshipFixtures.Vote(), "Approved");
        host.Client.DefaultRequestHeaders.Add("Cookie", host.CreateCookie("auth0|friend"));

        var home = await host.GetDocumentAsync("/Review");

        Assert.Equal("0", home.QuerySelector("a.stat[href='#fact-exceptions']")!.Children[0].TextContent);
        Assert.Equal("No action or vote needs action", home.QuerySelector("[aria-labelledby='fact-exceptions'] .empty h3")!.TextContent);
    }

    [Fact]
    public async Task TheReviewHomeShowsOnlyOnePageOfEachKindButCountsEveryRecordAsync()
    {
        var seeder = new RelationshipWorldSeeder(host.ConnectionString);
        for (var index = 0; index < 11; index++)
        {
            var id = new string(index.ToString("x")[0], 64);
            await seeder.ActionAsync(RelationshipFixtures.Action(index + 1) with { RecordId = id, CandidateId = id }, "AwaitingReview", index);
        }
        host.Client.DefaultRequestHeaders.Add("Cookie", host.CreateCookie("auth0|friend"));

        var home = await host.GetDocumentAsync("/Review");

        var panel = home.QuerySelector("[aria-labelledby='fact-exceptions']")!;
        Assert.Equal(10, panel.QuerySelectorAll("tbody tr").Length);
        Assert.Contains("11 records", panel.TextContent, StringComparison.Ordinal);
        Assert.Null(panel.QuerySelector($"a[href='/Review/Actions/{new string('0', 64)}']"));
    }

    [Fact]
    public async Task TheReviewHomeMergesActionsAndVotesByLastRevisionNewestFirstAsync()
    {
        var seeder = new RelationshipWorldSeeder(host.ConnectionString);
        var oldAction = RelationshipFixtures.Action(1) with { CreatedAtUtc = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero) };
        var middleAction = RelationshipFixtures.Action(2) with
        {
            RecordId = new string('3', 64),
            CandidateId = new string('3', 64),
            CreatedAtUtc = new DateTimeOffset(2025, 3, 1, 0, 0, 0, TimeSpan.Zero)
        };
        var newVote = RelationshipFixtures.Vote() with { CreatedAtUtc = new DateTimeOffset(2025, 6, 1, 0, 0, 0, TimeSpan.Zero) };
        await seeder.ActionAsync(oldAction, "AwaitingReview", oldAction.CreatedAtUtc.UtcTicks);
        await seeder.ActionAsync(middleAction, "AwaitingReview", middleAction.CreatedAtUtc.UtcTicks);
        await seeder.VoteAsync(newVote, "AwaitingReview", newVote.CreatedAtUtc.UtcTicks);
        host.Client.DefaultRequestHeaders.Add("Cookie", host.CreateCookie("auth0|friend"));

        var home = await host.GetDocumentAsync("/Review");

        Assert.Equal([$"/Review/Votes/{newVote.RecordId}", $"/Review/Actions/{middleAction.RecordId}", $"/Review/Actions/{oldAction.RecordId}"],
            home.QuerySelectorAll("[aria-labelledby='fact-exceptions'] tbody a.cell-title").Select(link => link.GetAttribute("href")));
    }
}
