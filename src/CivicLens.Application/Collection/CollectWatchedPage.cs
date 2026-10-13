using CivicLens.Collection.Contracts;

namespace CivicLens.Application.Collection;

public sealed class CollectWatchedPage(ICollectorProcess collector)
{
    public async Task<CollectionResult> ExecuteAsync(PreparedCollectionAttempt attempt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        var result = await collector.RunAsync(attempt.Request, cancellationToken);
        result.ValidateAgainst(attempt.Request);
        return result;
    }
}
