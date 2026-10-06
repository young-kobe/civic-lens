using System.Text.Json;
using CivicLens.Application.Collection;
using CivicLens.Collection.Contracts;
using CivicLens.Core.Collection;

namespace CivicLens.Host.Collection;

internal static class CollectionCommandOutput
{
    public static int WriteCompletion(CollectionAttemptCompletion completion)
    {
        var output = Create(completion.Decision, completion.HandoffRemoved);
        Console.WriteLine(JsonSerializer.Serialize(output, CollectionProtocol.JsonOptions));
        if (!completion.HandoffRemoved)
            Console.Error.WriteLine("Import confirmed, but the pending handoff remains. Replaying it is safe.");
        return completion.HandoffRemoved && output.Outcome is CollectionOutcome.Captured or CollectionOutcome.NotModified
            ? 0 : 1;
    }

    public static CollectionImportOutput Create(CollectionImportDecision decision, bool handoffRemoved)
    {
        var outcome = decision.AttemptResult switch
        {
            CapturedAttemptResult => CollectionOutcome.Captured,
            NotModifiedAttemptResult => CollectionOutcome.NotModified,
            FailedAttemptResult => CollectionOutcome.Failed,
            DeferredAttemptResult => CollectionOutcome.Deferred,
            _ => throw new InvalidOperationException("Unknown collection outcome.")
        };
        return new CollectionImportOutput(decision.AttemptResult.AttemptId, outcome,
            JsonNamingPolicy.CamelCase.ConvertName(decision.Disposition.ToString()),
            JsonNamingPolicy.CamelCase.ConvertName(decision.PriorCaptureLinkStatus.ToString()), handoffRemoved);
    }
}
