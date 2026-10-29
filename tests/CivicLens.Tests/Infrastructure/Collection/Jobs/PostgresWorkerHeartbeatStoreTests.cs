using CivicLens.Infrastructure.Collection.Jobs;

namespace CivicLens.Tests.Infrastructure.Collection.Jobs;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class PostgresWorkerHeartbeatStoreTests(PostgresCollection postgres) : IAsyncLifetime
{
    private string connectionString = null!;
    private PostgresWorkerHeartbeatStore store = null!;

    public async Task InitializeAsync()
    {
        connectionString = await postgres.CreateMigratedDatabaseAsync();
        store = PostgresWorkerHeartbeatStore.FromConnectionString(connectionString);
    }

    public Task DisposeAsync() => postgres.DropDatabaseAsync(connectionString);

    [Fact]
    public async Task EachLoopKeepsOneRowWhoseAgeComesFromTheDatabaseClockAndGrowsUntilTheNextBeat()
    {
        Assert.Empty(await store.ListAsync(default));

        await store.BeatAsync("gate", default);
        await store.BeatAsync("collection", default);
        await Task.Delay(300);
        var aged = await store.ListAsync(default);
        await store.BeatAsync("gate", default);
        var refreshed = await store.ListAsync(default);

        Assert.Equal(["collection", "gate"], aged.Select(beat => beat.Loop));
        Assert.All(aged, beat => Assert.InRange(beat.Age, TimeSpan.FromMilliseconds(250), TimeSpan.FromSeconds(30)));
        Assert.Equal(2, refreshed.Count);
        Assert.True(refreshed.Single(beat => beat.Loop == "gate").Age < refreshed.Single(beat => beat.Loop == "collection").Age);
    }

    [Fact]
    public async Task ClearingRemovesEveryRowSoAWorkerRestartDoesNotInheritAnOldBeat()
    {
        await store.BeatAsync("gate", default);

        await store.ClearAsync(default);

        Assert.Empty(await store.ListAsync(default));
    }
}
