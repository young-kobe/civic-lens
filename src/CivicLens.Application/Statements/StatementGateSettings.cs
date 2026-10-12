using CivicLens.Application.Analysis;

namespace CivicLens.Application.Statements;

public sealed record StatementGateSettings
{
    public const long DefaultRunTokenLimit = 120_000;

    public StatementGateSettings(long dailyTokenLimit, long? runTokenLimit = null, int concurrency = DocumentChangeAnalysisSettings.DefaultConcurrency)
    {
        if (dailyTokenLimit < 1) throw new ArgumentOutOfRangeException(nameof(dailyTokenLimit));
        runTokenLimit ??= Math.Min(DefaultRunTokenLimit, dailyTokenLimit);
        if (runTokenLimit < StatementGatePrompt.MinimumReservation || runTokenLimit > dailyTokenLimit)
            throw new ArgumentOutOfRangeException(nameof(runTokenLimit), runTokenLimit,
                $"The run token limit must be from {StatementGatePrompt.MinimumReservation}, the smallest reservation, to the daily limit.");
        if (concurrency is < 1 or > DocumentChangeAnalysisSettings.MaximumConcurrency) throw new ArgumentOutOfRangeException(nameof(concurrency));
        DailyTokenLimit = dailyTokenLimit;
        RunTokenLimit = runTokenLimit.Value;
        Concurrency = concurrency;
    }

    public long DailyTokenLimit { get; }
    public long RunTokenLimit { get; }
    public int Concurrency { get; }

    public int PromptByteCap => (int)Math.Min(StatementGateTask.MaximumPromptBytes, RunTokenLimit - StatementGateTask.MaximumOutputTokens);
}
