using System.Text.Json;
using CivicLens.Application.Analysis;
using CivicLens.Core.Review;

namespace CivicLens.Host.Analysis;

public static class AnalysisCommand
{
    private const string Usage = "Use analyses requeue <comparison-id> or analyses requeue --all.";

    public static bool Matches(string[] args) => args is ["analyses", ..];

    public static async Task<int> ExecuteAsync(string[] args, RequeueDocumentChangeAnalyses requeue, ReviewActor owner,
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
}
