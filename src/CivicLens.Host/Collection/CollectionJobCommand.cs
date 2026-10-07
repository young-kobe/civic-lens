using System.Text.Json;
using System.Text.Json.Serialization;
using CivicLens.Application.Collection;
using CivicLens.Application.Collection.Jobs;
using CivicLens.Collection.Contracts;
using CivicLens.Infrastructure.Collection;

namespace CivicLens.Host.Collection;

internal static class CollectionJobCommand
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    public static bool Matches(string[] arguments) => arguments is
        ["jobs", "enqueue", _, _, _] or ["jobs", "run", _, _, _] or ["jobs", "get", _] or
        ["jobs", "cancel", _] or ["jobs", "list"] or ["jobs", "list", _];

    public static void ValidateArguments(string[] arguments)
    {
        if (arguments[1] == "list")
        {
            _ = ListLimit(arguments);
            return;
        }
        if (!PendingCollectionHandoff.IsValidAttemptId(arguments[2])) throw new ArgumentException("Invalid job ID.");
        if (arguments[1] == "run") _ = Path.GetFullPath(arguments[4]);
    }

    public static async Task<int> EnqueueAsync(ICollectionJobStore jobs, ConfiguredCollectionSource source,
        string key, CancellationToken cancellationToken)
    {
        try
        {
            Write(await jobs.EnqueueAsync(source, key, cancellationToken));
            return 0;
        }
        catch (ArgumentException)
        {
            Console.Error.WriteLine("Invalid job admission. Existing keys must match the saved configuration and any explicit date; new keys require active coverage.");
            return 2;
        }
    }

    public static async Task<int> ExecuteAsync(string[] arguments, ICollectionJobStore jobs,
        ICollectionAttemptStore evidence, CancellationToken cancellationToken)
    {
        if (arguments[1] == "list")
        {
            Write(await jobs.ListAsync(ListLimit(arguments), cancellationToken));
            return 0;
        }
        if (arguments[1] is "get" or "cancel")
        {
            var job = arguments[1] == "get" ? await jobs.GetAsync(arguments[2], cancellationToken)
                : await jobs.CancelAsync(arguments[2], cancellationToken);
            if (job is null)
            {
                Console.Error.WriteLine("Job not found.");
                return 2;
            }
            Write(job);
            return 0;
        }
        var root = Path.GetFullPath(arguments[4]);
        var runner = new RunCollectionJob(jobs, evidence, new FileCollectionReceiptHandoffStore(root),
            new CaptureArtifactVerifier(), new CollectorProcess(arguments[3],
                Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet"));
        var result = await runner.ExecuteAsync(arguments[2], root, TimeSpan.FromSeconds(60), cancellationToken);
        Write(result);
        return result.Job?.State == CollectionJobState.Succeeded ? 0 : 1;
    }

    private static int ListLimit(string[] arguments) => arguments.Length == 2 ? 20 :
        int.TryParse(arguments[2], out var limit) && limit is >= 1 and <= 100 ? limit :
        throw new ArgumentException("Job list limit must be 1 to 100.");

    private static void Write<T>(T value) => Console.WriteLine(JsonSerializer.Serialize(value, JsonOptions));

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(CollectionProtocol.JsonOptions);
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        return options;
    }
}
