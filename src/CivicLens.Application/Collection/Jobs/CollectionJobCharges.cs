namespace CivicLens.Application.Collection.Jobs;

public sealed record CollectionJobCharges(int Requests, long Bytes, int TimeoutSeconds)
{
    public static CollectionJobCharges Of(IEnumerable<CollectionJobAttempt> attempts)
    {
        ArgumentNullException.ThrowIfNull(attempts);
        var requests = 0;
        var bytes = 0L;
        var timeoutSeconds = 0;
        foreach (var attempt in attempts)
        {
            var receipt = attempt.Resolution?.Receipt;
            requests += receipt?.RequestCount ?? attempt.Request.MaxRequests;
            bytes += receipt?.BytesReceived ?? attempt.Request.MaxBytes;
            timeoutSeconds += attempt.Request.TimeoutSeconds;
        }
        return new(requests, bytes, timeoutSeconds);
    }
}
