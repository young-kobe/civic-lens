namespace CivicLens.Application.Collection.Processing;

/// <summary>Bounds shared by durable evidence processing and its retry contract.</summary>
public static class EvidenceProcessingPolicy
{
    public const int DefaultConcurrency = 2;
    public const int MaximumConcurrency = 4;
    public static readonly TimeSpan MinimumLeaseDuration = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan MaximumLeaseDuration = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan MaximumRetryDelay = TimeSpan.FromSeconds(300);

    public static void ValidateLeaseDuration(TimeSpan leaseDuration)
    {
        if (leaseDuration < MinimumLeaseDuration || leaseDuration > MaximumLeaseDuration)
            throw new ArgumentOutOfRangeException(nameof(leaseDuration), leaseDuration,
                $"Lease duration must be between {MinimumLeaseDuration} and {MaximumLeaseDuration}.");
    }

    public static int ValidateConcurrency(int concurrency) => concurrency is < 1 or > MaximumConcurrency
        ? throw new ArgumentOutOfRangeException(nameof(concurrency), concurrency,
            $"Concurrency must be between 1 and {MaximumConcurrency}.")
        : concurrency;
}
