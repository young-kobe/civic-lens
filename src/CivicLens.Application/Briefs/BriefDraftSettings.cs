using CivicLens.Application.Analysis;

namespace CivicLens.Application.Briefs;

public sealed record BriefDraftSettings : AnalysisSettings
{
    public BriefDraftSettings(long dailyTokenLimit, int concurrency = DefaultConcurrency)
        : base(dailyTokenLimit, concurrency, BriefDraftPrompt.MinimumReservation) { }

    public int PromptByteCap => (int)Math.Min(BriefDraftTask.MaximumPromptBytes, CallTokenLimit - BriefDraftTask.MaximumOutputTokens);
}
