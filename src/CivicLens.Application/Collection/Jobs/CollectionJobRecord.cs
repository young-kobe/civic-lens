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
    int ReservedRequests,
    long ReservedBytes,
    int ReservedTimeoutSeconds,
    IReadOnlyList<CollectionJobAttempt> Attempts);
