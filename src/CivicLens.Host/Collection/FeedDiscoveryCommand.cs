using System.Text.Json;
using CivicLens.Application.Collection;
using CivicLens.Application.Collection.Discovery;
using CivicLens.Collection.Contracts;

namespace CivicLens.Host.Collection;

internal static class FeedDiscoveryCommand
{
    public static async Task<int> InspectAsync(ICollectionAttemptStore evidence, string attemptId,
        CancellationToken cancellationToken)
    {
        var attempt = await evidence.GetAsync(attemptId, cancellationToken);
        if (attempt?.Discovery is not { } discovery)
        {
            Console.Error.WriteLine("Feed discovery not found. Use the captured feed attempt ID.");
            return 2;
        }
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            attemptId,
            sourceId = attempt.AttemptResult.SourceId,
            discovery.Status,
            discovery.Urls
        }, CollectionProtocol.JsonOptions));
        return 0;
    }

    public static async Task<int> AdmitAsync(IFeedAdmissionStore store, FeedAdmissionRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await store.AdmitAsync(request, cancellationToken);
            Console.WriteLine(JsonSerializer.Serialize(result, CollectionProtocol.JsonOptions));
            return 0;
        }
        catch (ArgumentException)
        {
            Console.Error.WriteLine("Invalid admission. Check the feed attempt, source scope, limits, and idempotency key.");
            return 2;
        }
    }
}
