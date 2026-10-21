using CivicLens.Application.Collection;
using CivicLens.Application.Collection.Jobs;

namespace CivicLens.Host.Collection;

internal static class CollectionJobCommand
{
    public static bool Matches(string[] arguments) => arguments is
        ["jobs", "enqueue", _, _, _] or ["jobs", "get", _] or
        ["jobs", "cancel", _] or ["jobs", "list"] or ["jobs", "list", _];

    public static void ValidateArguments(string[] arguments)
    {
        if (arguments[1] == "list")
        {
            _ = ListLimit(arguments);
            return;
        }
        if (!PendingCollectionHandoff.IsValidAttemptId(arguments[2])) throw new ArgumentException("Invalid job ID.");
    }

    public static async Task<int> EnqueueAsync(ICollectionJobStore jobs, ConfiguredCollectionSource source,
        string key, CancellationToken cancellationToken)
    {
        try
        {
            var job = await jobs.RequestCheckAsync(source, key, cancellationToken);
            if (job is null) Console.WriteLine("Check scheduled. No job exists yet. The scheduler admits it at its next pass.");
            else CollectionCommandOutput.Write(job);
            return 0;
        }
        catch (ArgumentException)
        {
            Console.Error.WriteLine("Invalid job admission. Existing keys must match the saved configuration and any explicit date; new keys require active coverage.");
            return 2;
        }
    }

    public static async Task<int> ExecuteAsync(string[] arguments, ICollectionJobStore jobs,
        CancellationToken cancellationToken)
    {
        if (arguments[1] == "list")
        {
            CollectionCommandOutput.Write(await jobs.ListAsync(ListLimit(arguments), cancellationToken));
            return 0;
        }
        var job = arguments[1] == "get" ? await jobs.GetAsync(arguments[2], cancellationToken)
            : await jobs.CancelAsync(arguments[2], cancellationToken);
        if (job is null)
        {
            Console.Error.WriteLine("Job not found.");
            return 2;
        }
        CollectionCommandOutput.Write(job);
        return 0;
    }

    private static int ListLimit(string[] arguments) => arguments.Length == 2 ? 20 :
        int.TryParse(arguments[2], out var limit) && limit is >= 1 and <= 100 ? limit :
        throw new ArgumentException("Job list limit must be 1 to 100.");
}
