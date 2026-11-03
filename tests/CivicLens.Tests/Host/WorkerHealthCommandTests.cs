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
    public async Task EveryLoopWithinTenMinutesIsHealthy()
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
            "UPDATE worker_heartbeat SET beat_at_utc_ticks = beat_at_utc_ticks - 6010000000 WHERE loop = 'gate'");

        var result = await HostProcess.RunAsync(connectionString, "worker", "health");

        Assert.Equal(1, result.ExitCode);
        Assert.Matches(@"loop gate last beat 60\d seconds ago", result.Output);
        Assert.DoesNotContain("loop collection", result.Output);
    }

    [Fact]
    public async Task AMissingHeartbeatIsUnhealthyBecauseAWorkerThatNeverStartedIsNotAlive()
    {
        var result = await HostProcess.RunAsync(connectionString, "worker", "health");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("no heartbeat", result.Output);
    }

    [Fact]
    public async Task TheReleaseDirectoryMakesHealthReadTheActiveReleaseWhenRecordsWaitBecauseProgramMustWireTheCapCheck()
    {
        await BriefWorldSeeder.RunAsync(connectionString, ApprovalDrafts.DropForeignKeys);
        await BriefWorldSeeder.RunAsync(connectionString, ApprovalDrafts.Inserts["executive_action_drafts"].Replace("'AwaitingReview'", "'Approved'"));
        await BriefWorldSeeder.RunAsync(connectionString, """
            INSERT INTO publication_releases (release_number, directory_name, actor_subject, published_at_utc_ticks, record_count, is_active)
            VALUES (1, '000001-' || md5('missing'), 'owner', 0, 0, true)
            """);
        await store.BeatAsync("collection", default);
        var folder = Directory.CreateTempSubdirectory("worker-health-");
        try
        {
            var without = await HostProcess.RunAsync(connectionString, "worker", "health");
            var with = await HostProcess.RunWithEnvironmentAsync(connectionString, null,
                new Dictionary<string, string> { ["CIVIC_LENS_RELEASE_DIRECTORY"] = folder.FullName }, "worker", "health");

            Assert.Equal(0, without.ExitCode);
            Assert.NotEqual(0, with.ExitCode);
            Assert.Contains("Command failed", with.Error);
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }
}
