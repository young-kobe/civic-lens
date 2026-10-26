using CivicLens.Application.Actions;
using CivicLens.Application.Paging;
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
    private ListActionsNeedingAction listActions = null!;
    private ListVotesNeedingAction listVotes = null!;

    public async Task InitializeAsync()
    {
        connectionString = await postgres.CreateMigratedDatabaseAsync();
        seeder = new RelationshipWorldSeeder(connectionString);
        listActions = new ListActionsNeedingAction(PostgresActionReviewStore.FromConnectionString(connectionString));
        listVotes = new ListVotesNeedingAction(PostgresVoteReviewStore.FromConnectionString(connectionString));
    }

    public Task DisposeAsync() => postgres.DropDatabaseAsync(connectionString);

    [Fact]
    public async Task ActionsNeedingActionAreBoundedByTheLimitLeaveOutApprovedRecordsAndTotalEveryOne()
    {
        await SeedActionAsync('1', 11, revisedAt: 900, status: "Approved");
        await SeedActionAsync('2', 12, revisedAt: 100);
        await SeedActionAsync('3', 13, revisedAt: 300, status: "ChangesRequested");
        await SeedActionAsync('4', 14, revisedAt: 200, status: "ApprovalWithdrawn");

        var page = await listActions.ExecuteAsync(Reviewer, limit: 2);

        Assert.Equal(Ids('3', '4'), page.Items.Select(item => item.RecordId));
        Assert.Equal(3, page.Total);
    }

    [Fact]
    public async Task ANewRevisionOfAnOldActionMovesItAboveNewerRecords()
    {
        await SeedActionAsync('1', 11, revisedAt: 100);
        await SeedActionAsync('2', 12, revisedAt: 200);
        await SeedActionAsync('3', 13, revisedAt: 300);

        await new SaveActionDraft(PostgresActionReviewStore.FromConnectionString(connectionString)).ExecuteAsync(Reviewer,
            new(Id('1'), 1, "Person headline", "Person summary.", SuggestionChoice.Keep, "save-2"));

        var page = await listActions.ExecuteAsync(Reviewer);
        Assert.Equal(Ids('1', '3', '2'), page.Items.Select(item => item.RecordId));
    }

    [Fact]
    public async Task VotesNeedingActionAreBoundedByTheLimitLeaveOutApprovedRecordsAndTotalEveryOne()
    {
        await SeedVoteAsync('1', 101, revisedAt: 900, status: "Approved");
        await SeedVoteAsync('2', 102, revisedAt: 300);
        await SeedVoteAsync('3', 103, revisedAt: 100, status: "ChangesRequested");

        var page = await listVotes.ExecuteAsync(Reviewer, limit: 1);

        Assert.Equal(Ids('2'), page.Items.Select(item => item.RecordId));
        Assert.Equal(2, page.Total);
    }

    [Fact]
    public async Task AListWithoutTiersRefusesARankedCursorSoACursorFromAnotherListCannotMovePastItsRows()
    {
        var relationships = new ListRelationshipReviews(new PostgresRelationshipReviewStore(
            new PooledDbContextFactory<CollectionAttemptDbContext>(new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options)));

        await Assert.ThrowsAsync<ArgumentException>(() => relationships.ExecuteAsync(Reviewer, "o.1.5." + Id('1'), 1));
    }

    private static string Id(char digit) => new(digit, 64);

    private static string[] Ids(params char[] digits) => [.. digits.Select(Id)];

    private async Task SeedActionAsync(char id, int billNumber, long revisedAt, string status = "AwaitingReview")
    {
        var action = RelationshipFixtures.Action(billNumber) with { RecordId = Id(id), CandidateId = Id(id) };
        await seeder.ActionAsync(action, status, revisedAt);
    }

    private async Task SeedVoteAsync(char id, int rollCall, long revisedAt, string status = "AwaitingReview")
    {
        var vote = RelationshipFixtures.Vote() with { RecordId = Id(id), CandidateId = Id(id), RollCallNumber = rollCall };
        await seeder.VoteAsync(vote, status, revisedAt);
    }
}
