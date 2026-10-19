using CivicLens.Application.Analysis;

namespace CivicLens.Application.Actions;

public sealed record ActionDraftSettings : AnalysisSettings
{
    public ActionDraftSettings(long dailyTokenLimit, int concurrency = DefaultConcurrency)
        : base(dailyTokenLimit, concurrency, ActionDraftPrompt.MinimumReservation) { }

    public int PromptByteCap => (int)Math.Min(ActionDraftTask.MaximumPromptBytes, CallTokenLimit - ActionDraftTask.MaximumOutputTokens);
}
