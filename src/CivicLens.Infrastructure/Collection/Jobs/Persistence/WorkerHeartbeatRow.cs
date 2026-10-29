namespace CivicLens.Infrastructure.Collection.Jobs.Persistence;

internal sealed class WorkerHeartbeatRow
{
    public string Loop { get; set; } = string.Empty;
    public long BeatAtUtcTicks { get; set; }
}
