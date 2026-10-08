namespace CivicLens.Application.Collection.Jobs;

/// <summary>Waits for durable pipeline work without periodic polling.</summary>
public interface ICollectionPipelineWakeup : IAsyncDisposable
{
    Task ConnectAsync(CancellationToken cancellationToken);
    Task WaitAsync(CancellationToken cancellationToken);
}
