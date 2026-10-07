using CivicLens.Collection.Contracts;

namespace CivicLens.Application.Collection;

public sealed class CollectWatchedPage(ICollectorProcess collector)
{
    public async Task<CollectionResult> ExecuteAsync(CollectionConfiguration configuration, string sourceId,
        string artifactDirectory, CancellationToken cancellationToken = default, DateOnly? asOf = null)
    {
        var request = configuration.CreateRequest(sourceId, Guid.NewGuid().ToString("N"), Path.GetFullPath(artifactDirectory), asOf);
        var result = await collector.RunAsync(request, cancellationToken);
        result.ValidateAgainst(request);
        return result;
    }
}
