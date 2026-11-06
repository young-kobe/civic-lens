using CivicLens.Application.Analysis;
using CivicLens.Core.Analysis;
using CivicLens.Core.Review;

namespace CivicLens.Host.Analysis;

public static class AnalysisCommand
{
    private const string Usage = "Use analyses requeue <comparison-id>, analyses requeue --all, or analyses redraft --stale [actions|votes|briefs].";

    public static bool Matches(string[] args) => args is ["analyses", ..];

    public static Task<int> ExecuteAsync(string[] args, RequeueAnalyses<AnalysisRun> requeue,
        RedraftStaleAnalyses redraftStale, ReviewActor owner, CancellationToken cancellationToken) =>
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
                case ["analyses", "redraft", "--stale"]:
                    CommandOutput.Write(new { requeued = await redraftStale.ExecuteAsync(owner, null, cancellationToken) });
                    return 0;
                case ["analyses", "redraft", "--stale", var stage]:
                    CommandOutput.Write(new { requeued = await redraftStale.ExecuteAsync(owner, stage, cancellationToken) });
                    return 0;
                default:
                    Console.Error.WriteLine(Usage);
                    return 2;
            }
        });
}
