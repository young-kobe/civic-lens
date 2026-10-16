using CivicLens.Application.Analysis;

namespace CivicLens.Application.Briefs;

public sealed record BriefDraftSettings : AnalysisSettings
{
    public BriefDraftSettings(long dailyTokenLimit, long? runTokenLimit = null, int concurrency = DefaultConcurrency)
        : base(dailyTokenLimit, runTokenLimit, concurrency, BriefDraftPrompt.MinimumReservation) { }

    public int PromptByteCap => (int)Math.Min(BriefDraftTask.MaximumPromptBytes, RunTokenLimit - BriefDraftTask.MaximumOutputTokens);
}
