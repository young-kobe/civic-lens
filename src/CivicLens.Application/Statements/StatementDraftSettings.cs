using CivicLens.Application.Analysis;

namespace CivicLens.Application.Statements;

public sealed record StatementDraftSettings : AnalysisSettings
{
    public StatementDraftSettings(long dailyTokenLimit, int concurrency = DefaultConcurrency)
        : base(dailyTokenLimit, concurrency, StatementDraftPrompt.MinimumReservation) { }

    public int PromptByteCap => (int)Math.Min(StatementDraftTask.MaximumPromptBytes, CallTokenLimit - StatementDraftTask.MaximumOutputTokens);
}
