using CivicLens.Application.Collection.Jobs;

namespace CivicLens.Application.Collection.Discovery;

public sealed record FeedAdmissionPolicy
{
    public int MaxJobs { get; init; } = 10;
    public int MaxTotalRequests { get; init; } = 150;
    public long MaxTotalBytes { get; init; } = 60_000_000;
    public int MaxTotalTimeoutSeconds { get; init; } = 900;

    public void Validate()
    {
        if (MaxJobs is < 1 or > 1000 || MaxTotalRequests is < 1 or > 20_000 ||
            MaxTotalBytes is < 1 or > 10_000_000_000 || MaxTotalTimeoutSeconds is < 1 or > 30_000)
            throw new ArgumentException("Feed admission limits are outside supported bounds.");
    }

    public bool TryReserve(CollectionJobDefinition template, int admittedJobs, int usedRequests, long usedBytes,
        int usedSeconds, out FeedAdmissionReservation reservation)
    {
        ArgumentNullException.ThrowIfNull(template);
        Validate();
        if (admittedJobs < 0 || usedRequests < 0 || usedBytes < 0 || usedSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(admittedJobs), "Previously reserved feed budgets cannot be negative.");
        template.Validate();
        var request = template.CreateRequest("feed-admission-validation", Path.GetFullPath("collection-artifacts"));
        var requests = template.Policy.ResolveMaxTotalRequests(request);
        var bytes = template.Policy.ResolveMaxTotalBytes(request);
        var seconds = template.Policy.ResolveMaxTotalTimeoutSeconds(request);
        if (admittedJobs >= MaxJobs || requests > MaxTotalRequests - usedRequests ||
            bytes > MaxTotalBytes - usedBytes || seconds > MaxTotalTimeoutSeconds - usedSeconds)
        {
            reservation = default;
            return false;
        }

        reservation = new FeedAdmissionReservation(requests, bytes, seconds);
        return true;
    }
}
