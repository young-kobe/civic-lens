using CivicLens.Collection.Contracts;

namespace CivicLens.Application.Collection.Jobs;

public sealed record CollectionJobPolicy
{
    public int MaxAttempts { get; init; } = 3;
    public int InitialRetryDelaySeconds { get; init; } = 30;
    public int MaximumRetryDelaySeconds { get; init; } = 3600;
    public int? MaxTotalRequests { get; init; }
    public long? MaxTotalBytes { get; init; }
    public int? MaxTotalTimeoutSeconds { get; init; }

    public void Validate(CollectionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();
        if (MaxAttempts is < 1 or > 100 || InitialRetryDelaySeconds < 0 ||
            MaximumRetryDelaySeconds < InitialRetryDelaySeconds)
            throw new ArgumentException("Job retry policy is outside supported bounds.");
        var maxRequests = MaxTotalRequests ?? checked(request.MaxRequests * MaxAttempts);
        var maxBytes = MaxTotalBytes ?? checked(request.MaxBytes * MaxAttempts);
        var maxSeconds = MaxTotalTimeoutSeconds ?? checked(request.TimeoutSeconds * MaxAttempts);
        if (maxRequests < request.MaxRequests || maxRequests > 20_000 ||
            maxBytes < request.MaxBytes || maxBytes > 10_000_000_000 ||
            maxSeconds < request.TimeoutSeconds || maxSeconds > 30_000)
            throw new ArgumentException("Aggregate job limits must include one attempt and stay within supported bounds.");
    }

    public int ResolveMaxTotalRequests(CollectionRequest request) => MaxTotalRequests ?? checked(request.MaxRequests * MaxAttempts);
    public long ResolveMaxTotalBytes(CollectionRequest request) => MaxTotalBytes ?? checked(request.MaxBytes * MaxAttempts);
    public int ResolveMaxTotalTimeoutSeconds(CollectionRequest request) => MaxTotalTimeoutSeconds ?? checked(request.TimeoutSeconds * MaxAttempts);
}
