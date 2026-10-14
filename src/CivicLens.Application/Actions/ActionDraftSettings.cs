using CivicLens.Application.Analysis;

namespace CivicLens.Application.Actions;

public sealed record ActionDraftSettings : AnalysisSettings
{
    public ActionDraftSettings(long dailyTokenLimit, long? runTokenLimit = null, int concurrency = DefaultConcurrency)
        : base(dailyTokenLimit, runTokenLimit, concurrency, ActionDraftPrompt.MinimumReservation) { }

    public int PromptByteCap => (int)Math.Min(ActionDraftTask.MaximumPromptBytes, RunTokenLimit - ActionDraftTask.MaximumOutputTokens);
}
