using System.Text.Json;
using System.Text.Json.Serialization;
using CivicLens.Application.Collection;
using CivicLens.Application.Collection.Jobs;
using CivicLens.Application.Collection.Processing;
using CivicLens.Collection.Contracts;
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
        EvidenceProcessingWorker? evidenceProcessor = null, ICollectionPipelineWakeup? wakeup = null)
    {
        ValidateArguments(arguments);
        var collectorPath = Path.GetFullPath(arguments[1]);
        var artifactRoot = Path.GetFullPath(arguments[2]);
        var runner = new RunCollectionJob(jobs, evidence, new FileCollectionReceiptHandoffStore(artifactRoot),
            new CaptureArtifactVerifier(), new CollectorProcess(collectorPath,
                Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet"));
        var once = arguments.Length == 4;
        var result = await new CollectionJobWorker(queue, runner, evidenceProcessor, wakeup).ExecuteAsync(artifactRoot, once,
            cancellationToken: cancellationToken);
        Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
        return result.JobFailures == 0 && result.QueueFailures == 0 ? 0 : 1;
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(CollectionProtocol.JsonOptions);
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        return options;
    }
}
