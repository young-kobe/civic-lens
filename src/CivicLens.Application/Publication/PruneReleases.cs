using Microsoft.Extensions.Logging;

namespace CivicLens.Application.Publication;

public sealed class PruneReleases(IPublicationStore publications, IReleaseDirectory releases, ILogger<PruneReleases> logger)
{
    public const int KeepReleases = 30;

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await publications.PruneAsync(KeepReleases, DeleteUnservedAsync, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Old release files could not be pruned. The next publish tries again.");
        }
    }

    // The store holds the publication lock, so the served release cannot change while this reads it.
    private async Task DeleteUnservedAsync(IReadOnlyList<string> directoryNames, CancellationToken cancellationToken)
    {
        var served = await releases.GetActiveAsync(cancellationToken);
        foreach (var directoryName in directoryNames.Where(name => name != served))
            await releases.DeleteAsync(directoryName, cancellationToken);
    }
}
