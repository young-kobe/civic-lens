using CivicLens.Application.Analysis;

namespace CivicLens.Application.Briefs;

public sealed record BriefCheckSettings : AnalysisSettings
{
    public BriefCheckSettings(long dailyTokenLimit, int concurrency = DefaultConcurrency)
        : base(dailyTokenLimit, concurrency, BriefCheckPrompt.MinimumReservation) { }

    public int PromptByteCap => (int)Math.Min(BriefCheckTask.MaximumPromptBytes, CallTokenLimit - BriefCheckTask.MaximumOutputTokens);
}
