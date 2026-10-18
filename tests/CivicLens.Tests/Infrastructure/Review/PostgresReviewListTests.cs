using CivicLens.Application.Actions;
using CivicLens.Application.Relationships;
using CivicLens.Application.Review;
using CivicLens.Application.Votes;
using CivicLens.Core.Actions;
using CivicLens.Core.Review;
using CivicLens.Core.Votes;
using CivicLens.Infrastructure.Actions;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Relationships;
using CivicLens.Infrastructure.Votes;
using CivicLens.Tests.Fixtures;
using CivicLens.Tests.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace CivicLens.Tests.Infrastructure.Review;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class PostgresReviewListTests(PostgresCollection postgres) : IAsyncLifetime
{
    private const string Housing = "housing";
    private const string Health = "health";
    private static readonly ReviewActor Reviewer = new("auth0|reviewer", ReviewRole.Reviewer);
    private string connectionString = null!;
    private RelationshipWorldSeeder seeder = null!;
    private ListActionReviews listActions = null!;
    private ListVoteReviews listVotes = null!;

    public async Task InitializeAsync()
    {
        connectionString = await postgres.CreateMigratedDatabaseAsync();
        seeder = new RelationshipWorldSeeder(connectionString);
        listActions = new ListActionReviews(PostgresActionReviewStore.FromConnectionString(connectionString));
        listVotes = new ListVoteReviews(PostgresVoteReviewStore.FromConnectionString(connectionString));
    }

    public Task DisposeAsync() => postgres.DropDatabaseAsync(connectionString);

    [Fact]
    public async Task ActionsListTierOneBeforeANewerTierThreeAndTheNewestEventFirstWithinATierAcrossEveryPageBoundary()
    {
        await SeedActionAsync('1', 11, new DateOnly(2025, 1, 1), tier: 1, "Approved");
        await SeedActionAsync('2', 12, new DateOnly(2025, 2, 1), tier: 2);
        await SeedActionAsync('3', 13, new DateOnly(2025, 6, 1), tier: 3);
        await SeedActionAsync('4', 14, new DateOnly(2025, 3, 1), tier: 3);

        async Task<PageWalk> Walk(DraftStatusFilter status) => await PageWalk.RunAsync(async cursor =>
        {
            var page = await listActions.ExecuteAsync(Reviewer, cursor?.Encode(), 1, new DraftListFilter(status));
            return ([.. page.Items.Select(item => item.RecordId)], page.NewerCursor, page.OlderCursor);
        });

        (await Walk(DraftStatusFilter.All)).AssertExact(Ids('1', '2', '3', '4'));
        (await Walk(DraftStatusFilter.NeedsAction)).AssertExact(Ids('2', '3', '4'));
        (await Walk(DraftStatusFilter.Approved)).AssertExact(Ids('1'));
        var page = await listActions.ExecuteAsync(Reviewer, null, 10);
        Assert.Equal([1, 2, 3, 3], page.Items.Select(item => page.Priorities[item.RecordId]));
    }

    [Fact]
    public async Task ActionsWithoutAnAnalysisRowCountAsTheLowestTierSoTheyListLast()
    {
        await SeedActionAsync('1', 11, new DateOnly(2025, 1, 1), tier: null);
        await SeedActionAsync('2', 12, new DateOnly(2025, 2, 1), tier: 3);
        await SeedActionAsync('3', 13, new DateOnly(2025, 3, 1), tier: 2);

        var page = await listActions.ExecuteAsync(Reviewer, null, 10);

        Assert.Equal(Ids('3', '2', '1'), page.Items.Select(item => item.RecordId));
        Assert.Equal(ReviewPriority.Lowest, page.Priorities[Id('1')]);
    }

    [Fact]
    public async Task ActionFiltersKeepOnlyTheMatchingIssueOfficialAndEventDateAndCombineWithTheStatus()
    {
        await SeedActionAsync('1', 11, new DateOnly(2025, 3, 1), tier: 3, issues: [Housing], official: "warren");
        await SeedActionAsync('2', 12, new DateOnly(2025, 5, 1), tier: 3, "Approved", [Housing, Health], "markey");
        await SeedActionAsync('3', 13, new DateOnly(2025, 7, 1), tier: 3, issues: [], official: "warren");

        Assert.Equal(Ids('2', '1'), await ActionIdsAsync(new(DraftStatusFilter.All, IssueId: Housing)));
        Assert.Equal(Ids('2'), await ActionIdsAsync(new(DraftStatusFilter.All, IssueId: Health)));
        Assert.Equal(Ids('3', '1'), await ActionIdsAsync(new(DraftStatusFilter.All, OfficialId: "warren")));
        Assert.Equal(Ids('2'), await ActionIdsAsync(new(DraftStatusFilter.All, From: new DateOnly(2025, 4, 1), To: new DateOnly(2025, 6, 30))));
        Assert.Equal(Ids('3', '2'), await ActionIdsAsync(new(DraftStatusFilter.All, From: new DateOnly(2025, 5, 1))));
        Assert.Equal(Ids('2', '1'), await ActionIdsAsync(new(DraftStatusFilter.All, To: new DateOnly(2025, 5, 1))));
        Assert.Equal(Ids('1'), await ActionIdsAsync(new(DraftStatusFilter.NeedsAction, IssueId: Housing)));
        Assert.Empty(await ActionIdsAsync(new(DraftStatusFilter.Approved, OfficialId: "warren")));
        Assert.Equal(Ids('1'), await ActionIdsAsync(new(DraftStatusFilter.All, IssueId: Housing, OfficialId: "warren",
            From: new DateOnly(2025, 1, 1), To: new DateOnly(2025, 3, 1))));
    }

    [Fact]
    public async Task APageWalkWithAFilterStillReturnsEveryMatchingRowOnceInOrder()
    {
        for (var index = 0; index < 5; index++)
            await SeedActionAsync((char)('1' + index), 11 + index, new DateOnly(2025, 1, 1 + index), tier: index % 2 + 1, issues: [Housing]);
        await SeedActionAsync('9', 19, new DateOnly(2025, 1, 20), tier: 1, issues: [Health]);

        var walk = await PageWalk.RunAsync(async cursor =>
        {
            var page = await listActions.ExecuteAsync(Reviewer, cursor?.Encode(), 2, new DraftListFilter(DraftStatusFilter.All, IssueId: Housing));
            return ([.. page.Items.Select(item => item.RecordId)], page.NewerCursor, page.OlderCursor);
        });

        walk.AssertExact(Ids('5', '3', '1', '4', '2'));
    }

    [Fact]
    public async Task VotesListByTierThenTheNewestVoteDateAndFilterByIssueOfficialAndDate()
    {
        await SeedVoteAsync('1', 101, new DateOnly(2025, 4, 1), tier: 3, issues: [Housing], member: "warren");
        await SeedVoteAsync('2', 102, new DateOnly(2025, 2, 1), tier: 1, "Approved", [Health], "markey");
        await SeedVoteAsync('3', 103, new DateOnly(2025, 6, 1), tier: 3, issues: [Housing], member: "markey");

        var walk = await PageWalk.RunAsync(async cursor =>
        {
            var page = await listVotes.ExecuteAsync(Reviewer, cursor?.Encode(), 1);
            return ([.. page.Items.Select(item => item.RecordId)], page.NewerCursor, page.OlderCursor);
        });

        walk.AssertExact(Ids('2', '3', '1'));
        Assert.Equal(Ids('3', '1'), await VoteIdsAsync(new(DraftStatusFilter.All, IssueId: Housing)));
        Assert.Equal(Ids('2', '3'), await VoteIdsAsync(new(DraftStatusFilter.All, OfficialId: "markey")));
        Assert.Equal(Ids('1'), await VoteIdsAsync(new(DraftStatusFilter.All, From: new DateOnly(2025, 3, 1), To: new DateOnly(2025, 5, 1))));
        Assert.Equal(Ids('3'), await VoteIdsAsync(new(DraftStatusFilter.NeedsAction, OfficialId: "markey")));
        Assert.Equal(Ids('2'), await VoteIdsAsync(new(DraftStatusFilter.Approved, OfficialId: "markey")));
    }

    [Fact]
    public async Task ATieredListIssuesRankedCursorsAndRefusesATwoPartOne()
    {
        await SeedActionAsync('1', 11, new DateOnly(2025, 1, 1), tier: 1);
        await SeedActionAsync('2', 12, new DateOnly(2025, 1, 2), tier: 2);

        var first = await listActions.ExecuteAsync(Reviewer, null, 1);

        Assert.Equal($"o.1.{new DateTime(2025, 1, 1).Ticks}.{Id('1')}", first.OlderCursor);
        await Assert.ThrowsAsync<ArgumentException>(() => listActions.ExecuteAsync(Reviewer, "o.5." + Id('1'), 1));
    }

    [Fact]
    public async Task AListWithoutTiersRefusesARankedCursorSoACursorFromAnotherListCannotMovePastItsRows()
    {
        var relationships = new ListRelationshipReviews(new PostgresRelationshipReviewStore(
            new PooledDbContextFactory<CollectionAttemptDbContext>(new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options)));

        await Assert.ThrowsAsync<ArgumentException>(() => relationships.ExecuteAsync(Reviewer, "o.1.5." + Id('1'), 1));
    }

    private async Task<string[]> ActionIdsAsync(DraftListFilter filter) =>
        [.. (await listActions.ExecuteAsync(Reviewer, null, 10, filter)).Items.Select(item => item.RecordId)];

    private async Task<string[]> VoteIdsAsync(DraftListFilter filter) =>
        [.. (await listVotes.ExecuteAsync(Reviewer, null, 10, filter)).Items.Select(item => item.RecordId)];

    private static string Id(char digit) => new(digit, 64);

    private static string[] Ids(params char[] digits) => [.. digits.Select(Id)];

    private async Task SeedActionAsync(char id, int billNumber, DateOnly sponsoredOn, int? tier, string status = "AwaitingReview",
        string[]? issues = null, string official = "warren")
    {
        var action = RelationshipFixtures.Action(billNumber) with
        {
            RecordId = Id(id),
            CandidateId = Id(id),
            TrackedIssueIds = [.. issues ?? []],
            Sponsorships = [ActionRevisionFixtures.Sponsorship() with { PersonId = official, SponsoredOn = sponsoredOn }]
        };
        await seeder.ActionAsync(action, status);
        if (tier is { } value) await seeder.PriorityAsync("action_analysis", action.CandidateId, ActionDraftTask.Task, value);
    }

    private async Task SeedVoteAsync(char id, int rollCall, DateOnly votedOn, int? tier, string status = "AwaitingReview",
        string[]? issues = null, string member = "warren")
    {
        var vote = RelationshipFixtures.Vote() with
        {
            RecordId = Id(id),
            CandidateId = Id(id),
            RollCallNumber = rollCall,
            VotedOn = votedOn,
            TrackedIssueIds = [.. issues ?? []],
            Members = [RelationshipFixtures.Vote().Members[0] with { PersonId = member }]
        };
        await seeder.VoteAsync(vote, status);
        if (tier is { } value) await seeder.PriorityAsync("vote_analysis", vote.CandidateId, VoteDraftTask.Task, value);
    }
}
