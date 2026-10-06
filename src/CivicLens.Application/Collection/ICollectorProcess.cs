using CivicLens.Collection.Contracts;

namespace CivicLens.Application.Collection;

/// <summary>Executes bounded collection and returns a receipt after verifying any referenced capture.</summary>
public interface ICollectorProcess
{
    Task<CollectionResult> RunAsync(CollectionRequest request, CancellationToken cancellationToken);
}
