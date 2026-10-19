namespace CivicLens.Application.Analysis;

public abstract record AnalysisSettings
{
    public const long DefaultCallTokenLimit = 120_000;
    public const int DefaultConcurrency = 2;
    public const int MaximumConcurrency = 4;

    protected AnalysisSettings(long dailyTokenLimit, int concurrency, long minimumReservation)
    {
        if (dailyTokenLimit < 1) throw new ArgumentOutOfRangeException(nameof(dailyTokenLimit));
        var callTokenLimit = Math.Min(DefaultCallTokenLimit, dailyTokenLimit);
        if (callTokenLimit < minimumReservation)
            throw new ArgumentOutOfRangeException(nameof(dailyTokenLimit), dailyTokenLimit,
                $"The daily token limit must be at least {minimumReservation}, the smallest reservation.");
        if (concurrency is < 1 or > MaximumConcurrency) throw new ArgumentOutOfRangeException(nameof(concurrency));
        DailyTokenLimit = dailyTokenLimit;
        CallTokenLimit = callTokenLimit;
        Concurrency = concurrency;
        MinimumReservation = minimumReservation;
    }

    public long DailyTokenLimit { get; }
    public long CallTokenLimit { get; }
    public int Concurrency { get; }
    public long MinimumReservation { get; }
}
