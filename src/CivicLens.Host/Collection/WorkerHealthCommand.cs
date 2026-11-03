using CivicLens.Application;
using CivicLens.Application.Publication;

namespace CivicLens.Host.Collection;

public static class WorkerHealthCommand
{
    public const int UnhealthyExitCode = 1;
    public const int ReleaseFullExitCode = 3;

    private static readonly TimeSpan MaximumAge = TimeSpan.FromMinutes(10);

    public static bool Matches(string[] arguments) => arguments is ["worker", "health"];

    public static async Task<int> ExecuteAsync(IWorkerHeartbeatStore heartbeats, ReadReleaseCapacity? capacity, TextWriter output,
        CancellationToken cancellationToken)
    {
        var exitCode = await CheckHeartbeatsAsync(heartbeats, output, cancellationToken);
        if (capacity is null) return exitCode;
        var release = await capacity.ExecuteAsync(cancellationToken);
        if (release.WaitingForRoom == 0) return exitCode;
        output.WriteLine($"Release full: The release holds {release.Held} records. {release.WaitingForRoom} new records wait for room.");
        return exitCode == 0 ? ReleaseFullExitCode : exitCode;
    }

    private static async Task<int> CheckHeartbeatsAsync(IWorkerHeartbeatStore heartbeats, TextWriter output, CancellationToken cancellationToken)
    {
        var beats = await heartbeats.ListAsync(cancellationToken);
        if (beats.Count == 0)
        {
            output.WriteLine("Unhealthy: the worker has written no heartbeat.");
            return UnhealthyExitCode;
        }
        var stale = beats.Where(beat => beat.Age > MaximumAge).ToList();
        if (stale.Count > 0)
        {
            output.WriteLine("Unhealthy: " + string.Join(", ", stale.Select(Describe)) + $" (limit {MaximumAge.TotalMinutes:0} minutes).");
            return UnhealthyExitCode;
        }
        output.WriteLine($"Healthy: the oldest heartbeat is {Describe(beats.MaxBy(beat => beat.Age)!)}.");
        return 0;
    }

    private static string Describe(WorkerHeartbeat beat) => $"loop {beat.Loop} last beat {(int)beat.Age.TotalSeconds} seconds ago";
}
