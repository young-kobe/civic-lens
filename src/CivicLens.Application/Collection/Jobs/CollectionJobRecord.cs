namespace CivicLens.Application.Collection.Jobs;

public enum CollectionJobState { Pending, Running, WaitingToRetry, Succeeded, Failed, Cancelled }

public sealed record CollectionJobRecord(
    string JobId,
    CollectionJobDefinition Definition,
    string IdempotencyKey,
    CollectionJobState State,
    DateTimeOffset CreatedAt,
    DateTimeOffset? RetryAt,
    bool CancellationRequested,
    IReadOnlyList<CollectionJobAttempt> Attempts)
{
    public int ReservedRequests => CollectionJobCharges.Of(Attempts).Requests;
    public long ReservedBytes => CollectionJobCharges.Of(Attempts).Bytes;
    public int ReservedTimeoutSeconds => CollectionJobCharges.Of(Attempts).TimeoutSeconds;
}
