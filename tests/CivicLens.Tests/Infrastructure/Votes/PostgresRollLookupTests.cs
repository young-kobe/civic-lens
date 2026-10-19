using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Votes;
using CivicLens.Tests.Fixtures;
using CivicLens.Tests.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace CivicLens.Tests.Infrastructure.Votes;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class PostgresRollLookupTests(PostgresCollection postgres) : IAsyncLifetime
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
}
