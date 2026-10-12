using CivicLens.Application;
using System.Text.Json;
using System.Text.Json.Serialization;
using CivicLens.Application.Analysis;
using CivicLens.Application.Collection;
using CivicLens.Application.Collection.Jobs;
using CivicLens.Application.Collection.Processing;
using CivicLens.Application.Statements;
using CivicLens.Collection.Contracts;
using CivicLens.Core.Analysis;
using CivicLens.Infrastructure.Collection;

namespace CivicLens.Host.Collection;

internal static class CollectionWorkerCommand
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    public static bool Matches(string[] arguments) => arguments is
        ["worker", _, _] or ["worker", _, _, "--once"];

    public static void ValidateArguments(string[] arguments)
    {
        if (!Matches(arguments)) throw new ArgumentException("Use worker <collector.dll> <artifact-directory> [--once].");
        if (!Path.GetFileName(arguments[1]).EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Worker collector path must name a .dll.");
        _ = Path.GetFullPath(arguments[1]);
        _ = Path.GetFullPath(arguments[2]);
    }

    public static async Task<int> ExecuteAsync(string[] arguments, ICollectionWorkerQueue queue,
        ICollectionJobStore jobs, ICollectionAttemptStore evidence, CancellationToken cancellationToken,
        EvidenceProcessingWorker? evidenceProcessor = null, IWorkerWakeup? wakeup = null,
        ICollectionScheduleStore? schedules = null, AnalysisWorker<AnalysisRun>? drafting = null,
        AnalysisWorker<StatementGateRun>? gate = null)
    {
        ValidateArguments(arguments);
        var collectorPath = Path.GetFullPath(arguments[1]);
        var artifactRoot = Path.GetFullPath(arguments[2]);
        var runner = new RunCollectionJob(jobs, evidence, new FileCollectionReceiptHandoffStore(artifactRoot),
            new CaptureArtifactVerifier(), new CollectorProcess(collectorPath,
                Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet"));
        var once = arguments.Length == 4;
        var collection = new CollectionJobWorker(queue, runner, evidenceProcessor, wakeup, schedules);
        var (result, draftingResult, gateResult) = await RunAsync(collection, drafting, gate, artifactRoot, once, cancellationToken);
        Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
        if (draftingResult is not null) Console.WriteLine(JsonSerializer.Serialize(new { drafting = draftingResult }, JsonOptions));
        if (gateResult is not null) Console.WriteLine(JsonSerializer.Serialize(new { gate = gateResult }, JsonOptions));
        var analysisFailed = draftingResult is { Failures: > 0 } || gateResult is { Failures: > 0 };
        return result.JobFailures == 0 && result.QueueFailures == 0 && !analysisFailed ? 0 : 1;
    }

    private static async Task<(CollectionJobWorkerResult Collection, AnalysisWorkerResult? Drafting,
        AnalysisWorkerResult? Gate)> RunAsync(CollectionJobWorker collection, AnalysisWorker<AnalysisRun>? drafting,
        AnalysisWorker<StatementGateRun>? gate, string artifactRoot, bool once, CancellationToken cancellationToken)
    {
        if (once || drafting is null && gate is null)
        {
            var collected = await collection.ExecuteAsync(artifactRoot, once, cancellationToken: cancellationToken);
            var drafted = drafting is null ? null : await drafting.ExecuteAsync(once: true, cancellationToken);
            return (collected, drafted, gate is null ? null : await gate.ExecuteAsync(once: true, cancellationToken));
        }
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var collectionTask = StopOnFailureAsync(collection.ExecuteAsync(artifactRoot, false, cancellationToken: stop.Token), stop);
        var draftingTask = StopOnFailureAsync(drafting is null ? Task.FromResult<AnalysisWorkerResult?>(null)
            : Widen(drafting.ExecuteAsync(once: false, stop.Token)), stop);
        var gateTask = StopOnFailureAsync(gate is null ? Task.FromResult<AnalysisWorkerResult?>(null)
            : Widen(gate.ExecuteAsync(once: false, stop.Token)), stop);
        await Task.WhenAll(collectionTask, draftingTask, gateTask);
        return (await collectionTask, await draftingTask, await gateTask);
    }

    private static async Task<AnalysisWorkerResult?> Widen(Task<AnalysisWorkerResult> task) => await task;

    private static async Task<T> StopOnFailureAsync<T>(Task<T> task, CancellationTokenSource stop)
    {
        try { return await task; }
        catch
        {
            await stop.CancelAsync();
            throw;
        }
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(CollectionProtocol.JsonOptions);
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        return options;
    }
}
