using CivicLens.Collection.Contracts;

namespace CivicLens.Application.Collection.Jobs;

public sealed record CollectionJobCharges(int Requests, long Bytes, int TimeoutSeconds)
{
    public static AttemptTransfer? Chargeable(CollectionRequest reserved, CollectionRequest imported, AttemptTransfer? transfer) =>
        imported == reserved ? transfer : null;

    public static CollectionJobCharges Of(IEnumerable<CollectionJobAttempt> attempts)
    {
        ArgumentNullException.ThrowIfNull(attempts);
        var requests = 0;
        var bytes = 0L;
        var timeoutSeconds = 0;
        foreach (var attempt in attempts)
        {
            requests += attempt.Transfer?.RequestCount ?? attempt.Request.MaxRequests;
            bytes += attempt.Transfer?.BytesReceived ?? attempt.Request.MaxBytes;
            timeoutSeconds += attempt.Request.TimeoutSeconds;
        }
        return new(requests, bytes, timeoutSeconds);
    }
}
