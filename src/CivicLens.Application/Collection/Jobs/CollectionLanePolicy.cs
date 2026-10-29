namespace CivicLens.Application.Collection.Jobs;

/// <summary>Bounds for the number of collection jobs that one worker runs at the same time.</summary>
public static class CollectionLanePolicy
{
    public const int DefaultConcurrency = 1;
    public const int MaximumConcurrency = 4;

    public static int ValidateConcurrency(int concurrency) => concurrency is < 1 or > MaximumConcurrency
        ? throw new ArgumentOutOfRangeException(nameof(concurrency), concurrency,
            $"Concurrency must be between 1 and {MaximumConcurrency}.")
        : concurrency;
}
