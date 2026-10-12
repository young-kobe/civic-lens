namespace CivicLens.Application.Analysis;

public abstract record AnalysisSettings
{
    public const long DefaultRunTokenLimit = 120_000;
    public const int DefaultConcurrency = 2;
    public const int MaximumConcurrency = 4;

    protected AnalysisSettings(long dailyTokenLimit, long? runTokenLimit, int concurrency, long minimumReservation)
    {
        if (dailyTokenLimit < 1) throw new ArgumentOutOfRangeException(nameof(dailyTokenLimit));
        runTokenLimit ??= Math.Min(DefaultRunTokenLimit, dailyTokenLimit);
        if (runTokenLimit < minimumReservation || runTokenLimit > dailyTokenLimit)
            throw new ArgumentOutOfRangeException(nameof(runTokenLimit), runTokenLimit,
                $"The run token limit must be from {minimumReservation}, the smallest reservation, to the daily limit.");
        if (concurrency is < 1 or > MaximumConcurrency) throw new ArgumentOutOfRangeException(nameof(concurrency));
        DailyTokenLimit = dailyTokenLimit;
        RunTokenLimit = runTokenLimit.Value;
        Concurrency = concurrency;
        MinimumReservation = minimumReservation;
    }

    public long DailyTokenLimit { get; }
    public long RunTokenLimit { get; }
    public int Concurrency { get; }
    public long MinimumReservation { get; }
}
