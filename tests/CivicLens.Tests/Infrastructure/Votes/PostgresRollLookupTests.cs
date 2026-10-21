using CivicLens.Core.Votes;
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
    public async Task ARollCallFindsOnlyTheVoteDraftOfItsChamberCongressSessionAndNumber()
    {
        var house = RelationshipFixtures.Vote();
        var senate = house with { RecordId = new string('7', 64), CandidateId = new string('8', 64), Chamber = Chamber.Senate };
        var seeder = new RelationshipWorldSeeder(connectionString);
        await seeder.VoteAsync(house);
        await seeder.VoteAsync(senate);
        var store = new PostgresVoteReviewStore(new PooledDbContextFactory<CollectionAttemptDbContext>(
            new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options));

        Assert.Equal(house.RecordId, await store.FindRecordIdByRollAsync(Chamber.House, house.Congress, house.SessionNumber, house.RollCallNumber, default));
        Assert.Equal(senate.RecordId, await store.FindRecordIdByRollAsync(Chamber.Senate, house.Congress, house.SessionNumber, house.RollCallNumber, default));
        Assert.Null(await store.FindRecordIdByRollAsync(Chamber.House, house.Congress, house.SessionNumber + 1, house.RollCallNumber, default));
        Assert.Null(await store.FindRecordIdByRollAsync(Chamber.House, house.Congress - 1, house.SessionNumber, house.RollCallNumber, default));
        Assert.Null(await store.FindRecordIdByRollAsync(Chamber.House, house.Congress, house.SessionNumber, house.RollCallNumber + 1, default));
    }
}
