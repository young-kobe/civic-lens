using CivicLens.Infrastructure.Actions;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Votes;
using CivicLens.Tests.Fixtures;
using CivicLens.Tests.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace CivicLens.Tests.Infrastructure.Votes;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class PostgresDraftLookupTests(PostgresCollection postgres) : IAsyncLifetime
{
    private string connectionString = null!;

    public async Task InitializeAsync() => connectionString = await postgres.CreateMigratedDatabaseAsync();

    public Task DisposeAsync() => postgres.DropDatabaseAsync(connectionString);

    [Fact]
    public async Task ARollCallFindsOnlyTheVoteDraftOfItsCongressSessionAndNumber()
    {
        var vote = RelationshipFixtures.Vote();
        await new RelationshipWorldSeeder(connectionString).VoteAsync(vote);
        var store = new PostgresVoteReviewStore(new PooledDbContextFactory<CollectionAttemptDbContext>(
            new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options));

        Assert.Equal(vote.RecordId, await store.FindRecordIdByRollAsync(vote.Congress, vote.SessionNumber, vote.RollCallNumber, default));
        Assert.Null(await store.FindRecordIdByRollAsync(vote.Congress, vote.SessionNumber + 1, vote.RollCallNumber, default));
        Assert.Null(await store.FindRecordIdByRollAsync(vote.Congress - 1, vote.SessionNumber, vote.RollCallNumber, default));
        Assert.Null(await store.FindRecordIdByRollAsync(vote.Congress, vote.SessionNumber, vote.RollCallNumber + 1, default));
    }

    [Fact]
    public async Task ABillFindsOnlyTheActionDraftOfItsCongressTypeAndNumber()
    {
        var action = RelationshipFixtures.Action(1234);
        await new RelationshipWorldSeeder(connectionString).ActionAsync(action);
        var store = new PostgresActionReviewStore(new PooledDbContextFactory<CollectionAttemptDbContext>(
            new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options));

        Assert.Equal(action.RecordId, await store.FindRecordIdByBillAsync(action.Congress, action.BillType, action.BillNumber, default));
        Assert.Null(await store.FindRecordIdByBillAsync(action.Congress - 1, action.BillType, action.BillNumber, default));
        Assert.Null(await store.FindRecordIdByBillAsync(action.Congress, "s", action.BillNumber, default));
        Assert.Null(await store.FindRecordIdByBillAsync(action.Congress, action.BillType, action.BillNumber + 1, default));
    }
}
