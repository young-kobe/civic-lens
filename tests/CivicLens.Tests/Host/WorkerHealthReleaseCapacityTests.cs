using CivicLens.Application;
using CivicLens.Application.Publication;
using CivicLens.Host.Collection;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.Application.Publication;

namespace CivicLens.Tests.Host;

public sealed class WorkerHealthReleaseCapacityTests
{
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
        Assert.Contains("Healthy: the oldest heartbeat is", output.ToString());
        Assert.Contains($"Unhealthy: The release holds {PublicationProtocol.MaximumRecordsPerRelease} records. 3 new records wait for room.", output.ToString());
    }

    [Fact]
    public async Task WorkerHealthStaysHealthyWhenTheWaitingRecordsFitBecauseOnlyAFullReleaseNeedsTheOwner()
    {
        var scenario = new PublicationScenario();
        scenario.SeedRelease(10, withFiles: false);
        scenario.Publications.ApprovedOutsideActiveRelease = 3;
        var output = new StringWriter();

        var exitCode = await WorkerHealthCommand.ExecuteAsync(new BeatingHeartbeats(),
            new ReadReleaseCapacity(scenario.Publications, scenario.Releases), output, default);

        Assert.Equal(0, exitCode);
        Assert.DoesNotContain("Unhealthy", output.ToString());
    }

    private sealed class BeatingHeartbeats : IWorkerHeartbeatStore
    {
        public Task BeatAsync(string loop, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ClearAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IReadOnlyList<WorkerHeartbeat>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<WorkerHeartbeat>>([new("publishing", TimeSpan.FromSeconds(5))]);
    }
}
