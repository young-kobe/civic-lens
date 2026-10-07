using System.Text.Json;
using CivicLens.Application.Collection;
using CivicLens.Application.Collection.Discovery;
using CivicLens.Collection.Contracts;

namespace CivicLens.Host.Collection;

internal static class DiscoveryCommand
{
    public static async Task<int> InspectAsync(ICollectionAttemptStore evidence, string attemptId,
        CancellationToken cancellationToken)
    {
        var attempt = await evidence.GetAsync(attemptId, cancellationToken);
        if (attempt?.Discovery is not { } discovery)
        {
            Console.Error.WriteLine("Discovery not found. Use the captured feed or HTML attempt ID.");
            return 2;
        }
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            attemptId,
            sourceId = attempt.AttemptResult.SourceId,
            mode = discovery.Request.Mode,
            discovery.Status,
            discovery.Urls
        }, CollectionProtocol.JsonOptions));
        return 0;
    }

    public static async Task<int> AdmitAsync(IDiscoveryAdmissionStore store, DiscoveryAdmissionRequest request,
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
            Console.Error.WriteLine("Invalid admission. Check the discovery attempt, source scope, limits, and idempotency key.");
            return 2;
        }
    }
}
