using CivicLens.Application;
using CivicLens.Application.Publication;
using CivicLens.Host.Collection;
using CivicLens.Publication.Contracts;

namespace CivicLens.Tests.Application.Publication;

public sealed class ReadReleaseCapacityTests
{
    [Fact]
    public async Task TheHeldCountAddsWithdrawalsToRecordsBecauseTheyShareTheCap()
    {
        var scenario = new PublicationScenario();
        scenario.SeedRelease(PublicationProtocol.MaximumRecordsPerRelease - 5, withFiles: false);
        scenario.Publications.ApprovedOutsideActiveRelease = 12;

        var capacity = await new ReadReleaseCapacity(scenario.Publications, scenario.Releases).ExecuteAsync(default);

        Assert.Equal(new ReleaseCapacity(PublicationProtocol.MaximumRecordsPerRelease - 5, 7), capacity);
    }

    [Fact]
    public async Task NoRecordWaitsForRoomWhileTheBacklogFitsTheFreeSlots()
    {
        var scenario = new PublicationScenario();
        scenario.SeedRelease(10, withFiles: false);
        scenario.Publications.ApprovedOutsideActiveRelease = 100;

        var capacity = await new ReadReleaseCapacity(scenario.Publications, scenario.Releases).ExecuteAsync(default);

        Assert.Equal(new ReleaseCapacity(10, 0), capacity);
    }

    [Fact]
    public async Task WorkerHealthReportsTheCapStateBecauseTheOwnerMustSeeNewRecordsThatWaitForRoom()
    {
        var scenario = new PublicationScenario();
        scenario.SeedRelease(PublicationProtocol.MaximumRecordsPerRelease, withFiles: false);
        scenario.Publications.ApprovedOutsideActiveRelease = 3;
        var output = new StringWriter();

        var exitCode = await WorkerHealthCommand.ExecuteAsync(new BeatingHeartbeats(),
            new ReadReleaseCapacity(scenario.Publications, scenario.Releases), output, default);

        Assert.Equal(1, exitCode);
        Assert.Contains($"The release holds {PublicationProtocol.MaximumRecordsPerRelease} records. 3 new records wait for room.", output.ToString());
    }

    private sealed class BeatingHeartbeats : IWorkerHeartbeatStore
    {
        public Task BeatAsync(string loop, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ClearAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IReadOnlyList<WorkerHeartbeat>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<WorkerHeartbeat>>([new("publishing", TimeSpan.FromSeconds(5))]);
    }
}
