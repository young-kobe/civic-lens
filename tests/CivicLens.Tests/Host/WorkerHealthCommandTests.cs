using CivicLens.Infrastructure.Collection.Jobs;
using CivicLens.Tests.Fixtures;
using CivicLens.Tests.Infrastructure.Collection;

namespace CivicLens.Tests.Host;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class WorkerHealthCommandTests(PostgresCollection postgres) : IAsyncLifetime
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
    public async Task EveryLoopWithinThreeMinutesIsHealthy()
    {
        await store.BeatAsync("collection", default);
        await store.BeatAsync("gate", default);

        var result = await HostProcess.RunAsync(connectionString, "worker", "health");

        Assert.Equal(0, result.ExitCode);
        Assert.StartsWith("Healthy: the oldest heartbeat is loop ", result.Output);
        Assert.Contains(" seconds ago.", result.Output);
    }

    [Fact]
    public async Task OneStaleLoopMakesTheWorkerUnhealthyEvenWhenOtherLoopsBeatSoAHungLoopIsNotHidden()
    {
        await store.BeatAsync("collection", default);
        await store.BeatAsync("gate", default);
        await BriefWorldSeeder.RunAsync(connectionString,
            "UPDATE worker_heartbeat SET beat_at_utc_ticks = beat_at_utc_ticks - 1810000000 WHERE loop = 'gate'");

        var result = await HostProcess.RunAsync(connectionString, "worker", "health");

        Assert.Equal(1, result.ExitCode);
        Assert.Matches(@"loop gate last beat 18\d seconds ago", result.Output);
        Assert.DoesNotContain("loop collection", result.Output);
    }

    [Fact]
    public async Task AMissingHeartbeatIsUnhealthyBecauseAWorkerThatNeverStartedIsNotAlive()
    {
        var result = await HostProcess.RunAsync(connectionString, "worker", "health");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("no heartbeat", result.Output);
    }
}
