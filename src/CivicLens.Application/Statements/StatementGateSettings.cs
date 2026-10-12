using CivicLens.Application.Analysis;

namespace CivicLens.Application.Statements;

public sealed record StatementGateSettings : AnalysisSettings
{
    public StatementGateSettings(long dailyTokenLimit, long? runTokenLimit = null, int concurrency = DefaultConcurrency)
        : base(dailyTokenLimit, runTokenLimit, concurrency, StatementGatePrompt.MinimumReservation) { }

    public int PromptByteCap => (int)Math.Min(StatementGateTask.MaximumPromptBytes, RunTokenLimit - StatementGateTask.MaximumOutputTokens);
}
