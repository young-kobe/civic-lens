namespace CivicLens.Application.Publication;

public interface IReleaseStaging : IAsyncDisposable
{
    Task WriteFileAsync(string relativePath, ReadOnlyMemory<byte> content, CancellationToken cancellationToken);

    Task<string> CompleteAsync(int releaseNumber, CancellationToken cancellationToken);
}
