namespace CivicLens.Application;

public interface IWorkerHeartbeatStore
{
    Task BeatAsync(string loop, CancellationToken cancellationToken);
    Task ClearAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<WorkerHeartbeat>> ListAsync(CancellationToken cancellationToken);
}
