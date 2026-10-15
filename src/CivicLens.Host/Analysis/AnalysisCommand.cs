using System.Collections.Immutable;
using System.Text.Json;
using CivicLens.Application.Analysis;
using CivicLens.Core.Analysis;
using CivicLens.Core.Review;

namespace CivicLens.Host.Analysis;

public static class AnalysisCommand
{
    private const string Usage = "Use analyses requeue <comparison-id>, analyses requeue --all, or analyses metrics.";

    public static bool Matches(string[] args) => args is ["analyses", ..];

    public static Task<int> ExecuteAsync(string[] args, RequeueAnalyses<AnalysisRun> requeue,
        GetDocumentChangeReviewOutcomes outcomes, ReviewActor owner,
        CancellationToken cancellationToken) =>
        OwnerCommand.RunAsync(async () =>
        {
            switch (args)
            {
                case ["analyses", "requeue", "--all"]:
                    CommandOutput.Write(new { requeued = await requeue.ExecuteAsync(owner, null, cancellationToken) });
                    return 0;
                case ["analyses", "requeue", var comparisonId]:
                    CommandOutput.Write(new { requeued = await requeue.ExecuteAsync(owner, comparisonId, cancellationToken) });
                    return 0;
                case ["analyses", "metrics"]:
                    WriteMetrics(await outcomes.ExecuteAsync(owner, cancellationToken));
                    return 0;
                default:
                    Console.Error.WriteLine(Usage);
                    return 2;
            }
        });

    private static void WriteMetrics(ImmutableArray<DocumentChangeReviewOutcomes> outcomes) =>
        CommandOutput.Write(new
        {
            promptVersions = outcomes.Select(item => new
            {
                item.PromptVersion,
                item.Drafts,
                item.ApprovedUnchanged,
                item.ApprovedEdited,
                item.ChangesRequested,
                item.ApprovalWithdrawn,
                item.AwaitingReview,
                fieldEdits = item.FieldEdits.ToDictionary(pair => JsonNamingPolicy.CamelCase.ConvertName(pair.Key.ToString()), pair => pair.Value)
            })
        });
}
