namespace CivicLens.Application.Analysis;

public static class DocumentChangeAnalysisPolicy
{
    public const int MaximumAttempts = 8;
    public const int MaximumBatch = 100;
    public const int OutageFailureBound = 3;
    public static readonly TimeSpan InitialOutagePause = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan MaximumOutagePause = TimeSpan.FromHours(1);
    public static readonly TimeSpan LeaseDuration = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan MinimumLeaseDuration = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan MaximumLeaseDuration = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan InitialRetryDelay = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan MaximumRetryDelay = TimeSpan.FromMinutes(30);

    public static void ValidateLeaseDuration(TimeSpan leaseDuration)
    {
        if (leaseDuration < MinimumLeaseDuration || leaseDuration > MaximumLeaseDuration)
            throw new ArgumentOutOfRangeException(nameof(leaseDuration), leaseDuration,
                $"Lease duration must be between {MinimumLeaseDuration} and {MaximumLeaseDuration}.");
    }

    public static TimeSpan RetryDelay(int previousFailures) => Backoff(InitialRetryDelay, MaximumRetryDelay, previousFailures);

    public static TimeSpan OutagePause(int previousPauses) => Backoff(InitialOutagePause, MaximumOutagePause, previousPauses);

    private static TimeSpan Backoff(TimeSpan initial, TimeSpan maximum, int steps) =>
        TimeSpan.FromTicks(Math.Min(maximum.Ticks, initial.Ticks << Math.Clamp(steps, 0, 16)));
}
