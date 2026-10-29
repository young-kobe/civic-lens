using CivicLens.Application;
using CivicLens.Application.Analysis;
using CivicLens.Application.Collection;
using CivicLens.Application.Collection.Jobs;
using CivicLens.Application.Collection.Processing;
using CivicLens.Infrastructure.Collection;
using Microsoft.Extensions.Logging;

namespace CivicLens.Host.Collection;

internal static class CollectionWorkerCommand
{
    public static bool Matches(string[] arguments) => arguments is
        ["worker", _, _] or ["worker", _, _, "--once"];

    public static bool IsOnce(string[] arguments) => arguments is ["worker", _, _, "--once"];

    public static void ValidateArguments(string[] arguments)
    {
        if (!Matches(arguments)) throw new ArgumentException("Use worker <collector.dll> <artifact-directory> [--once].");
        if (!Path.GetFileName(arguments[1]).EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Worker collector path must name a .dll.");
        _ = Path.GetFullPath(arguments[1]);
        _ = Path.GetFullPath(arguments[2]);
    }

    public static string ArtifactRootOf(string[] arguments) => Path.GetFullPath(arguments[2]);

    public static async Task<int> ExecuteAsync(string[] arguments, ICollectionWorkerQueue queue,
        ICollectionJobStore jobs, ICollectionAttemptStore evidence, ILoggerFactory loggers, WorkerBeat beat,
        CancellationToken cancellationToken,
        EvidenceProcessingWorker? evidenceProcessor = null, IWorkerWakeup? wakeup = null,
        ICollectionScheduleStore? schedules = null, IReadOnlyList<AnalysisStage>? analysis = null)
    {
        ValidateArguments(arguments);
        var collectorPath = Path.GetFullPath(arguments[1]);
        var artifactRoot = ArtifactRootOf(arguments);
        var runner = new RunCollectionJob(jobs, evidence, new FileCollectionReceiptHandoffStore(artifactRoot),
            new CaptureArtifactVerifier(), new CollectorProcess(collectorPath,
                Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet"),
            loggers.CreateLogger<RunCollectionJob>());
        var once = IsOnce(arguments);
        var collection = new CollectionJobWorker(queue, runner, loggers.CreateLogger<CollectionJobWorker>(), beat, evidenceProcessor, wakeup, schedules);
        var (result, analysisResults) = await RunAsync(collection, analysis ?? [], artifactRoot, once, cancellationToken);
        CollectionCommandOutput.Write(result);
        foreach (var (name, analysisResult) in analysisResults)
            CollectionCommandOutput.Write(new Dictionary<string, AnalysisWorkerResult> { [name] = analysisResult });
        var analysisFailed = analysisResults.Any(item => item.Result.Failures > 0);
        return result.JobFailures == 0 && result.QueueFailures == 0 && !analysisFailed ? 0 : 1;
    }

    private static async Task<(CollectionJobWorkerResult Collection, IReadOnlyList<(string Name, AnalysisWorkerResult Result)> Analysis)> RunAsync(
        CollectionJobWorker collection, IReadOnlyList<AnalysisStage> stages, string artifactRoot, bool once, CancellationToken cancellationToken)
    {
        if (once || stages.Count == 0)
        {
            var collected = await collection.ExecuteAsync(artifactRoot, once, cancellationToken: cancellationToken);
            var results = new List<(string, AnalysisWorkerResult)>();
            foreach (var stage in stages) results.Add((stage.Name, await stage.RunAsync(true, cancellationToken)));
            return (collected, results);
        }
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var collectionTask = StopOnFailureAsync(collection.ExecuteAsync(artifactRoot, false, cancellationToken: stop.Token), stop);
        var stageTasks = stages.Select(stage => StopOnFailureAsync(stage.RunAsync(false, stop.Token), stop)).ToArray();
        List<Task> running = [collectionTask, .. stageTasks];
        await Task.WhenAll(running);
        return (await collectionTask, [.. stages.Select((stage, index) => (stage.Name, stageTasks[index].Result))]);
    }

    private static async Task<T> StopOnFailureAsync<T>(Task<T> task, CancellationTokenSource stop)
    {
        try { return await task; }
        catch
        {
            await stop.CancelAsync();
            throw;
        }
    }
}
