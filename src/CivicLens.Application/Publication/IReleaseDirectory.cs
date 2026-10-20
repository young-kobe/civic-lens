namespace CivicLens.Application.Publication;

public interface IReleaseDirectory
{
    Task<IReleaseStaging> BeginAsync(CancellationToken cancellationToken);
    Task<byte[]> ReadFileAsync(string directoryName, string relativePath, int maximumBytes, CancellationToken cancellationToken);
    Task<IReadOnlyList<string>> ListFilesAsync(string directoryName, string folder, CancellationToken cancellationToken);
    Task DeleteAsync(string directoryName, CancellationToken cancellationToken);
    Task ActivateAsync(string directoryName, CancellationToken cancellationToken);
    Task<string?> GetActiveAsync(CancellationToken cancellationToken);
}
