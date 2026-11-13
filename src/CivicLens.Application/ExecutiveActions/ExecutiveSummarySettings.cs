using CivicLens.Application.Analysis;

namespace CivicLens.Application.ExecutiveActions;

public sealed record ExecutiveSummarySettings(long DailyTokenLimit, int Concurrency = AnalysisSettings.DefaultConcurrency)
    : AnalysisSettings(DailyTokenLimit, Concurrency, ExecutiveSummaryPrompt.MinimumReservation)
{
    public int PromptByteCap => (int)Math.Min(ExecutiveSummaryTask.MaximumPromptBytes, CallTokenLimit - ExecutiveSummaryTask.MaximumOutputTokens);
}
