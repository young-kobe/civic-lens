using CivicLens.Application.Analysis;

namespace CivicLens.Application.Statements;

public sealed record StatementGateSettings : AnalysisSettings
{
    public StatementGateSettings(long dailyTokenLimit, int concurrency = DefaultConcurrency)
        : base(dailyTokenLimit, concurrency, StatementGatePrompt.MinimumReservation) { }

    public int PromptByteCap => (int)Math.Min(StatementGateTask.MaximumPromptBytes, CallTokenLimit - StatementGateTask.MaximumOutputTokens);
}
