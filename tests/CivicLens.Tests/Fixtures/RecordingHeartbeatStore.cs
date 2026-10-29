using CivicLens.Application;

namespace CivicLens.Tests.Fixtures;

/// <summary>A heartbeat store that counts each write. It can fail every write.</summary>
internal sealed class RecordingHeartbeatStore(bool failing = false) : IWorkerHeartbeatStore
{
    private int beats;

    public int Beats => Volatile.Read(ref beats);

    public Task BeatAsync(string loop, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref beats);
        return failing ? Task.FromException(new InvalidOperationException("down")) : Task.CompletedTask;
    }

    public Task ClearAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<IReadOnlyList<WorkerHeartbeat>> ListAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<WorkerHeartbeat>>([]);
}
