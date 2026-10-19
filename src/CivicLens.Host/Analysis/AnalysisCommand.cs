using CivicLens.Application.Analysis;
using CivicLens.Core.Analysis;
using CivicLens.Core.Review;

namespace CivicLens.Host.Analysis;

public static class AnalysisCommand
{
    private const string Usage = "Use analyses requeue <comparison-id> or analyses requeue --all.";

    public static bool Matches(string[] args) => args is ["analyses", ..];

    public static Task<int> ExecuteAsync(string[] args, RequeueAnalyses<AnalysisRun> requeue,
        ReviewActor owner, CancellationToken cancellationToken) =>
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
                default:
                    Console.Error.WriteLine(Usage);
                    return 2;
            }
        });
}
