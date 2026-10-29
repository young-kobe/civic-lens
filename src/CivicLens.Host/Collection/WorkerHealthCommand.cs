using CivicLens.Application;

namespace CivicLens.Host.Collection;

internal static class WorkerHealthCommand
{
    public static readonly TimeSpan MaximumAge = TimeSpan.FromMinutes(3);

    public static bool Matches(string[] arguments) => arguments is ["worker", "health"];

    public static async Task<int> ExecuteAsync(IWorkerHeartbeatStore heartbeats, TextWriter output, CancellationToken cancellationToken)
    {
        var beats = await heartbeats.ListAsync(cancellationToken);
        if (beats.Count == 0)
        {
            output.WriteLine("Unhealthy: the worker has written no heartbeat.");
            return 1;
        }
        var stale = beats.Where(beat => beat.Age > MaximumAge).ToList();
        if (stale.Count > 0)
        {
            output.WriteLine("Unhealthy: " + string.Join(", ", stale.Select(Describe)) + $" (limit {MaximumAge.TotalMinutes:0} minutes).");
            return 1;
        }
        output.WriteLine($"Healthy: the oldest heartbeat is {Describe(beats.MaxBy(beat => beat.Age)!)}.");
        return 0;
    }

    private static string Describe(WorkerHeartbeat beat) => $"loop {beat.Loop} last beat {(int)beat.Age.TotalSeconds} seconds ago";
}
