namespace CivicLens.Application.Analysis;

public sealed record DocumentChangeAnalysisSettings
{
    public const long DefaultRunTokenLimit = 120_000;
    public const int DefaultConcurrency = 2;
    public const int MaximumConcurrency = 4;

    public DocumentChangeAnalysisSettings(long dailyTokenLimit, long runTokenLimit = DefaultRunTokenLimit, int concurrency = DefaultConcurrency)
    {
        if (dailyTokenLimit < 1) throw new ArgumentOutOfRangeException(nameof(dailyTokenLimit));
        if (runTokenLimit < DocumentChangeDraftingPrompt.MinimumReservation || runTokenLimit > dailyTokenLimit)
            throw new ArgumentOutOfRangeException(nameof(runTokenLimit), runTokenLimit,
                $"The run token limit must be from {DocumentChangeDraftingPrompt.MinimumReservation}, the smallest reservation, to the daily limit.");
        if (concurrency is < 1 or > MaximumConcurrency) throw new ArgumentOutOfRangeException(nameof(concurrency));
        DailyTokenLimit = dailyTokenLimit;
        RunTokenLimit = runTokenLimit;
        Concurrency = concurrency;
    }

    public long DailyTokenLimit { get; }
    public long RunTokenLimit { get; }
    public int Concurrency { get; }
}
