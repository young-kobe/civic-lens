namespace CivicLens.Application;

public interface IWorkerWakeup : IAsyncDisposable
{
    Task ConnectAsync(CancellationToken cancellationToken);
    Task WaitAsync(CancellationToken cancellationToken);
}
