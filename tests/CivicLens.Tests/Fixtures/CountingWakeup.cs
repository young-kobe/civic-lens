using CivicLens.Application;

namespace CivicLens.Tests.Fixtures;

/// <summary>A wakeup that returns at once and counts the waits. The callback runs at each wait.</summary>
internal sealed class CountingWakeup(Action? onWait = null) : IWorkerWakeup
{
    public int WaitCount { get; private set; }

    public Task ConnectAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task WaitAsync(CancellationToken cancellationToken)
    {
        WaitCount++;
        onWait?.Invoke();
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
