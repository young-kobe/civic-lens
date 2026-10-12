using CivicLens.Application.Analysis;

namespace CivicLens.Application.Statements;

public sealed record StatementDraftSettings : AnalysisSettings
{
    public StatementDraftSettings(long dailyTokenLimit, long? runTokenLimit = null, int concurrency = DefaultConcurrency)
        : base(dailyTokenLimit, runTokenLimit, concurrency, StatementDraftPrompt.MinimumReservation) { }

    public int PromptByteCap => (int)Math.Min(StatementDraftTask.MaximumPromptBytes, RunTokenLimit - StatementDraftTask.MaximumOutputTokens);
}
