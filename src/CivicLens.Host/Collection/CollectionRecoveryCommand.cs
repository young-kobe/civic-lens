using System.Text.Json;
using CivicLens.Application.Collection;
using CivicLens.Collection.Contracts;

namespace CivicLens.Host.Collection;

internal static class CollectionRecoveryCommand
{
    public static async Task<int> ExecuteAsync(RecoverCollectionAttempts recovery, string artifactRoot,
        string selection, CancellationToken cancellationToken)
    {
        if (selection != "--all")
        {
            var completion = await recovery.ReplayAsync(selection, artifactRoot, cancellationToken);
            return CollectionCommandOutput.WriteCompletion(completion);
        }

        var results = await recovery.ReplayAllAsync(artifactRoot, cancellationToken);
        var exitCode = 0;
        foreach (var result in results)
        {
            var removed = result.Status == CollectionRecoveryStatus.ImportedAndRemoved;
            var import = result.Decision is null ? null : CollectionCommandOutput.Create(result.Decision, removed);
            var output = new CollectionRecoveryOutput(result.HandoffId,
                JsonNamingPolicy.CamelCase.ConvertName(result.Status.ToString()), import, result.Error);
            Console.WriteLine(JsonSerializer.Serialize(output, CollectionProtocol.JsonOptions));
            if (!removed || import?.Outcome is not (CollectionOutcome.Captured or CollectionOutcome.NotModified))
                exitCode = 1;
        }
        return exitCode;
    }
}
