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

    public static async Task<int> ExecuteAsync(string[] args, RequeueAnalyses<AnalysisRun> requeue,
        GetDocumentChangeReviewOutcomes outcomes, ReviewActor owner,
        CancellationToken cancellationToken)
    {
        try
        {
            switch (args)
            {
                case ["analyses", "requeue", "--all"]:
                    Write(await requeue.ExecuteAsync(owner, null, cancellationToken));
                    return 0;
                case ["analyses", "requeue", var comparisonId]:
                    Write(await requeue.ExecuteAsync(owner, comparisonId, cancellationToken));
                    return 0;
                case ["analyses", "metrics"]:
                    WriteMetrics(await outcomes.ExecuteAsync(owner, cancellationToken));
                    return 0;
                default:
                    Console.Error.WriteLine(Usage);
                    return 2;
            }
        }
        catch (ArgumentException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 2;
        }
        catch (UnauthorizedAccessException)
        {
            Console.Error.WriteLine("The configured actor is not the owner.");
            return 2;
        }
    }

    private static void Write(int requeued) => Console.WriteLine(JsonSerializer.Serialize(new { requeued }));

    private static void WriteMetrics(ImmutableArray<DocumentChangeReviewOutcomes> outcomes) =>
        Console.WriteLine(JsonSerializer.Serialize(new
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
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
}
